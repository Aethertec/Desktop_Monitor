using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace DesktopMonitor
{
    // DispatcherTimer whose action can throw without stopping it. WPF does not re-arm a DispatcherTimer whose Tick
    // threw, even when DispatcherUnhandledException marks the exception handled, which would freeze the card.
    internal sealed class SafeTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly string _name;
        private readonly Action _action;
        private string _lastError;

        public SafeTimer(TimeSpan interval, string name, Action action)
        {
            _name = name;
            _action = action;
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += delegate { RunNow(); };
        }

        public void Start() { _timer.Start(); }

        public void Stop() { _timer.Stop(); }

        public void RunNow()
        {
            try
            {
                _action();
                _lastError = null;
            }
            catch (Exception ex)
            {
                string error = ex.GetType().Name + ": " + ex.Message;
                if (error != _lastError) Log.Write(_name + " failed, " + error); // once per distinct error, not every tick
                _lastError = error;
            }
        }
    }

    // Runs one part of the UI update so that its failure cannot stop the others (card, tray, alerts, log, panel).
    // UI thread only; logs once per distinct error per part.
    internal static class Guard
    {
        private static readonly Dictionary<string, string> LastError = new Dictionary<string, string>();

        public static void Run(string name, Action action)
        {
            try
            {
                action();
                LastError.Remove(name);
            }
            catch (Exception ex)
            {
                string error = ex.GetType().Name + ": " + ex.Message;
                string last;
                if (!LastError.TryGetValue(name, out last) || last != error) Log.Write(name + " failed, " + error);
                LastError[name] = error;
            }
        }
    }
}
