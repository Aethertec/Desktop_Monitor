using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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

            var map = new Dictionary<string, object>(raw, StringComparer.OrdinalIgnoreCase);
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
            sb.Append("  \"zoneBattery\": ").Append(js.Serialize(s.ZoneBattery)).Append("\r\n");
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
                return Parse(File.ReadAllText(path));
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
            }
        }

        public void Dispose()
        {
            _fsw.Dispose();
            _debounce.Dispose();
        }
    }
}
