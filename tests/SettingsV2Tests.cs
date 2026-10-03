using System;

namespace DesktopMonitor.Tests
{
    internal static class SettingsV2Tests
    {
        public static void Run()
        {
            SettingsV2_DefaultsAndParsing();
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
