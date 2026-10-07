using System;
using System.IO;

namespace DesktopMonitor.Tests
{
    internal static class SettingsTests
    {
        public static void Run()
        {
            Parse_EmptyObjectGivesDefaults();
            Parse_ReadsEveryKey();
            Parse_KeysAreCaseInsensitive();
            Parse_DuplicateKeysDifferingInCase_LastWins();
            Parse_WrongTypeFallsBackToDefault();
            Parse_ClampsOutOfRangeNumbers();
            Parse_NonFiniteNumbersFallBackToDefault();
            Parse_InvertedRingRangeUsesDefaults();
            Parse_MalformedJsonThrowsFormatException();
            ToJson_RoundTrips();
            LoadOrCreate_WritesDefaultsWhenMissing();
            LoadOrCreate_MalformedFileGivesDefaultsAndLeavesFileAlone();
        }

        private static void Parse_EmptyObjectGivesDefaults()
        {
            AppSettings s = SettingsStore.Parse("{}");
            TestMain.Equal<double?>(null, s.X, "x default null");
            TestMain.Equal<double?>(null, s.Y, "y default null");
            TestMain.Near(0.93, s.Opacity, "opacity default");
            TestMain.Near(70, s.UsageAmber, "usageAmber default");
            TestMain.Near(85, s.UsageRed, "usageRed default");
            TestMain.Near(80, s.TempAmber, "tempAmber default");
            TestMain.Near(85, s.TempRed, "tempRed default");
            TestMain.Near(90, s.DiskAmber, "diskAmber default");
            TestMain.Near(95, s.DiskRed, "diskRed default");
            TestMain.Near(30, s.TempRingMin, "tempRingMin default");
            TestMain.Near(100, s.TempRingMax, "tempRingMax default");
            TestMain.Equal("CPUZ", s.ZoneCpu, "zoneCpu default");
            TestMain.Equal("SK1Z", s.ZoneSkin, "zoneSkin default");
            TestMain.Equal("BATZ", s.ZoneBattery, "zoneBattery default");
        }

        private static void Parse_ReadsEveryKey()
        {
            AppSettings s = SettingsStore.Parse(@"{""x"": 100.5, ""y"": 40, ""opacity"": 0.8,
                ""usageAmber"": 60, ""usageRed"": 90, ""tempAmber"": 75, ""tempRed"": 88,
                ""diskAmber"": 80, ""diskRed"": 97, ""tempRingMin"": 20, ""tempRingMax"": 110,
                ""zoneCpu"": ""LOCZ"", ""zoneSkin"": ""EXTZ"", ""zoneBattery"": ""CHGZ""}");
            TestMain.Near(100.5, s.X, "x");
            TestMain.Near(40, s.Y, "y");
            TestMain.Near(0.8, s.Opacity, "opacity");
            TestMain.Near(60, s.UsageAmber, "usageAmber");
            TestMain.Near(90, s.UsageRed, "usageRed");
            TestMain.Near(75, s.TempAmber, "tempAmber");
            TestMain.Near(88, s.TempRed, "tempRed");
            TestMain.Near(80, s.DiskAmber, "diskAmber");
            TestMain.Near(97, s.DiskRed, "diskRed");
            TestMain.Near(20, s.TempRingMin, "tempRingMin");
            TestMain.Near(110, s.TempRingMax, "tempRingMax");
            TestMain.Equal("LOCZ", s.ZoneCpu, "zoneCpu");
            TestMain.Equal("EXTZ", s.ZoneSkin, "zoneSkin");
            TestMain.Equal("CHGZ", s.ZoneBattery, "zoneBattery");
        }

        private static void Parse_KeysAreCaseInsensitive()
        {
            AppSettings s = SettingsStore.Parse(@"{""TempAmber"": 78, ""ZONECPU"": ""LOCZ""}");
            TestMain.Near(78, s.TempAmber, "TempAmber");
            TestMain.Equal("LOCZ", s.ZoneCpu, "ZONECPU");
        }

        // Final review 1: "opacity" plus "Opacity" threw ArgumentException, which escaped the watcher's timer and killed the app.
        private static void Parse_DuplicateKeysDifferingInCase_LastWins()
        {
            AppSettings s;
            try
            {
                s = SettingsStore.Parse(@"{""opacity"": 0.8, ""Opacity"": 0.5, ""tempAmber"": 78}");
            }
            catch (Exception ex)
            {
                TestMain.True(false, "duplicate keys differing in case must not throw (got " + ex.GetType().Name + ")");
                return;
            }
            TestMain.Near(0.5, s.Opacity, "last duplicate wins");
            TestMain.Near(78, s.TempAmber, "other keys still read");
        }

        // Review focus 2: a typo in one key must not reset or break the others.
        private static void Parse_WrongTypeFallsBackToDefault()
        {
            AppSettings s = SettingsStore.Parse(@"{""tempAmber"": ""eighty"", ""tempRed"": 90, ""x"": ""left"", ""zoneCpu"": 5, ""zoneSkin"": ""  ""}");
            TestMain.Near(80, s.TempAmber, "string where number expected");
            TestMain.Near(90, s.TempRed, "neighbouring key still read");
            TestMain.Equal<double?>(null, s.X, "non-numeric x");
            TestMain.Equal("CPUZ", s.ZoneCpu, "number where string expected");
            TestMain.Equal("SK1Z", s.ZoneSkin, "blank zone");
        }

        // JavaScriptSerializer accepts the bare literals NaN and Infinity; opacity NaN would make the card invisible.
        private static void Parse_NonFiniteNumbersFallBackToDefault()
        {
            AppSettings s = SettingsStore.Parse(@"{""opacity"": NaN, ""usageRed"": Infinity, ""x"": NaN, ""y"": -Infinity}");
            TestMain.Near(0.93, s.Opacity, "opacity NaN keeps default");
            TestMain.Near(85, s.UsageRed, "usageRed Infinity keeps default");
            TestMain.Equal<double?>(null, s.X, "x NaN is unset");
            TestMain.Equal<double?>(null, s.Y, "y -Infinity is unset");
        }

        private static void Parse_ClampsOutOfRangeNumbers()
        {
            AppSettings s = SettingsStore.Parse(@"{""opacity"": 1.5, ""usageRed"": 120, ""diskAmber"": -5, ""tempRed"": 400}");
            TestMain.Near(1.0, s.Opacity, "opacity max 1");
            TestMain.Near(100, s.UsageRed, "usage max 100");
            TestMain.Near(0, s.DiskAmber, "disk min 0");
            TestMain.Near(150, s.TempRed, "temp max 150");
            TestMain.Near(0.2, SettingsStore.Parse(@"{""opacity"": 0}").Opacity, "opacity min 0.2 so the card never vanishes");
        }

        private static void Parse_InvertedRingRangeUsesDefaults()
        {
            AppSettings s = SettingsStore.Parse(@"{""tempRingMin"": 90, ""tempRingMax"": 40}");
            TestMain.Near(30, s.TempRingMin, "ring min reset");
            TestMain.Near(100, s.TempRingMax, "ring max reset");
        }

        // Review focus 2: a half-saved or broken file is rejected as a whole so the caller keeps the last good settings.
        private static void Parse_MalformedJsonThrowsFormatException()
        {
            TestMain.Throws<FormatException>(() => SettingsStore.Parse("{ \"tempAmber\": 80,"), "truncated file");
            TestMain.Throws<FormatException>(() => SettingsStore.Parse("tempAmber = 80"), "not JSON");
            TestMain.Throws<FormatException>(() => SettingsStore.Parse("[1, 2]"), "array instead of object");
            TestMain.Throws<FormatException>(() => SettingsStore.Parse(""), "empty file");
        }

        private static void ToJson_RoundTrips()
        {
            var original = new AppSettings();
            original.X = 1104;
            original.Y = 12.5;
            original.Opacity = 0.85;
            original.TempAmber = 78;
            original.ZoneCpu = "A\"B";
            AppSettings back = SettingsStore.Parse(SettingsStore.ToJson(original));
            TestMain.Near(1104, back.X, "x");
            TestMain.Near(12.5, back.Y, "y");
            TestMain.Near(0.85, back.Opacity, "opacity");
            TestMain.Near(78, back.TempAmber, "tempAmber");
            TestMain.Equal("A\"B", back.ZoneCpu, "quote escaped");
            TestMain.True(SettingsStore.ToJson(new AppSettings()).Contains("\"x\": null"), "default x written as null");
        }

        private static void LoadOrCreate_WritesDefaultsWhenMissing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "DesktopMonitorTests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "settings.json");
            try
            {
                AppSettings s = SettingsStore.LoadOrCreate(path);
                TestMain.Near(80, s.TempAmber, "defaults returned");
                TestMain.True(File.Exists(path), "file created");
                TestMain.Near(90, SettingsStore.Parse(File.ReadAllText(path)).DiskAmber, "created file parses");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        private static void LoadOrCreate_MalformedFileGivesDefaultsAndLeavesFileAlone()
        {
            string dir = Path.Combine(Path.GetTempPath(), "DesktopMonitorTests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "settings.json");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "{ broken");
                AppSettings s = SettingsStore.LoadOrCreate(path);
                TestMain.Near(0.93, s.Opacity, "defaults used");
                TestMain.Equal("{ broken", File.ReadAllText(path), "user's file not overwritten");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
