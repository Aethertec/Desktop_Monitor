using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopMonitor
{
    // CSV formatting and retention rules (spec section 8). Pure; tested in CsvLogTests.
    public static class CsvLog
    {
        public const string Header = "time,cpu_pct,cpu_limit_pct,ram_pct,gpu_pct,disk_free_gb,down_kbps,up_kbps,battery_pct,on_ac,cpu_temp_c,skin_temp_c,battery_temp_c,top_app,top_app_cpu_pct,power_mode";
        public const string AlertHeader = "time,alert,title,detail";
        private static readonly Regex DailyFile = new Regex(@"^(\d{4}-\d{2}-\d{2})\.csv$", RegexOptions.IgnoreCase);

        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopMonitor", "logs"); }
        }

        public static string FileNameFor(DateTime day)
        {
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";
        }

        public static string Time(DateTime t)
        {
            return t.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        // One decimal, '.' as the decimal point, empty for a missing reading.
        public static string Num(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
        }

        public static string Quote(string s)
        {
            if (s == null) return "";
            return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static string AlertRow(Alert a)
        {
            return Time(a.Time) + "," + a.Kind + "," + Quote(a.Title) + "," + Quote(a.Body);
        }

        // Daily files (exactly YYYY-MM-DD.csv) whose date is more than `retentionDays` before `today`. Nothing else is ever listed.
        public static List<string> Expired(IEnumerable<string> fileNames, DateTime today, int retentionDays)
        {
            var expired = new List<string>();
            DateTime oldestKept = today.Date.AddDays(-retentionDays);
            foreach (string name in fileNames)
            {
                Match m = DailyFile.Match(name);
                DateTime day;
                if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) && day < oldestKept)
                    expired.Add(name);
            }
            return expired;
        }
    }

    // Collects the 1 s snapshots of one log row: averages for CPU, GPU and network, the CPU temperature peak,
    // and the latest value of everything else.
    public sealed class LogWindow
    {
        private DateTime _start = DateTime.MinValue;
        private int _count, _cpuN, _gpuN, _downN, _upN;
        private double _cpu, _gpu, _down, _up;
        private double? _peak;
        private Snapshot _last;

        public void Add(Snapshot s)
        {
            if (_start == DateTime.MinValue) _start = s.Time;
            _count++;
            if (s.CpuPercent.HasValue) { _cpu += s.CpuPercent.Value; _cpuN++; }
            if (s.GpuPercent.HasValue) { _gpu += s.GpuPercent.Value; _gpuN++; }
            if (s.DownBytesPerSec.HasValue) { _down += s.DownBytesPerSec.Value; _downN++; }
            if (s.UpBytesPerSec.HasValue) { _up += s.UpBytesPerSec.Value; _upN++; }
            if (s.CpuTempC.HasValue && (!_peak.HasValue || s.CpuTempC.Value > _peak.Value)) _peak = s.CpuTempC;
            _last = s;
        }

        public bool Due(DateTime now, TimeSpan interval)
        {
            return _count > 0 && now - _start >= interval;
        }

        // Starts the next window where this one ended, so rows stay exactly `interval` apart; after a long gap
        // (sleep) it restarts from the latest sample instead of writing a burst of catch-up rows.
        public void Next(TimeSpan interval)
        {
            DateTime end = _start + interval;
            DateTime lastTime = _last != null ? _last.Time : end;
            Clear();
            _start = lastTime - end >= interval ? lastTime : end;
        }

        public string ToRow(string powerMode)
        {
            Snapshot s = _last;
            AppUsage top = s.TopApps.Count > 0 ? s.TopApps[0] : null;
            var row = new StringBuilder();
            row.Append(CsvLog.Time(s.Time)).Append(',');
            row.Append(CsvLog.Num(Avg(_cpu, _cpuN))).Append(',');
            row.Append(CsvLog.Num(s.CpuLimitPercent)).Append(',');
            row.Append(CsvLog.Num(s.RamPercent)).Append(',');
            row.Append(CsvLog.Num(Avg(_gpu, _gpuN))).Append(',');
            row.Append(CsvLog.Num(s.DiskFreeBytes.HasValue ? s.DiskFreeBytes.Value / (1024.0 * 1024 * 1024) : (double?)null)).Append(',');
            row.Append(CsvLog.Num(Kb(Avg(_down, _downN)))).Append(',');
            row.Append(CsvLog.Num(Kb(Avg(_up, _upN)))).Append(',');
            row.Append(CsvLog.Num(s.BatteryPercent)).Append(',');
            row.Append(s.OnAc ? "1" : "0").Append(',');
            row.Append(CsvLog.Num(_peak)).Append(',');
            row.Append(CsvLog.Num(s.SkinTempC)).Append(',');
            row.Append(CsvLog.Num(s.BatteryTempC)).Append(',');
            row.Append(top == null ? "" : CsvLog.Quote(top.Name)).Append(',');
            row.Append(top == null ? "" : CsvLog.Num(top.CpuPercent)).Append(',');
            row.Append(CsvLog.Quote(powerMode));
            return row.ToString();
        }

        private void Clear()
        {
            _count = _cpuN = _gpuN = _downN = _upN = 0;
            _cpu = _gpu = _down = _up = 0;
            _peak = null;
            _last = null;
        }

        private static double? Avg(double sum, int n)
        {
            return n > 0 ? sum / n : (double?)null;
        }

        private static double? Kb(double? bytesPerSecond)
        {
            return bytesPerSecond.HasValue ? bytesPerSecond.Value / 1024 : (double?)null;
        }
    }

    // File side of the log: one file per day plus alerts.csv. Rows that cannot be written (for example while the
    // file is open in Excel) are kept in memory, up to a day's worth, and written with the next successful row.
    public sealed class CsvLogWriter
    {
        private const int MaxPending = 8640;
        private readonly string _dir;
        private readonly Dictionary<string, List<string>> _pending = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private string _failingPath;
        private DateTime _prunedFor = DateTime.MinValue;

        public CsvLogWriter(string dir)
        {
            _dir = dir;
        }

        public string Dir { get { return _dir; } }

        public void Append(DateTime time, string row, int retentionDays)
        {
            Write(Path.Combine(_dir, CsvLog.FileNameFor(time)), CsvLog.Header, row);
            if (_prunedFor != time.Date)
            {
                _prunedFor = time.Date;
                Prune(time, retentionDays);
            }
        }

        public void AppendAlert(Alert a)
        {
            Write(Path.Combine(_dir, "alerts.csv"), CsvLog.AlertHeader, CsvLog.AlertRow(a));
        }

        public void Prune(DateTime today, int retentionDays)
        {
            try
            {
                if (!Directory.Exists(_dir)) return;
                var names = new List<string>();
                foreach (string path in Directory.GetFiles(_dir, "*.csv")) names.Add(Path.GetFileName(path));
                foreach (string name in CsvLog.Expired(names, today, retentionDays)) File.Delete(Path.Combine(_dir, name));
            }
            catch (Exception ex)
            {
                Log.Write("CSV log: could not prune old files, " + ex.Message);
            }
        }

        private void Write(string path, string header, string row)
        {
            List<string> queue;
            if (!_pending.TryGetValue(path, out queue))
            {
                queue = new List<string>();
                _pending[path] = queue;
            }
            queue.Add(row);
            if (queue.Count > MaxPending) queue.RemoveRange(0, queue.Count - MaxPending);
            try
            {
                Directory.CreateDirectory(_dir);
                var text = new StringBuilder();
                if (!File.Exists(path)) text.Append('﻿').Append(header).Append("\r\n"); // BOM: Excel then reads it as UTF-8
                foreach (string r in queue) text.Append(r).Append("\r\n");
                File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
                queue.Clear();
                if (_failingPath == path) Log.Write("CSV log: writing " + Path.GetFileName(path) + " again");
                _failingPath = null;
            }
            catch (Exception ex)
            {
                if (_failingPath != path) Log.Write("CSV log: cannot write " + Path.GetFileName(path) + " (" + ex.Message + "), keeping rows until it can");
                _failingPath = path;
            }
        }
    }
}
