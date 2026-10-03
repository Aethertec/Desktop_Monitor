using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopMonitor
{
    internal static class Program
    {
        // Outcome of the v1 Task 1 spike (spike\RESULT.md): true = owned by the desktop host window, false = bottom-of-stack only.
        internal static readonly bool UseDesktopOwner = true;

        // Outcome of the v2 Task 1 spike (spike\POWER-RESULT.md): true = the panel may switch the power mode.
        internal static readonly bool PowerSwitchAvailable = true;

        [STAThread]
        private static int Main()
        {
            bool firstInstance;
            using (var mutex = new Mutex(true, @"Local\DesktopMonitor", out firstInstance))
            {
                if (!firstInstance) return 0; // already running
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) => { Log.Write("UI error: " + e.Exception); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Fatal: " + e.ExceptionObject);
                using (var controller = new AppController())
                {
                    controller.Start();
                    app.Run();
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }

    // Wires settings, the sampler thread, card, tray, details panel, alerts and CSV log, and owns their lifetimes.
    internal sealed class AppController : IDisposable
    {
        private static readonly TimeSpan HistoryEvery = TimeSpan.FromSeconds(10);
        private const double ReopenGuardMs = 600;

        private readonly string _settingsPath = SettingsStore.DefaultPath;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly History _history = new History(60, HistoryEvery);
        private readonly AlertEngine _alerts = new AlertEngine();
        private readonly CsvLogWriter _log = new CsvLogWriter(CsvLog.DefaultDir);
        private LogWindow _logWindow = new LogWindow();
        private AppSettings _settings;
        private SettingsWatcher _watcher;
        private SamplerThread _sampler;
        private TrayIcon _tray;
        private CardWindow _window;
        private DetailsPanel _panel;
        private Snapshot _last;
        private PanelData _panelData;
        private bool _exiting;

        public void Start()
        {
            _settings = SettingsStore.LoadOrCreate(_settingsPath);
            _tray = new TrayIcon(_settingsPath);
            _tray.UnlockToggled += delegate { _window.SetUnlocked(!_window.Unlocked); };
            _tray.ExitRequested += Exit;
            _tray.DetailsRequested += TogglePanel;
            ShowCard();
            CreatePanel();
            try
            {
                _watcher = new SettingsWatcher(_settingsPath);
                _watcher.Changed += s => _dispatcher.BeginInvoke(new Action(() => ApplySettings(s)));
            }
            catch (Exception ex)
            {
                Log.Write("Settings: live reload unavailable, " + ex.Message);
            }
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _sampler = new SamplerThread(_settings, _dispatcher, OnSnapshot, OnPanelData);
            _sampler.Start();
        }

        private void ShowCard()
        {
            _window = new CardWindow(_settings, Program.UseDesktopOwner);
            _window.PositionCommitted += SavePosition;
            _window.UnlockedChanged += unlocked => _tray.SetUnlocked(unlocked);
            _window.Closed += delegate
            {
                if (_exiting) return;
                Log.Write("Card window closed unexpectedly, recreating");
                _dispatcher.BeginInvoke(new Action(ShowCard));
            };
            _tray.SetUnlocked(false);
            _window.Show();
            if (_last != null) _window.Card.Update(_last);
            _window.Card.SetHistory(_history.ToArray(), _history.Peak, _history.Capacity);
        }

        private void CreatePanel()
        {
            _panel = new DetailsPanel();
            _panel.IsVisibleChanged += delegate { if (_sampler != null) _sampler.SetPanelOpen(_panel.IsVisible); };
            PanelView v = _panel.View;
            v.CopyRequested += Copy;
            v.PowerModeRequested += SetPowerMode;
            v.LogToggleRequested += ToggleLog;
            v.OpenLogFolderRequested += delegate { Open("explorer.exe", "\"" + EnsureDir(_log.Dir) + "\""); };
            v.TaskManagerRequested += delegate { Open("taskmgr.exe", null); };
            v.PowerSettingsRequested += delegate { Open("ms-settings:powersleep", null); };
        }

        private void OnSnapshot(Snapshot s)
        {
            _last = s;
            Guard.Run("Card", delegate
            {
                _window.Card.Update(s);
                if (_history.Offer(s.Time, s.CpuTempC)) _window.Card.SetHistory(_history.ToArray(), _history.Peak, _history.Capacity);
            });
            Guard.Run("Tray", delegate { _tray.Update(s, _settings); });
            Guard.Run("Alerts", delegate
            {
                foreach (Alert a in _alerts.Evaluate(s, _settings))
                {
                    _tray.ShowAlert(a);
                    _log.AppendAlert(a);
                }
            });
            Guard.Run("CSV log", delegate { LogRow(s); });
            if (_panel.IsVisible) Guard.Run("Panel", RefreshPanel);
        }

        private void OnPanelData(PanelData d)
        {
            _panelData = d;
            if (_panel.IsVisible) Guard.Run("Panel", RefreshPanel);
        }

        private void LogRow(Snapshot s)
        {
            if (!_settings.LogEnabled)
            {
                _logWindow = new LogWindow();
                return;
            }
            _logWindow.Add(s);
            TimeSpan every = TimeSpan.FromSeconds(_settings.LogIntervalSeconds);
            if (!_logWindow.Due(s.Time, every)) return;
            _log.Append(s.Time, _logWindow.ToRow(PowerMode.Name(PowerMode.Get())), (int)_settings.LogRetentionDays);
            _logWindow.Next(every);
        }

        private void RefreshPanel()
        {
            _panel.View.Update(_last ?? new Snapshot(), _panelData, _alerts.Recent(DateTime.Now), _settings, Program.PowerSwitchAvailable);
        }

        private void TogglePanel()
        {
            if (_panel.IsVisible)
            {
                _panel.Hide();
                return;
            }
            if ((DateTime.Now - _panel.LastAutoHide).TotalMilliseconds < ReopenGuardMs) return; // that double-click was to close it
            RefreshPanel();
            Box card = _window.Bounds;
            _panel.ShowBeside(card, CardWindow.WorkAreaContaining(card));
        }

        private void Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                _panel.View.ShowCopied(text);
            }
            catch (Exception ex)
            {
                Log.Write("Panel: could not copy to the clipboard, " + ex.Message);
            }
        }

        private void SetPowerMode(PowerModeKind mode)
        {
            if (!PowerMode.Set(mode)) Log.Write("Panel: Windows refused power mode " + mode);
            if (_panelData == null) _panelData = new PanelData();
            _panelData.PowerMode = PowerMode.Get(); // show what Windows actually applied
            RefreshPanel();
        }

        private void ToggleLog()
        {
            AppSettings s = _settings.Clone();
            s.LogEnabled = !s.LogEnabled;
            _settings = s;
            Save(s, "log setting");
            RefreshPanel();
        }

        // Thresholds, opacity, zones, alerts and log settings apply live; x/y are only read at start-up.
        private void ApplySettings(AppSettings s)
        {
            _settings = s;
            _sampler.ApplySettings(s);
            _window.ApplySettings(s);
            if (_panel.IsVisible) RefreshPanel();
        }

        private void SavePosition(double x, double y)
        {
            AppSettings s = _settings.Clone();
            s.X = Math.Round(x);
            s.Y = Math.Round(y);
            _settings = s;
            Save(s, "position");
        }

        private void Save(AppSettings s, string what)
        {
            try
            {
                SettingsStore.Save(_settingsPath, s);
            }
            catch (Exception ex)
            {
                Log.Write("Settings: could not save " + what + ", " + ex.Message);
            }
        }

        private static string EnsureDir(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Open(string target, string args)
        {
            try
            {
                if (args == null) Process.Start(target);
                else Process.Start(target, args);
            }
            catch (Exception ex)
            {
                Log.Write("Panel: could not open " + target + ", " + ex.Message);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) _sampler.RequestReset();
        }

        private void Exit()
        {
            _exiting = true;
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _exiting = true;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (_sampler != null) _sampler.Dispose();
            if (_watcher != null) _watcher.Dispose();
            if (_tray != null) _tray.Dispose();
        }
    }
}
