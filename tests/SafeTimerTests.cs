using System;
using System.Windows.Threading;

namespace DesktopMonitor.Tests
{
    internal static class SafeTimerTests
    {
        public static void Run()
        {
            KeepsTickingAfterAnException();
        }

        // Final review 2: a throwing Tick stopped the DispatcherTimer for good, freezing the card on stale numbers,
        // even though DispatcherUnhandledException marked the exception handled.
        private static void KeepsTickingAfterAnException()
        {
            Dispatcher d = Dispatcher.CurrentDispatcher;
            DispatcherUnhandledExceptionEventHandler swallow = (s, e) => e.Handled = true;
            d.UnhandledException += swallow;
            int ticks = 0;
            var timer = new SafeTimer(TimeSpan.FromMilliseconds(50), "test", delegate
            {
                ticks++;
                if (ticks == 1) throw new InvalidOperationException("boom");
            });
            timer.Start();
            var frame = new DispatcherFrame();
            var stop = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Normal, delegate { frame.Continue = false; }, d);
            Dispatcher.PushFrame(frame);
            stop.Stop();
            timer.Stop();
            d.UnhandledException -= swallow;
            TestMain.True(ticks >= 3, "timer kept ticking after a throwing tick (ticks=" + ticks + ")");
        }
    }
}
