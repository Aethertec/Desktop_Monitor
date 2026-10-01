using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopMonitor
{
    internal static class Program
    {
        // Outcome of the Task 1 spike (spike\RESULT.md): true = owned by the desktop host window, false = bottom-of-stack only.
        internal static readonly bool UseDesktopOwner = true;

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

    // Wires settings, sampler, card window and tray icon together and owns their lifetimes.
    internal sealed class AppController : IDisposable
    {
        private readonly string _settingsPath = SettingsStore.DefaultPath;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private AppSettings _settings;
        private SettingsWatcher _watcher;
        private MetricsSampler _sampler;
        private TrayIcon _tray;
        private CardWindow _window;
        private DispatcherTimer _timer;
        private bool _exiting;

        public void Start()
        {
            _settings = SettingsStore.LoadOrCreate(_settingsPath);
            _sampler = new MetricsSampler(_settings);
            _tray = new TrayIcon(_settingsPath);
            _tray.UnlockToggled += delegate { _window.SetUnlocked(!_window.Unlocked); };
            _tray.ExitRequested += Exit;
            ShowCard();
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
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Tick(); };
            _timer.Start();
            Tick();
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
        }

        private void Tick()
        {
            Snapshot s = _sampler.Sample();
            _window.Card.Update(s);
            _tray.Update(s, _settings);
        }

        // Thresholds, opacity and zones apply live; x/y are only read at start-up.
        private void ApplySettings(AppSettings s)
        {
            _settings = s;
            _sampler.ApplySettings(s);
            _window.ApplySettings(s);
        }

        private void SavePosition(double x, double y)
        {
            AppSettings s = _settings.Clone();
            s.X = Math.Round(x);
            s.Y = Math.Round(y);
            _settings = s;
            try
            {
                SettingsStore.Save(_settingsPath, s);
            }
            catch (Exception ex)
            {
                Log.Write("Settings: could not save position, " + ex.Message);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) _dispatcher.BeginInvoke(new Action(() => _sampler.Reset()));
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
            if (_timer != null) _timer.Stop();
            if (_watcher != null) _watcher.Dispose();
            if (_tray != null) _tray.Dispose();
            if (_sampler != null) _sampler.Dispose();
        }
    }
}
