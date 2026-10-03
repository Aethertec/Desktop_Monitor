using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopMonitor.Tests
{
    internal static class CsvLogTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 3, 14, 0, 0);

        public static void Run()
        {
            Row_HasOneFieldPerHeaderColumn();
            Row_AveragesPeaksAndLatest();
            Row_MissingReadingsAreEmptyFields();
            Quote_OnlyWhenNeeded();
            Window_KeepsItsCadence_AndSkipsAfterAGap();
            Expired_OnlyDailyFilesOlderThanRetention();
            Writer_WritesTheHeaderOnce();
            Writer_KeepsRowsWhileTheFileIsLocked();
            Writer_MarksNewFilesAsUtf8ForExcel();
        }

        private static Snapshot S(DateTime t, double cpu, double temp)
        {
            var s = new Snapshot
            {
                Time = t, CpuPercent = cpu, GpuPercent = 5, CpuTempC = temp, CpuLimitPercent = 100, RamPercent = 62,
                DownBytesPerSec = 1024, UpBytesPerSec = 2048, DiskFreeBytes = 36.0 * 1024 * 1024 * 1024,
                BatteryPercent = 99, OnAc = true, SkinTempC = 41, BatteryTempC = 33
            };
            s.TopApps.Add(new AppUsage { Name = "brave", CpuPercent = 12.34, PrivateBytes = 1 });
            return s;
        }

        private static void Row_HasOneFieldPerHeaderColumn()
        {
            var w = new LogWindow();
            w.Add(S(T0, 10, 70));
            TestMain.Equal(CsvLog.Header.Split(',').Length, w.ToRow("Balanced").Split(',').Length, "column count matches the header");
        }

        private static void Row_AveragesPeaksAndLatest()
        {
            var w = new LogWindow();
            w.Add(S(T0, 10, 70));
            w.Add(S(T0.AddSeconds(1), 20, 88));
            Snapshot last = S(T0.AddSeconds(2), 30, 75);
            last.GpuPercent = null; // ignored in the GPU average
            last.OnAc = false;
            w.Add(last);
            string[] f = w.ToRow("Efficiency").Split(',');
            TestMain.Equal("2026-10-03T14:00:02", f[0], "time of the latest sample");
            TestMain.Equal("20.0", f[1], "CPU is the average");
            TestMain.Equal("5.0", f[4], "GPU average skips the missing reading");
            TestMain.Equal("36.0", f[5], "disk free in GB");
            TestMain.Equal("1.0", f[6], "download in KB/s");
            TestMain.Equal("2.0", f[7], "upload in KB/s");
            TestMain.Equal("0", f[9], "on_ac is the latest value");
            TestMain.Equal("88.0", f[10], "CPU temp is the peak");
            TestMain.Equal("brave", f[13], "top app");
            TestMain.Equal("12.3", f[14], "top app CPU, one decimal");
            TestMain.Equal("Efficiency", f[15], "power mode");
        }

        private static void Row_MissingReadingsAreEmptyFields()
        {
            var w = new LogWindow();
            w.Add(new Snapshot { Time = T0 });
            string[] f = w.ToRow("--").Split(',');
            TestMain.Equal("", f[1], "no CPU reading: empty");
            TestMain.Equal("", f[10], "no temperature: empty");
            TestMain.Equal("", f[13], "no top app: empty");
        }

        private static void Quote_OnlyWhenNeeded()
        {
            TestMain.Equal("brave", CsvLog.Quote("brave"), "plain text unchanged");
            TestMain.Equal("\"a,b\"", CsvLog.Quote("a,b"), "comma quoted");
            TestMain.Equal("\"say \"\"hi\"\"\"", CsvLog.Quote("say \"hi\""), "quotes doubled");
            TestMain.Equal("", CsvLog.Quote(null), "null is empty");
        }

        private static void Window_KeepsItsCadence_AndSkipsAfterAGap()
        {
            var w = new LogWindow();
            var every = TimeSpan.FromSeconds(10);
            for (int i = 0; i < 10; i++) w.Add(S(T0.AddSeconds(i), 10, 70));
            TestMain.True(!w.Due(T0.AddSeconds(9), every), "not due after 9 s");
            w.Add(S(T0.AddSeconds(10), 10, 70));
            TestMain.True(w.Due(T0.AddSeconds(10), every), "due after 10 s");
            w.Next(every);
            w.Add(S(T0.AddSeconds(11), 10, 70));
            TestMain.True(!w.Due(T0.AddSeconds(19), every), "next window started at 10 s, so not due at 19 s");
            TestMain.True(w.Due(T0.AddSeconds(20), every), "due at 20 s");
            w.Add(S(T0.AddSeconds(3600), 10, 70)); // laptop slept for an hour
            w.Next(every);
            w.Add(S(T0.AddSeconds(3601), 10, 70));
            TestMain.True(!w.Due(T0.AddSeconds(3601), every), "after a long gap it restarts instead of writing catch-up rows");
        }

        private static void Expired_OnlyDailyFilesOlderThanRetention()
        {
            var names = new List<string> { "2026-09-02.csv", "2026-09-03.csv", "2026-10-03.csv", "alerts.csv", "notes.txt", "2026-13-40.csv", "2026-09-01.csv.bak" };
            List<string> expired = CsvLog.Expired(names, T0, 30);
            TestMain.Equal(1, expired.Count, "only one file is old enough");
            TestMain.Equal("2026-09-02.csv", expired.Count > 0 ? expired[0] : "(none)", "31 days old goes; 30 days old stays");
        }

        private static string TempDir()
        {
            return Path.Combine(Path.GetTempPath(), "DesktopMonitorTests-" + Guid.NewGuid().ToString("N"));
        }

        private static void Writer_WritesTheHeaderOnce()
        {
            string dir = TempDir();
            try
            {
                var writer = new CsvLogWriter(dir);
                writer.Append(T0, "row1", 30);
                writer.Append(T0.AddSeconds(10), "row2", 30);
                string[] lines = File.ReadAllLines(Path.Combine(dir, "2026-10-03.csv"));
                TestMain.Equal(3, lines.Length, "header plus two rows");
                TestMain.Equal(CsvLog.Header, lines[0], "header first");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // Found in Task 11: without a byte-order mark, Excel and PowerShell on a code-page-932 Windows read "95°C" in
        // alerts.csv as "95ﾂｰC". New files must start with the UTF-8 BOM, written once.
        private static void Writer_MarksNewFilesAsUtf8ForExcel()
        {
            string dir = TempDir();
            try
            {
                var writer = new CsvLogWriter(dir);
                writer.AppendAlert(new Alert { Time = T0, Kind = "cpu_hot", Title = "CPU hot", Body = "95°C for 1 min" });
                writer.AppendAlert(new Alert { Time = T0.AddMinutes(5), Kind = "cpu_hot", Title = "CPU hot", Body = "96°C for 1 min" });
                byte[] bytes = File.ReadAllBytes(Path.Combine(dir, "alerts.csv"));
                TestMain.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "alerts.csv starts with the UTF-8 BOM");
                string text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                TestMain.True(text.IndexOf('﻿') < 0, "the BOM is written once, not before every row");
                TestMain.True(text.Contains("95°C") && text.Contains("96°C"), "the degree sign survives");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // Review focus 3: opening today's CSV in Excel locks it; rows written meanwhile must not be lost.
        private static void Writer_KeepsRowsWhileTheFileIsLocked()
        {
            string dir = TempDir();
            string path = Path.Combine(dir, "2026-10-03.csv");
            try
            {
                var writer = new CsvLogWriter(dir);
                writer.Append(T0, "row1", 30);
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    writer.Append(T0.AddSeconds(10), "row2", 30); // fails quietly, row kept
                writer.Append(T0.AddSeconds(20), "row3", 30);
                string[] lines = File.ReadAllLines(path);
                TestMain.Equal("row1|row2|row3", string.Join("|", lines, 1, lines.Length - 1), "the row written while locked arrives, in order");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
