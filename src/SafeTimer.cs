using System;
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
}
