using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace DesktopMonitor
{
    // Data only the details panel needs; read every 5 s, and only while the panel is open.
    public sealed class PanelData
    {
        public List<AdapterInfo> Adapters = new List<AdapterInfo>();
        public PowerModeKind PowerMode;
    }

    // Runs MetricsSampler once a second on a background thread and posts each Snapshot to the UI thread,
    // so slow readers (network enumeration took up to ~400 ms in v1) never stall the card or the desktop.
    internal sealed class SamplerThread : IDisposable
    {
        private const double PanelEvery = 5; // seconds
        private readonly Dispatcher _ui;
        private readonly Action<Snapshot> _onSnapshot;
        private readonly Action<PanelData> _onPanelData;
        private readonly Thread _thread;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly object _gate = new object();
        private AppSettings _settings, _pendingSettings;
        private bool _resetRequested, _panelOpen, _panelDue;

        public SamplerThread(AppSettings settings, Dispatcher ui, Action<Snapshot> onSnapshot, Action<PanelData> onPanelData)
        {
            _settings = settings;
            _ui = ui;
            _onSnapshot = onSnapshot;
            _onPanelData = onPanelData;
            _thread = new Thread(Run) { IsBackground = true, Name = "Sampler" };
        }

        public void Start()
        {
            _thread.Start();
        }

        public void ApplySettings(AppSettings settings)
        {
            lock (_gate) _pendingSettings = settings;
        }

        public void RequestReset()
        {
            lock (_gate) _resetRequested = true;
        }

        // Opening the panel asks for panel data on the very next tick.
        public void SetPanelOpen(bool open)
        {
            lock (_gate)
            {
                _panelOpen = open;
                if (open) _panelDue = true;
            }
        }

        private void Run()
        {
            var clock = Stopwatch.StartNew();
            double nextTick = 0, nextPanel = 0;
            using (var sampler = new MetricsSampler(_settings))
            {
                while (true)
                {
                    double wait = Math.Max(0, nextTick - clock.Elapsed.TotalSeconds);
                    if (_stop.WaitOne(TimeSpan.FromSeconds(wait))) break;
                    nextTick = Math.Max(nextTick + 1, clock.Elapsed.TotalSeconds); // no burst of catch-up ticks after a stall
                    try
                    {
                        AppSettings pending;
                        bool reset, panelOpen, panelDue;
                        lock (_gate)
                        {
                            pending = _pendingSettings;
                            _pendingSettings = null;
                            reset = _resetRequested;
                            _resetRequested = false;
                            panelOpen = _panelOpen;
                            panelDue = _panelDue;
                            _panelDue = false;
                        }
                        if (pending != null) sampler.ApplySettings(pending);
                        if (reset) sampler.Reset();
                        _ui.BeginInvoke(_onSnapshot, sampler.Sample());
                        double now = clock.Elapsed.TotalSeconds;
                        if (panelOpen && (panelDue || now >= nextPanel))
                        {
                            nextPanel = now + PanelEvery;
                            var data = new PanelData();
                            try { data.Adapters = NetworkInfo.Read(); }
                            catch (Exception ex) { Log.Write("Panel: network details unavailable, " + ex.Message); }
                            data.PowerMode = PowerMode.Get();
                            _ui.BeginInvoke(_onPanelData, data);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Sampler thread: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }

        public void Dispose()
        {
            _stop.Set();
            if (_thread.IsAlive) _thread.Join(3000);
        }
    }
}
