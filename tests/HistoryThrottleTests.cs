using System;

namespace DesktopMonitor.Tests
{
    internal static class HistoryThrottleTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 3, 14, 0, 0);

        public static void Run()
        {
            History_AddsOnePointPerInterval();
            History_KeepsTheLastCapacityPointsInOrder();
            History_PeakIgnoresGaps();
            Throttle_NeedsTwoReadsToShowAndOneToHide();
            Throttle_FullSpeedBoundary();
            Throttle_Reason();
        }

        private static void History_AddsOnePointPerInterval()
        {
            var h = new History(60, TimeSpan.FromSeconds(10));
            TestMain.True(h.Offer(T0, 70), "first point is added");
            TestMain.True(!h.Offer(T0.AddSeconds(5), 71), "5 s later is too early");
            TestMain.True(h.Offer(T0.AddSeconds(10), 72), "10 s later is added");
            TestMain.Equal(2, h.Count, "two points");
            double?[] a = h.ToArray();
            TestMain.Near(70, a[0], "oldest first");
            TestMain.Near(72, a[1], "newest last");
        }

        private static void History_KeepsTheLastCapacityPointsInOrder()
        {
            var h = new History(3, TimeSpan.FromSeconds(10));
            for (int i = 0; i < 5; i++) h.Offer(T0.AddSeconds(10 * i), 60 + i);
            double?[] a = h.ToArray();
            TestMain.Equal(3, a.Length, "capped at capacity");
            TestMain.Near(62, a[0], "oldest kept is the third point");
            TestMain.Near(64, a[2], "newest is the fifth point");
        }

        private static void History_PeakIgnoresGaps()
        {
            var h = new History(60, TimeSpan.FromSeconds(10));
            TestMain.Equal<double?>(null, h.Peak, "empty history has no peak");
            h.Offer(T0, 70);
            h.Offer(T0.AddSeconds(10), null);
            h.Offer(T0.AddSeconds(20), 80);
            TestMain.Near(80, h.Peak, "peak of 70, gap, 80");
            TestMain.Equal<double?>(null, h.ToArray()[1], "missing reading kept as a gap");
        }

        private static void Throttle_NeedsTwoReadsToShowAndOneToHide()
        {
            var t = new Throttle();
            t.Feed(72);
            TestMain.True(!t.Shown, "one read below full speed is not enough");
            t.Feed(72);
            TestMain.True(t.Shown, "two consecutive reads show the pill");
            t.Feed(null);
            TestMain.True(t.Shown, "a missing read changes nothing");
            t.Feed(100);
            TestMain.True(!t.Shown, "one read at full speed hides it");
            t.Feed(72);
            t.Feed(100);
            t.Feed(72);
            TestMain.True(!t.Shown, "reads that are not consecutive do not show it");
        }

        private static void Throttle_FullSpeedBoundary()
        {
            TestMain.True(!Rules.IsBelowFullSpeed(99.5), "99.5 displays as 100%, so it is full speed");
            TestMain.True(Rules.IsBelowFullSpeed(99.4), "99.4 displays as 99%, so it is below");
        }

        private static void Throttle_Reason()
        {
            TestMain.Equal("heat", Throttle.Reason(86, 85, true), "hot CPU on AC");
            TestMain.Equal("heat", Throttle.Reason(86, 85, false), "heat wins over battery");
            TestMain.Equal("power saving", Throttle.Reason(60, 85, false), "cool CPU on battery");
            TestMain.Equal("power saving", Throttle.Reason(null, 85, false), "no temperature, on battery");
            TestMain.Equal("limit", Throttle.Reason(60, 85, true), "cool CPU on AC");
        }
    }
}
