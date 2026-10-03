using System;
using System.Collections.Generic;

namespace DesktopMonitor
{
    public sealed class Alert
    {
        public DateTime Time;
        public string Kind;
        public string Title;
        public string Body;
        public Level Severity;
    }

    // One alert's life cycle: fires once after its condition has held for `holdFor`, then re-arms only after the clear
    // condition has held for `clearFor`. A missing reading (null) restarts the timing in progress and never fires or clears.
    public sealed class Episode
    {
        private DateTime? _since, _clearSince;
        private bool _fired;

        public bool Fired { get { return _fired; } }

        public bool Step(DateTime now, bool? trigger, bool? clear, TimeSpan holdFor, TimeSpan clearFor)
        {
            if (!trigger.HasValue || !clear.HasValue)
            {
                _since = null;
                _clearSince = null;
                return false;
            }
            if (!_fired)
            {
                if (!trigger.Value) { _since = null; return false; }
                if (!_since.HasValue) _since = now;
                if (now - _since.Value < holdFor) return false;
                _fired = true;
                _clearSince = null;
                return true;
            }
            if (!clear.Value) { _clearSince = null; return false; }
            if (!_clearSince.HasValue) _clearSince = now;
            if (now - _clearSince.Value >= clearFor)
            {
                _fired = false;
                _since = null;
            }
            return false;
        }
    }

    // The six alerts of spec section 7. Pure: feed it each Snapshot; it returns the alerts that fire now.
    public sealed class AlertEngine
    {
        private static readonly TimeSpan ComNameWait = TimeSpan.FromSeconds(3);
        private readonly Episode _cpuHot = new Episode();
        private readonly Episode _throttled = new Episode();
        private readonly Episode _diskLow = new Episode();
        private readonly Episode _batteryLow = new Episode();
        private readonly Episode _batteryHot = new Episode();
        private readonly HashSet<string> _comAlerted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Alert> _recent = new List<Alert>();

        public List<Alert> Evaluate(Snapshot s, AppSettings st)
        {
            var fired = new List<Alert>();
            DateTime now = s.Time;

            TimeSpan cpuHold = Seconds(st.AlertCpuSeconds);
            if (_cpuHot.Step(now, AtLeast(s.CpuTempC, st.AlertCpuTemp), Below(s.CpuTempC, st.AlertCpuClear), cpuHold, cpuHold) && st.AlertCpuHotOn)
                Add(fired, now, "cpu_hot", "CPU hot", Rules.FormatTemp(s.CpuTempC, true) + " for " + Rules.FormatDuration(st.AlertCpuSeconds), Level.Red);

            bool? limited = s.CpuLimitPercent.HasValue ? Rules.IsBelowFullSpeed(s.CpuLimitPercent.Value) : (bool?)null;
            bool? fullSpeed = limited.HasValue ? !limited.Value : (bool?)null;
            if (_throttled.Step(now, limited, fullSpeed, Seconds(st.AlertThrottleSeconds), TimeSpan.Zero) && st.AlertThrottledOn)
                Add(fired, now, "throttled", "Throttled", "CPU held at " + Rules.FormatPercent(s.CpuLimitPercent) + " of full speed for " + Rules.FormatDuration(st.AlertThrottleSeconds), Level.Amber);

            double? freeGb = s.DiskFreeBytes.HasValue ? s.DiskFreeBytes.Value / (1024.0 * 1024 * 1024) : (double?)null;
            bool? diskLow = freeGb.HasValue ? freeGb.Value < st.AlertDiskFreeGb : (bool?)null;
            bool? diskOk = freeGb.HasValue ? freeGb.Value > st.AlertDiskClearGb : (bool?)null;
            if (_diskLow.Step(now, diskLow, diskOk, TimeSpan.Zero, TimeSpan.Zero) && st.AlertDiskLowOn)
                Add(fired, now, "disk_low", "Disk low", "C: has " + Rules.FormatDiskFree(s.DiskFreeBytes) + " free", Level.Amber);

            bool? batteryLow = s.BatteryPercent.HasValue ? !s.OnAc && s.BatteryPercent.Value < st.AlertBatteryPercent : (bool?)null;
            bool? pluggedIn = s.BatteryPercent.HasValue ? s.OnAc : (bool?)null;
            if (_batteryLow.Step(now, batteryLow, pluggedIn, TimeSpan.Zero, TimeSpan.Zero) && st.AlertBatteryLowOn)
                Add(fired, now, "battery_low", "Battery low", Rules.FormatPercent(s.BatteryPercent) + " left, on battery", Level.Amber);

            TimeSpan batteryHold = Seconds(st.AlertBatteryTempSeconds);
            if (_batteryHot.Step(now, AtLeast(s.BatteryTempC, st.AlertBatteryTemp), Below(s.BatteryTempC, st.AlertBatteryTempClear), batteryHold, batteryHold) && st.AlertBatteryHotOn)
                Add(fired, now, "battery_hot", "Battery hot", "Battery at " + Rules.FormatTemp(s.BatteryTempC, true) + " for " + Rules.FormatDuration(st.AlertBatteryTempSeconds), Level.Red);

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ComPortInfo port in s.ComPorts)
            {
                present.Add(port.Port);
                if (!port.AppearedLive || _comAlerted.Contains(port.Port)) continue;
                if (port.Name == null && now - port.FirstSeen < ComNameWait) continue; // give the name lookup a moment
                _comAlerted.Add(port.Port);
                if (st.AlertComPortsOn) Add(fired, now, "com_port", port.Port + " connected", port.Name ?? "New serial device", Level.Ok);
            }
            _comAlerted.RemoveWhere(p => !present.Contains(p));

            if (!st.AlertsEnabled) fired.Clear();
            foreach (Alert a in fired) _recent.Insert(0, a);
            if (_recent.Count > 10) _recent.RemoveRange(10, _recent.Count - 10);
            return fired;
        }

        // Today's alerts, newest first, at most 10.
        public List<Alert> Recent(DateTime now)
        {
            return _recent.FindAll(a => a.Time.Date == now.Date);
        }

        private static void Add(List<Alert> list, DateTime now, string kind, string title, string body, Level severity)
        {
            list.Add(new Alert { Time = now, Kind = kind, Title = title, Body = body, Severity = severity });
        }

        private static bool? AtLeast(double? value, double limit)
        {
            return value.HasValue ? Rules.Display(value.Value) >= limit : (bool?)null;
        }

        private static bool? Below(double? value, double limit)
        {
            return value.HasValue ? Rules.Display(value.Value) < limit : (bool?)null;
        }

        private static TimeSpan Seconds(double s)
        {
            return TimeSpan.FromSeconds(s);
        }
    }
}
