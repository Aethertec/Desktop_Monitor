using System;

namespace DesktopMonitor.Tests
{
    internal static class RulesV2Tests
    {
        private const double Gb = 1024.0 * 1024 * 1024;

        public static void Run()
        {
            PanelPlacement_LeftRightAndClamp();
            FormatCpuSpeed_Cases();
            FormatDiskFree_GbAndTb();
            FormatMemory_MbAndGb();
            FormatBattery_Cases();
            FormatDuration_Cases();
        }

        private static void PanelPlacement_LeftRightAndClamp()
        {
            var wa = new Box(0, 0, 1366, 720);
            Box left = Rules.PanelPlacement(new Box(1104, 12, 250, 282), wa, 300, 480, 10);
            TestMain.Near(794, left.X, "card at top-right: panel 10 px to its left");
            TestMain.Near(12, left.Y, "top-aligned with the card");
            Box right = Rules.PanelPlacement(new Box(20, 12, 250, 282), wa, 300, 480, 10);
            TestMain.Near(280, right.X, "card at the left edge: panel to its right");
            Box low = Rules.PanelPlacement(new Box(1104, 400, 250, 282), wa, 300, 480, 10);
            TestMain.Near(240, low.Y, "card low on screen: panel pushed up to stay inside");
        }

        private static void FormatCpuSpeed_Cases()
        {
            TestMain.Equal("Full speed", Rules.FormatCpuSpeed(100, "heat"), "full speed");
            TestMain.Equal("Limited to 72% \u00B7 heat", Rules.FormatCpuSpeed(72, "heat"), "limited");
            TestMain.Equal("--", Rules.FormatCpuSpeed(null, "heat"), "missing");
        }

        private static void FormatDiskFree_GbAndTb()
        {
            TestMain.Equal("36 GB", Rules.FormatDiskFree(36 * Gb), "36 GB");
            TestMain.Equal("999 GB", Rules.FormatDiskFree(999 * Gb), "999 GB");
            TestMain.Equal("1.2 TB", Rules.FormatDiskFree(1229 * Gb), "TB with one decimal");
            TestMain.Equal("--", Rules.FormatDiskFree(null), "missing");
        }

        private static void FormatMemory_MbAndGb()
        {
            TestMain.Equal("820 MB", Rules.FormatMemory(820L * 1024 * 1024), "MB");
            TestMain.Equal("1.9 GB", Rules.FormatMemory((long)(1.9 * Gb)), "GB with one decimal");
        }

        private static void FormatBattery_Cases()
        {
            TestMain.Equal("Plugged in \u00B7 100%", Rules.FormatBattery(100, true, false, null), "full on AC");
            TestMain.Equal("Charging \u00B7 87%", Rules.FormatBattery(87, true, true, null), "charging");
            TestMain.Equal("1 h 45 min left \u00B7 62%", Rules.FormatBattery(62, false, false, 105), "hours and minutes");
            TestMain.Equal("45 min left \u00B7 30%", Rules.FormatBattery(30, false, false, 45), "minutes only");
            TestMain.Equal("On battery \u00B7 50%", Rules.FormatBattery(50, false, false, null), "time not known yet");
            TestMain.Equal("--", Rules.FormatBattery(null, true, false, null), "no battery reading");
        }

        private static void FormatDuration_Cases()
        {
            TestMain.Equal("1 min", Rules.FormatDuration(60), "60 s");
            TestMain.Equal("2 min", Rules.FormatDuration(120), "120 s");
            TestMain.Equal("90 s", Rules.FormatDuration(90), "90 s");
            TestMain.Equal("10 s", Rules.FormatDuration(10), "10 s");
        }

    }
}
