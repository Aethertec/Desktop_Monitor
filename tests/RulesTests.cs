using System.Collections.Generic;

namespace DesktopMonitor.Tests
{
    internal static class RulesTests
    {
        public static void Run()
        {
            Classify_ThresholdsAreInclusive();
            Classify_UsesTheDisplayedRounding();
            ThermalToCelsius_ConvertsAndRejectsEmptyZones();
            Percent_And_Clamp();
            AdapterRate_CountsOnlyAdaptersSeenInBothSamples();
            BatteryPercent_RejectsUnknownCharge();
            TempRingFraction_MapsAndClamps();
            Formatting();
            FormatSpeed_SwitchesUnitsAt1000KB();
            Tooltip_Text();
            IsMostlyOnScreen_Cases();
            DefaultPlacement_TopRight();
        }

        private static void Classify_ThresholdsAreInclusive()
        {
            TestMain.Equal(Level.Ok, Rules.Classify(79.0, 80, 85), "temp 79 ok");
            TestMain.Equal(Level.Amber, Rules.Classify(80.0, 80, 85), "temp 80 amber");
            TestMain.Equal(Level.Amber, Rules.Classify(84.0, 80, 85), "temp 84 amber");
            TestMain.Equal(Level.Red, Rules.Classify(85.0, 80, 85), "temp 85 red");
            TestMain.Equal(Level.Ok, Rules.Classify(69.0, 70, 85), "usage 69 ok");
            TestMain.Equal(Level.Amber, Rules.Classify(70.0, 70, 85), "usage 70 amber");
            TestMain.Equal(Level.Ok, Rules.Classify(89.0, 90, 95), "disk 89 ok");
            TestMain.Equal(Level.Amber, Rules.Classify(90.0, 90, 95), "disk 90 amber");
            TestMain.Equal(Level.Red, Rules.Classify(95.0, 90, 95), "disk 95 red");
            TestMain.Equal(Level.Unknown, Rules.Classify(null, 80, 85), "missing value unknown");
        }

        // Review focus 1: a value shown as "80°" must never be drawn in the OK colour.
        private static void Classify_UsesTheDisplayedRounding()
        {
            TestMain.Equal("79\u00B0", Rules.FormatTemp(79.4, false), "79.4 displays 79");
            TestMain.Equal(Level.Ok, Rules.Classify(79.4, 80, 85), "79.4 classified ok");
            TestMain.Equal("80\u00B0", Rules.FormatTemp(79.5, false), "79.5 displays 80");
            TestMain.Equal(Level.Amber, Rules.Classify(79.5, 80, 85), "79.5 classified amber");
            TestMain.Equal("90%", Rules.FormatPercent(89.6), "disk 89.6 displays 90%");
            TestMain.Equal(Level.Amber, Rules.Classify(89.6, 90, 95), "disk 89.6 classified amber");
        }

        // Review focus 5: an empty thermal zone (273.2 K) must show "--", not "0°C".
        private static void ThermalToCelsius_ConvertsAndRejectsEmptyZones()
        {
            TestMain.Near(86.0, Rules.ThermalToCelsius(3592, true), "high precision tenths of K");
            TestMain.Near(85.8, Rules.ThermalToCelsius(359, false), "plain Kelvin");
            TestMain.Equal<double?>(null, Rules.ThermalToCelsius(2732, true), "empty zone 273.2 K");
            TestMain.Equal<double?>(null, Rules.ThermalToCelsius(0, false), "zero Kelvin");
            TestMain.Equal<double?>(null, Rules.ThermalToCelsius(5000, true), "500 K is not a reading");
        }

        private static void Percent_And_Clamp()
        {
            TestMain.Near(25, Rules.Percent(50, 200), "50 of 200");
            TestMain.Equal<double?>(null, Rules.Percent(1, 0), "zero total");
            TestMain.Near(100, Rules.Percent(150, 100), "over 100 clamps");
            TestMain.Near(0, Rules.ClampPercent(-3), "negative clamps to 0");
            TestMain.Near(100, Rules.ClampPercent(130), "CPU utility above 100 clamps");
            TestMain.Near(0, Rules.ClampPercent(double.NaN), "NaN clamps to 0");
        }

        // Review focus 3 + final review 3: adapters joining, leaving or resetting must never show a negative or absurd speed.
        private static void AdapterRate_CountsOnlyAdaptersSeenInBothSamples()
        {
            TestMain.Near(1000, Rules.AdapterRate(Bytes("wifi", 1000), Bytes("wifi", 3000), 2.0), "steady traffic, 2000 bytes in 2 s");
            var rejoined = Bytes("wifi", 2000);
            rejoined["eth"] = 1796484081;
            TestMain.Near(1000, Rules.AdapterRate(Bytes("wifi", 1000), rejoined, 1.0), "adapter reappearing with 1.7 GB lifetime total adds nothing");
            var withVpn = Bytes("wifi", 1000);
            withVpn["vpn"] = 5000;
            TestMain.Near(500, Rules.AdapterRate(withVpn, Bytes("wifi", 1500), 1.0), "adapter disappearing is ignored");
            TestMain.Near(0, Rules.AdapterRate(Bytes("wifi", 5000), Bytes("wifi", 100), 1.0), "adapter counter reset counts as 0");
            TestMain.Equal<double?>(null, Rules.AdapterRate(Bytes("wifi", 5000), Bytes("eth", 9000), 1.0), "Wi-Fi to Ethernet switch shows -- for one tick");
            TestMain.Equal<double?>(null, Rules.AdapterRate(null, Bytes("wifi", 100), 1.0), "no baseline yet");
            TestMain.Equal<double?>(null, Rules.AdapterRate(Bytes("wifi", 0), Bytes("wifi", 100), 0), "no time elapsed");
        }

        private static Dictionary<string, long> Bytes(string adapter, long total)
        {
            var d = new Dictionary<string, long>();
            d[adapter] = total;
            return d;
        }

        // Review focus 5: unknown charge (255 %) must show "--".
        private static void BatteryPercent_RejectsUnknownCharge()
        {
            TestMain.Near(99, Rules.BatteryPercent(0.99f), "99 %");
            TestMain.Near(0, Rules.BatteryPercent(0f), "empty battery");
            TestMain.Equal<double?>(null, Rules.BatteryPercent(2.55f), "unknown charge");
        }

        private static void TempRingFraction_MapsAndClamps()
        {
            TestMain.Near(0.5, Rules.TempRingFraction(65, 30, 100), "65 is halfway");
            TestMain.Near(0, Rules.TempRingFraction(20, 30, 100), "below min");
            TestMain.Near(1, Rules.TempRingFraction(120, 30, 100), "above max");
            TestMain.Near(0, Rules.TempRingFraction(null, 30, 100), "missing");
            TestMain.Near(0, Rules.TempRingFraction(50, 100, 30), "inverted range");
        }

        private static void Formatting()
        {
            TestMain.Equal("23%", Rules.FormatPercent(23.4), "percent rounds");
            TestMain.Equal("--", Rules.FormatPercent(null), "percent missing");
            TestMain.Equal("77\u00B0C", Rules.FormatTemp(77.2, true), "temp with unit");
            TestMain.Equal("77\u00B0", Rules.FormatTemp(77.2, false), "temp without unit");
            TestMain.Equal("--", Rules.FormatTemp(null, true), "temp missing");
            TestMain.Equal("--", Rules.FormatNumber(null), "number missing");
            TestMain.Equal("100", Rules.FormatNumber(99.5), "number rounds half up");
        }

        private static void FormatSpeed_SwitchesUnitsAt1000KB()
        {
            TestMain.Equal("--", Rules.FormatSpeed(null), "missing");
            TestMain.Equal("0 KB/s", Rules.FormatSpeed(0), "idle");
            TestMain.Equal("1 KB/s", Rules.FormatSpeed(512), "half a KB rounds up");
            TestMain.Equal("999 KB/s", Rules.FormatSpeed(999 * 1024), "999 KB/s");
            TestMain.Equal("1.0 MB/s", Rules.FormatSpeed(999.6 * 1024), "rounds into MB, never 1000 KB/s");
            TestMain.Equal("1.5 MB/s", Rules.FormatSpeed(1.5 * 1048576), "1.5 MB/s");
            TestMain.Equal("12.3 MB/s", Rules.FormatSpeed(12.34 * 1048576), "one decimal");
        }

        private static void Tooltip_Text()
        {
            TestMain.Equal("CPU 23% \u00B7 77\u00B0C", Rules.Tooltip(23.4, 77.2), "tooltip");
            TestMain.Equal("CPU -- \u00B7 --", Rules.Tooltip(null, null), "tooltip missing");
        }

        // Review focus 4: projector unplugged or resolution changed; card must not stay off-screen.
        private static void IsMostlyOnScreen_Cases()
        {
            var laptop = new List<Box> { new Box(0, 0, 1366, 728) };
            TestMain.True(Rules.IsMostlyOnScreen(new Box(1104, 12, 250, 204), laptop), "default spot is on screen");
            TestMain.True(!Rules.IsMostlyOnScreen(new Box(1300, 12, 250, 204), laptop), "mostly past right edge");
            TestMain.True(!Rules.IsMostlyOnScreen(new Box(3000, 100, 250, 204), laptop), "saved on a monitor that is gone");
            var twoScreens = new List<Box> { new Box(0, 0, 1366, 728), new Box(1366, 0, 1920, 1040) };
            TestMain.True(Rules.IsMostlyOnScreen(new Box(1300, 12, 250, 204), twoScreens), "straddling, most on second screen");
            TestMain.True(!Rules.IsMostlyOnScreen(new Box(0, 0, 0, 0), laptop), "zero-size card");
        }

        private static void DefaultPlacement_TopRight()
        {
            Box p = Rules.DefaultPlacement(new Box(0, 0, 1366, 728), 250, 204, 12);
            TestMain.Near(1104, p.X, "x is 12 px from the right edge");
            TestMain.Near(12, p.Y, "y is 12 px from the top");
        }
    }
}
