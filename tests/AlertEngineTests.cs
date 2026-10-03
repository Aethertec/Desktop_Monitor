using System;
using System.Collections.Generic;

namespace DesktopMonitor.Tests
{
    internal static class AlertEngineTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 3, 14, 0, 0);
        private const double Gb = 1024.0 * 1024 * 1024;

        public static void Run()
        {
            CpuHot_FiresAfterTheHoldTime_Once();
            CpuHot_ReArmsOnlyBelowTheClearTemperature();
            CpuHot_MissingReadingRestartsTheTiming();
            Throttled_FiresAfterTwoMinutes();
            DiskLow_FiresAtOnce_AndReArmsAboveTheClearLevel();
            BatteryLow_OnlyOnBattery_ReArmsWhenPluggedIn();
            BatteryHot_FiresAfterTheHoldTime();
            ComPort_PresentAtStartNeverAlerts();
            ComPort_WithName_AlertsOnce_AndAgainAfterReplug();
            ComPort_WithoutName_WaitsThreeSeconds();
            ComPort_LookupFinishedWithoutName_AlertsAtOnce();
            Switches_TurnAlertsOff();
            Recent_IsTodayNewestFirstAtMostTen();
        }

        // A calm snapshot: everything well inside its limits.
        private static Snapshot Calm(DateTime t)
        {
            return new Snapshot
            {
                Time = t, CpuTempC = 60, BatteryTempC = 35, CpuLimitPercent = 100,
                DiskFreeBytes = 50 * Gb, BatteryPercent = 80, OnAc = true
            };
        }

        private static List<Alert> Run(AlertEngine e, AppSettings st, Snapshot s)
        {
            return e.Evaluate(s, st);
        }

        // Feeds one snapshot per second from `from` for `seconds` seconds; returns how many alerts of `kind` fired.
        private static int Feed(AlertEngine e, AppSettings st, DateTime from, int seconds, Func<DateTime, Snapshot> make, string kind)
        {
            int n = 0;
            for (int i = 0; i < seconds; i++)
                foreach (Alert a in e.Evaluate(make(from.AddSeconds(i)), st))
                    if (a.Kind == kind) n++;
            return n;
        }

        private static Snapshot Hot(DateTime t, double temp)
        {
            Snapshot s = Calm(t);
            s.CpuTempC = temp;
            return s;
        }

        private static void CpuHot_FiresAfterTheHoldTime_Once()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            TestMain.Equal(0, Feed(e, st, T0, 60, t => Hot(t, 92), "cpu_hot"), "not within the first 59 s at 92 \u00B0C");
            List<Alert> fired = Run(e, st, Hot(T0.AddSeconds(60), 92));
            TestMain.Equal(1, fired.Count, "fires at 60 s");
            TestMain.Equal("CPU hot", fired[0].Title, "title");
            TestMain.Equal("92\u00B0C for 1 min", fired[0].Body, "body");
            TestMain.Equal(Level.Red, fired[0].Severity, "severity");
            TestMain.Equal(0, Feed(e, st, T0.AddSeconds(61), 300, t => Hot(t, 92), "cpu_hot"), "no repeat while it stays hot");
        }

        private static void CpuHot_ReArmsOnlyBelowTheClearTemperature()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Feed(e, st, T0, 61, t => Hot(t, 92), "cpu_hot");
            TestMain.Equal(0, Feed(e, st, T0.AddSeconds(61), 120, t => Hot(t, 87), "cpu_hot"), "87 \u00B0C is not below 85: still the same episode");
            Feed(e, st, T0.AddSeconds(181), 61, t => Hot(t, 80), "cpu_hot"); // a minute below 85 re-arms
            TestMain.Equal(1, Feed(e, st, T0.AddSeconds(242), 61, t => Hot(t, 92), "cpu_hot"), "a new hot minute fires again");
        }

        private static void CpuHot_MissingReadingRestartsTheTiming()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Feed(e, st, T0, 30, t => Hot(t, 92), "cpu_hot");
            Snapshot gap = Hot(T0.AddSeconds(30), 92);
            gap.CpuTempC = null;
            Run(e, st, gap);
            TestMain.Equal(0, Feed(e, st, T0.AddSeconds(31), 60, t => Hot(t, 92), "cpu_hot"), "30 s + gap + 59 s is not a full minute");
            TestMain.Equal(1, Feed(e, st, T0.AddSeconds(91), 1, t => Hot(t, 92), "cpu_hot"), "a full minute after the first reading after the gap");
        }

        private static void Throttled_FiresAfterTwoMinutes()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Func<DateTime, Snapshot> limited = t => { Snapshot s = Calm(t); s.CpuLimitPercent = 72; return s; };
            TestMain.Equal(0, Feed(e, st, T0, 120, limited, "throttled"), "not within 119 s");
            List<Alert> fired = Run(e, st, limited(T0.AddSeconds(120)));
            TestMain.Equal("CPU held at 72% of full speed for 2 min", fired.Count > 0 ? fired[0].Body : "(none)", "fires at 120 s with the limit in the text");
            Run(e, st, Calm(T0.AddSeconds(121))); // back at full speed re-arms at once
            TestMain.Equal(1, Feed(e, st, T0.AddSeconds(122), 121, limited, "throttled"), "a new 2-minute episode fires again");
        }

        private static void DiskLow_FiresAtOnce_AndReArmsAboveTheClearLevel()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Func<DateTime, double, Snapshot> disk = (t, gb) => { Snapshot s = Calm(t); s.DiskFreeBytes = gb * Gb; return s; };
            List<Alert> fired = Run(e, st, disk(T0, 9));
            TestMain.Equal("C: has 9 GB free", fired.Count > 0 ? fired[0].Body : "(none)", "fires at once below 10 GB");
            TestMain.Equal(0, Run(e, st, disk(T0.AddSeconds(1), 11)).Count, "11 GB is not above 12: same episode");
            TestMain.Equal(0, Run(e, st, disk(T0.AddSeconds(2), 9)).Count, "so dipping again does not repeat");
            Run(e, st, disk(T0.AddSeconds(3), 13));
            TestMain.Equal(1, Run(e, st, disk(T0.AddSeconds(4), 9)).Count, "after going above 12 GB it can fire again");
        }

        private static void BatteryLow_OnlyOnBattery_ReArmsWhenPluggedIn()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Func<DateTime, bool, double, Snapshot> bat = (t, ac, pct) => { Snapshot s = Calm(t); s.OnAc = ac; s.BatteryPercent = pct; return s; };
            TestMain.Equal(0, Run(e, st, bat(T0, true, 15)).Count, "15 % while plugged in is fine");
            List<Alert> fired = Run(e, st, bat(T0.AddSeconds(1), false, 15));
            TestMain.Equal("15% left, on battery", fired.Count > 0 ? fired[0].Body : "(none)", "fires on battery below 20 %");
            TestMain.Equal(0, Run(e, st, bat(T0.AddSeconds(2), false, 12)).Count, "no repeat while still on battery");
            Run(e, st, bat(T0.AddSeconds(3), true, 12));
            TestMain.Equal(1, Run(e, st, bat(T0.AddSeconds(4), false, 12)).Count, "unplugging again after charging fires again");
        }

        private static void BatteryHot_FiresAfterTheHoldTime()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Func<DateTime, Snapshot> hot = t => { Snapshot s = Calm(t); s.BatteryTempC = 46; return s; };
            TestMain.Equal(0, Feed(e, st, T0, 60, hot, "battery_hot"), "not within 59 s");
            List<Alert> fired = Run(e, st, hot(T0.AddSeconds(60)));
            TestMain.Equal("Battery at 46\u00B0C for 1 min", fired.Count > 0 ? fired[0].Body : "(none)", "fires at 60 s");
        }

        private static ComPortInfo Port(string port, string name, DateTime firstSeen, bool live)
        {
            return new ComPortInfo { Port = port, Name = name, FirstSeen = firstSeen, AppearedLive = live };
        }

        private static Snapshot WithPorts(DateTime t, params ComPortInfo[] ports)
        {
            Snapshot s = Calm(t);
            s.ComPorts = new List<ComPortInfo>(ports);
            return s;
        }

        private static void ComPort_PresentAtStartNeverAlerts()
        {
            var e = new AlertEngine();
            TestMain.Equal(0, Run(e, new AppSettings(), WithPorts(T0.AddSeconds(10), Port("COM3", "USB Serial", T0, false))).Count, "port there at start-up");
        }

        private static void ComPort_WithName_AlertsOnce_AndAgainAfterReplug()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            List<Alert> fired = Run(e, st, WithPorts(T0, Port("COM5", "Silicon Labs CP210x", T0, true)));
            TestMain.Equal("COM5 connected", fired.Count > 0 ? fired[0].Title : "(none)", "title names the port");
            TestMain.Equal("Silicon Labs CP210x", fired.Count > 0 ? fired[0].Body : "(none)", "body names the device");
            TestMain.Equal(Level.Ok, fired.Count > 0 ? fired[0].Severity : Level.Unknown, "teal");
            TestMain.Equal(0, Run(e, st, WithPorts(T0.AddSeconds(2), Port("COM5", "Silicon Labs CP210x", T0, true))).Count, "only once while plugged in");
            Run(e, st, WithPorts(T0.AddSeconds(4)));
            TestMain.Equal(1, Run(e, st, WithPorts(T0.AddSeconds(6), Port("COM5", "Silicon Labs CP210x", T0.AddSeconds(6), true))).Count, "again after unplug and replug");
        }

        private static void ComPort_WithoutName_WaitsThreeSeconds()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            TestMain.Equal(0, Run(e, st, WithPorts(T0.AddSeconds(2), Port("COM7", null, T0, true))).Count, "waits for the name for up to 3 s");
            List<Alert> fired = Run(e, st, WithPorts(T0.AddSeconds(3), Port("COM7", null, T0, true)));
            TestMain.Equal("New serial device", fired.Count > 0 ? fired[0].Body : "(none)", "then alerts without a name");
        }

        // Final review M4: once the lookup has finished without a name (""), there is nothing to wait for.
        private static void ComPort_LookupFinishedWithoutName_AlertsAtOnce()
        {
            var e = new AlertEngine();
            List<Alert> fired = Run(e, new AppSettings(), WithPorts(T0, Port("COM9", "", T0, true)));
            TestMain.Equal("New serial device", fired.Count > 0 ? fired[0].Body : "(none)", "alerts straight away, without a name");
        }

        private static void Switches_TurnAlertsOff()
        {
            var off = new AppSettings { AlertsEnabled = false };
            var e = new AlertEngine();
            Snapshot s = Calm(T0);
            s.DiskFreeBytes = 1 * Gb;
            TestMain.Equal(0, Run(e, off, s).Count, "master switch off: nothing");
            var diskOff = new AppSettings { AlertDiskLowOn = false };
            var e2 = new AlertEngine();
            Snapshot s2 = WithPorts(T0, Port("COM5", "X", T0, true));
            s2.DiskFreeBytes = 1 * Gb;
            List<Alert> fired = Run(e2, diskOff, s2);
            TestMain.Equal(1, fired.Count, "disk alert off, COM alert still on");
            TestMain.Equal("com_port", fired.Count > 0 ? fired[0].Kind : "(none)", "the one that fired is the COM alert");
        }

        private static void Recent_IsTodayNewestFirstAtMostTen()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            for (int i = 0; i < 12; i++)
            {
                Run(e, st, WithPorts(T0.AddSeconds(2 * i), Port("COM" + (i + 1), "X", T0.AddSeconds(2 * i), true)));
                Run(e, st, WithPorts(T0.AddSeconds(2 * i + 1))); // unplug so the next one is fresh
            }
            List<Alert> recent = e.Recent(T0.AddHours(1));
            TestMain.Equal(10, recent.Count, "at most 10");
            TestMain.Equal("COM12 connected", recent[0].Title, "newest first");
            TestMain.Equal(0, e.Recent(T0.AddDays(1)).Count, "yesterday's alerts are not today's");
        }
    }
}
