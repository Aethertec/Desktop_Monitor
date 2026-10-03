using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace DesktopMonitor
{
    // User-editable settings (spec section 7). Defaults live in the field initialisers.
    public sealed class AppSettings
    {
        public double? X;
        public double? Y;
        public double Opacity = 0.93;
        public double UsageAmber = 70, UsageRed = 85;
        public double TempAmber = 80, TempRed = 85;
        public double DiskAmber = 90, DiskRed = 95;
        public double TempRingMin = 30, TempRingMax = 100;
        public string ZoneCpu = "CPUZ", ZoneSkin = "SK1Z", ZoneBattery = "BATZ";

        // v2: CSV log (spec section 8) and alerts (spec section 7).
        public bool LogEnabled = true;
        public double LogIntervalSeconds = 10, LogRetentionDays = 30;
        public bool AlertsEnabled = true;
        public bool AlertCpuHotOn = true, AlertThrottledOn = true, AlertDiskLowOn = true, AlertBatteryLowOn = true, AlertBatteryHotOn = true, AlertComPortsOn = true;
        public double AlertCpuTemp = 90, AlertCpuClear = 85, AlertCpuSeconds = 60;
        public double AlertThrottleSeconds = 120;
        public double AlertDiskFreeGb = 10, AlertDiskClearGb = 12;
        public double AlertBatteryPercent = 20;
        public double AlertBatteryTemp = 45, AlertBatteryTempClear = 42, AlertBatteryTempSeconds = 60;

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }

    public static class SettingsStore
    {
        public static string DefaultPath
        {
            get { return Path.Combine(Log.DataDir, "settings.json"); }
        }

        // Throws FormatException when the text is not a JSON object.
        // Missing or mistyped keys fall back to defaults; out-of-range numbers are clamped.
        public static AppSettings Parse(string json)
        {
            object root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(json ?? "");
            }
            catch (Exception ex)
            {
                throw new FormatException("settings.json is not valid JSON: " + ex.Message, ex);
            }
            var raw = root as Dictionary<string, object>;
            if (raw == null) throw new FormatException("settings.json must contain a JSON object");

            // Copied one by one: the dictionary constructor throws when two keys differ only in case; here the last one wins.
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kv in raw) map[kv.Key] = kv.Value;
            var d = new AppSettings();
            var s = new AppSettings();
            s.X = NullableNumber(map, "x");
            s.Y = NullableNumber(map, "y");
            s.Opacity = Number(map, "opacity", d.Opacity, 0.2, 1.0);
            s.UsageAmber = Number(map, "usageAmber", d.UsageAmber, 0, 100);
            s.UsageRed = Number(map, "usageRed", d.UsageRed, 0, 100);
            s.TempAmber = Number(map, "tempAmber", d.TempAmber, 0, 150);
            s.TempRed = Number(map, "tempRed", d.TempRed, 0, 150);
            s.DiskAmber = Number(map, "diskAmber", d.DiskAmber, 0, 100);
            s.DiskRed = Number(map, "diskRed", d.DiskRed, 0, 100);
            s.TempRingMin = Number(map, "tempRingMin", d.TempRingMin, 0, 150);
            s.TempRingMax = Number(map, "tempRingMax", d.TempRingMax, 0, 150);
            if (s.TempRingMax <= s.TempRingMin)
            {
                s.TempRingMin = d.TempRingMin;
                s.TempRingMax = d.TempRingMax;
            }
            s.ZoneCpu = Text(map, "zoneCpu", d.ZoneCpu);
            s.ZoneSkin = Text(map, "zoneSkin", d.ZoneSkin);
            s.ZoneBattery = Text(map, "zoneBattery", d.ZoneBattery);
            s.LogEnabled = Flag(map, "logEnabled", d.LogEnabled);
            s.LogIntervalSeconds = Number(map, "logIntervalSeconds", d.LogIntervalSeconds, 5, 300);
            s.LogRetentionDays = Number(map, "logRetentionDays", d.LogRetentionDays, 1, 365);
            s.AlertsEnabled = Flag(map, "alertsEnabled", d.AlertsEnabled);
            s.AlertCpuHotOn = Flag(map, "alertCpuHotOn", d.AlertCpuHotOn);
            s.AlertThrottledOn = Flag(map, "alertThrottledOn", d.AlertThrottledOn);
            s.AlertDiskLowOn = Flag(map, "alertDiskLowOn", d.AlertDiskLowOn);
            s.AlertBatteryLowOn = Flag(map, "alertBatteryLowOn", d.AlertBatteryLowOn);
            s.AlertBatteryHotOn = Flag(map, "alertBatteryHotOn", d.AlertBatteryHotOn);
            s.AlertComPortsOn = Flag(map, "alertComPortsOn", d.AlertComPortsOn);
            s.AlertCpuTemp = Number(map, "alertCpuTemp", d.AlertCpuTemp, 0, 150);
            s.AlertCpuClear = Number(map, "alertCpuClear", d.AlertCpuClear, 0, 150);
            s.AlertCpuSeconds = Number(map, "alertCpuSeconds", d.AlertCpuSeconds, 0, 3600);
            s.AlertThrottleSeconds = Number(map, "alertThrottleSeconds", d.AlertThrottleSeconds, 0, 3600);
            s.AlertDiskFreeGb = Number(map, "alertDiskFreeGb", d.AlertDiskFreeGb, 0, 100000);
            s.AlertDiskClearGb = Number(map, "alertDiskClearGb", d.AlertDiskClearGb, 0, 100000);
            s.AlertBatteryPercent = Number(map, "alertBatteryPercent", d.AlertBatteryPercent, 0, 100);
            s.AlertBatteryTemp = Number(map, "alertBatteryTemp", d.AlertBatteryTemp, 0, 150);
            s.AlertBatteryTempClear = Number(map, "alertBatteryTempClear", d.AlertBatteryTempClear, 0, 150);
            s.AlertBatteryTempSeconds = Number(map, "alertBatteryTempSeconds", d.AlertBatteryTempSeconds, 0, 3600);
            return s;
        }

        public static string ToJson(AppSettings s)
        {
            var js = new JavaScriptSerializer();
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"x\": ").Append(s.X.HasValue ? Num(s.X.Value) : "null").Append(",\r\n");
            sb.Append("  \"y\": ").Append(s.Y.HasValue ? Num(s.Y.Value) : "null").Append(",\r\n");
            sb.Append("  \"opacity\": ").Append(Num(s.Opacity)).Append(",\r\n");
            sb.Append("  \"usageAmber\": ").Append(Num(s.UsageAmber)).Append(",\r\n");
            sb.Append("  \"usageRed\": ").Append(Num(s.UsageRed)).Append(",\r\n");
            sb.Append("  \"tempAmber\": ").Append(Num(s.TempAmber)).Append(",\r\n");
            sb.Append("  \"tempRed\": ").Append(Num(s.TempRed)).Append(",\r\n");
            sb.Append("  \"diskAmber\": ").Append(Num(s.DiskAmber)).Append(",\r\n");
            sb.Append("  \"diskRed\": ").Append(Num(s.DiskRed)).Append(",\r\n");
            sb.Append("  \"tempRingMin\": ").Append(Num(s.TempRingMin)).Append(",\r\n");
            sb.Append("  \"tempRingMax\": ").Append(Num(s.TempRingMax)).Append(",\r\n");
            sb.Append("  \"zoneCpu\": ").Append(js.Serialize(s.ZoneCpu)).Append(",\r\n");
            sb.Append("  \"zoneSkin\": ").Append(js.Serialize(s.ZoneSkin)).Append(",\r\n");
            sb.Append("  \"zoneBattery\": ").Append(js.Serialize(s.ZoneBattery)).Append(",\r\n");
            sb.Append("  \"logEnabled\": ").Append(Bool(s.LogEnabled)).Append(",\r\n");
            sb.Append("  \"logIntervalSeconds\": ").Append(Num(s.LogIntervalSeconds)).Append(",\r\n");
            sb.Append("  \"logRetentionDays\": ").Append(Num(s.LogRetentionDays)).Append(",\r\n");
            sb.Append("  \"alertsEnabled\": ").Append(Bool(s.AlertsEnabled)).Append(",\r\n");
            sb.Append("  \"alertCpuHotOn\": ").Append(Bool(s.AlertCpuHotOn)).Append(",\r\n");
            sb.Append("  \"alertThrottledOn\": ").Append(Bool(s.AlertThrottledOn)).Append(",\r\n");
            sb.Append("  \"alertDiskLowOn\": ").Append(Bool(s.AlertDiskLowOn)).Append(",\r\n");
            sb.Append("  \"alertBatteryLowOn\": ").Append(Bool(s.AlertBatteryLowOn)).Append(",\r\n");
            sb.Append("  \"alertBatteryHotOn\": ").Append(Bool(s.AlertBatteryHotOn)).Append(",\r\n");
            sb.Append("  \"alertComPortsOn\": ").Append(Bool(s.AlertComPortsOn)).Append(",\r\n");
            sb.Append("  \"alertCpuTemp\": ").Append(Num(s.AlertCpuTemp)).Append(",\r\n");
            sb.Append("  \"alertCpuClear\": ").Append(Num(s.AlertCpuClear)).Append(",\r\n");
            sb.Append("  \"alertCpuSeconds\": ").Append(Num(s.AlertCpuSeconds)).Append(",\r\n");
            sb.Append("  \"alertThrottleSeconds\": ").Append(Num(s.AlertThrottleSeconds)).Append(",\r\n");
            sb.Append("  \"alertDiskFreeGb\": ").Append(Num(s.AlertDiskFreeGb)).Append(",\r\n");
            sb.Append("  \"alertDiskClearGb\": ").Append(Num(s.AlertDiskClearGb)).Append(",\r\n");
            sb.Append("  \"alertBatteryPercent\": ").Append(Num(s.AlertBatteryPercent)).Append(",\r\n");
            sb.Append("  \"alertBatteryTemp\": ").Append(Num(s.AlertBatteryTemp)).Append(",\r\n");
            sb.Append("  \"alertBatteryTempClear\": ").Append(Num(s.AlertBatteryTempClear)).Append(",\r\n");
            sb.Append("  \"alertBatteryTempSeconds\": ").Append(Num(s.AlertBatteryTempSeconds)).Append("\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        // First run writes the defaults. A malformed file is left untouched for the user to fix; defaults are used meanwhile.
        public static AppSettings LoadOrCreate(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var defaults = new AppSettings();
                    Save(path, defaults);
                    return defaults;
                }
                string json = File.ReadAllText(path);
                AppSettings s = Parse(json);
                if (MissingAnyKey(json)) Save(path, s); // a file from an older version gains the new keys, values kept
                return s;
            }
            catch (Exception ex)
            {
                Log.Write("Settings: using defaults, " + ex.Message);
                return new AppSettings();
            }
        }

        public static void Save(string path, AppSettings s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ToJson(s), new UTF8Encoding(false));
        }

        private static double Number(Dictionary<string, object> map, string key, double fallback, double min, double max)
        {
            object v;
            if (!map.TryGetValue(key, out v) || !IsNumber(v)) return fallback;
            double x = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            return x < min ? min : (x > max ? max : x);
        }

        private static double? NullableNumber(Dictionary<string, object> map, string key)
        {
            object v;
            if (!map.TryGetValue(key, out v) || !IsNumber(v)) return null;
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        private static string Text(Dictionary<string, object> map, string key, string fallback)
        {
            object v;
            if (!map.TryGetValue(key, out v)) return fallback;
            var s = v as string;
            return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
        }

        // True when the (already valid) JSON lacks any key that ToJson writes, e.g. a v1 file without the alert and log keys.
        private static bool MissingAnyKey(string json)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var raw = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (raw != null) foreach (string key in raw.Keys) present.Add(key);
            foreach (Match m in KeyName.Matches(ToJson(new AppSettings())))
                if (!present.Contains(m.Groups[1].Value)) return true;
            return false;
        }

        private static readonly Regex KeyName = new Regex(@"""(\w+)"":");

        // JSON true/false only; anything else (a string "yes", a number) keeps the default.
        private static bool Flag(Dictionary<string, object> map, string key, bool fallback)
        {
            object v;
            return map.TryGetValue(key, out v) && v is bool ? (bool)v : fallback;
        }

        private static string Bool(bool v)
        {
            return v ? "true" : "false";
        }

        private static bool IsNumber(object v)
        {
            return v is int || v is long || v is decimal || v is double;
        }

        private static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    // Raises Changed (on a thread-pool thread) 300 ms after settings.json stops changing. Invalid edits are logged and ignored.
    public sealed class SettingsWatcher : IDisposable
    {
        private readonly string _path;
        private readonly FileSystemWatcher _fsw;
        private readonly Timer _debounce;

        public event Action<AppSettings> Changed;

        public SettingsWatcher(string path)
        {
            _path = path;
            _debounce = new Timer(delegate { Reload(); }, null, Timeout.Infinite, Timeout.Infinite);
            _fsw = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path));
            _fsw.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            _fsw.Changed += delegate { Kick(); };
            _fsw.Created += delegate { Kick(); };
            _fsw.Renamed += delegate { Kick(); };
            _fsw.EnableRaisingEvents = true;
        }

        private void Kick()
        {
            _debounce.Change(300, Timeout.Infinite);
        }

        private void Reload()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    AppSettings s = SettingsStore.Parse(File.ReadAllText(_path));
                    Action<AppSettings> handler = Changed;
                    if (handler != null) handler(s);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(200); // the editor may still be writing the file
                }
                catch (FormatException ex)
                {
                    Log.Write("Settings: ignored invalid edit, " + ex.Message);
                    return;
                }
                catch (Exception ex) // runs on a timer thread, where anything unhandled would end the process
                {
                    Log.Write("Settings: reload failed, " + ex.GetType().Name + ": " + ex.Message);
                    return;
                }
            }
            Log.Write("Settings: reload skipped, settings.json stayed locked");
        }

        public void Dispose()
        {
            _fsw.Dispose();
            _debounce.Dispose();
        }
    }
}
