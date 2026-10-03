using System;

namespace DesktopMonitor.Tests
{
    internal static class SettingsV2Tests
    {
        public static void Run()
        {
            SettingsV2_DefaultsAndParsing();
            LoadOrCreate_UpgradesAV1FileOnce();
        }

        // Final review I2: a v1 settings.json (14 keys) must gain the v2 keys so they can be edited, keeping the user's values.
        private static void LoadOrCreate_UpgradesAV1FileOnce()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DesktopMonitorTests-" + Guid.NewGuid().ToString("N"));
            string path = System.IO.Path.Combine(dir, "settings.json");
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(path, @"{""x"": 1096, ""y"": 15, ""opacity"": 0.8, ""usageAmber"": 70, ""usageRed"": 85, ""tempAmber"": 80, ""tempRed"": 85,
                    ""diskAmber"": 90, ""diskRed"": 95, ""tempRingMin"": 30, ""tempRingMax"": 100, ""zoneCpu"": ""CPUZ"", ""zoneSkin"": ""SK1Z"", ""zoneBattery"": ""BATZ""}");
                AppSettings s = SettingsStore.LoadOrCreate(path);
                string upgraded = System.IO.File.ReadAllText(path);
                TestMain.True(upgraded.Contains("\"alertComPortsOn\"") && upgraded.Contains("\"logIntervalSeconds\""), "v2 keys were added to the file");
                TestMain.Near(0.8, SettingsStore.Parse(upgraded).Opacity, "the user's own values are kept");
                TestMain.Near(1096, s.X, "position kept");
                SettingsStore.LoadOrCreate(path);
                TestMain.Equal(upgraded, System.IO.File.ReadAllText(path), "a complete file is not rewritten again");
            }
            finally
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }

        private static void SettingsV2_DefaultsAndParsing()
        {
            AppSettings d = SettingsStore.Parse("{}");
            TestMain.True(d.LogEnabled && d.AlertsEnabled && d.AlertComPortsOn, "log and alerts on by default");
            TestMain.Near(10, d.LogIntervalSeconds, "log every 10 s");
            TestMain.Near(30, d.LogRetentionDays, "keep 30 days");
            TestMain.Near(90, d.AlertCpuTemp, "CPU hot at 90");
            TestMain.Near(85, d.AlertCpuClear, "clears below 85");
            TestMain.Near(120, d.AlertThrottleSeconds, "throttled after 2 min");
            TestMain.Near(12, d.AlertDiskClearGb, "disk clears above 12 GB");
            AppSettings s = SettingsStore.Parse(@"{""logEnabled"": false, ""alertCpuHotOn"": ""yes"", ""logIntervalSeconds"": 1, ""logRetentionDays"": 0, ""alertCpuTemp"": 88}");
            TestMain.True(!s.LogEnabled, "false is read");
            TestMain.True(s.AlertCpuHotOn, "\"yes\" is not a JSON bool: default kept");
            TestMain.Near(5, s.LogIntervalSeconds, "interval clamped to at least 5 s");
            TestMain.Near(1, s.LogRetentionDays, "retention clamped to at least 1 day");
            TestMain.Near(88, s.AlertCpuTemp, "number read");
            AppSettings back = SettingsStore.Parse(SettingsStore.ToJson(s));
            TestMain.True(!back.LogEnabled && Math.Abs(back.AlertCpuTemp - 88) < 1e-9, "new keys survive a save and reload");
        }
    }
}
