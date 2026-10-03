using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopMonitor
{
    public enum Level { Unknown, Ok, Amber, Red }

    public struct Box
    {
        public double X, Y, W, H;

        public Box(double x, double y, double w, double h)
        {
            X = x; Y = y; W = w; H = h;
        }
    }

    // Pure rules shared by the card, tray icon and sampler: thresholds, unit conversion, formatting, placement.
    public static class Rules
    {
        // Rounds the way every number on the card is displayed, so colour and text always agree.
        public static double Display(double v)
        {
            return Math.Round(v, MidpointRounding.AwayFromZero);
        }

        public static Level Classify(double? value, double amber, double red)
        {
            if (!value.HasValue) return Level.Unknown;
            double v = Display(value.Value);
            if (v >= red) return Level.Red;
            if (v >= amber) return Level.Amber;
            return Level.Ok;
        }

        // ACPI thermal zones report Kelvin with 273.2 K as 0 °C ("High Precision Temperature" in tenths of Kelvin).
        // A zone without a sensor reads exactly 273.2 K, so anything at or below 0 °C or above 150 °C is not a reading.
        public static double? ThermalToCelsius(double raw, bool tenthsOfKelvin)
        {
            double c = (tenthsOfKelvin ? raw / 10.0 : raw) - 273.2;
            if (double.IsNaN(c) || c <= 0 || c > 150) return null;
            return c;
        }

        public static double ClampPercent(double v)
        {
            if (double.IsNaN(v) || v < 0) return 0;
            return v > 100 ? 100 : v;
        }

        public static double? Percent(double part, double whole)
        {
            if (whole <= 0 || double.IsNaN(part) || part < 0) return null;
            return ClampPercent(part / whole * 100.0);
        }

        // Bytes per second from per-adapter cumulative counters. Only adapters present in both samples count, so an adapter
        // that just (re)appeared never shows its lifetime total as one second of traffic; a counter that went backwards adds 0.
        // Null when there is no baseline, no time passed, or no adapter is common to both samples.
        public static double? AdapterRate(IDictionary<string, long> previous, IDictionary<string, long> current, double seconds)
        {
            if (previous == null || current == null || seconds <= 0) return null;
            long sum = 0;
            bool common = false;
            foreach (KeyValuePair<string, long> adapter in current)
            {
                long before;
                if (!previous.TryGetValue(adapter.Key, out before)) continue;
                common = true;
                if (adapter.Value >= before) sum += adapter.Value - before;
            }
            return common ? sum / seconds : (double?)null;
        }

        // PowerStatus.BatteryLifePercent is 0..1, or 2.55 when the charge is unknown.
        public static double? BatteryPercent(float lifePercent)
        {
            if (float.IsNaN(lifePercent) || lifePercent < 0 || lifePercent > 1) return null;
            return Display(lifePercent * 100.0);
        }

        public static double TempRingFraction(double? celsius, double min, double max)
        {
            if (!celsius.HasValue || max <= min) return 0;
            double f = (celsius.Value - min) / (max - min);
            return f < 0 ? 0 : (f > 1 ? 1 : f);
        }

        public static string FormatNumber(double? v)
        {
            return v.HasValue ? Display(v.Value).ToString(CultureInfo.InvariantCulture) : "--";
        }

        public static string FormatPercent(double? v)
        {
            return v.HasValue ? FormatNumber(v) + "%" : "--";
        }

        public static string FormatTemp(double? celsius, bool withUnit)
        {
            return celsius.HasValue ? FormatNumber(celsius) + (withUnit ? "\u00B0C" : "\u00B0") : "--";
        }

        // KB/s (1 KB = 1024 bytes) below 1000 KB/s, otherwise MB/s with one decimal.
        public static string FormatSpeed(double? bytesPerSecond)
        {
            if (!bytesPerSecond.HasValue) return "--";
            double kb = bytesPerSecond.Value / 1024.0;
            if (Display(kb) < 1000) return Display(kb).ToString(CultureInfo.InvariantCulture) + " KB/s";
            return (kb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s";
        }

        public static string Tooltip(double? cpuPercent, double? cpuTempC)
        {
            return "CPU " + FormatPercent(cpuPercent) + " \u00B7 " + FormatTemp(cpuTempC, true);
        }

        // True when at least half of the card's area lies inside a single work area.
        public static bool IsMostlyOnScreen(Box card, IList<Box> workAreas)
        {
            double area = card.W * card.H;
            if (area <= 0) return false;
            foreach (Box wa in workAreas)
            {
                double w = Math.Min(card.X + card.W, wa.X + wa.W) - Math.Max(card.X, wa.X);
                double h = Math.Min(card.Y + card.H, wa.Y + wa.H) - Math.Max(card.Y, wa.Y);
                if (w > 0 && h > 0 && w * h >= area / 2) return true;
            }
            return false;
        }

        public static Box DefaultPlacement(Box workArea, double cardW, double cardH, double margin)
        {
            return new Box(workArea.X + workArea.W - cardW - margin, workArea.Y + margin, cardW, cardH);
        }

        // Details panel beside the card: left of it when there is room, otherwise right of it, top-aligned with the card,
        // and always kept inside the work area.
        public static Box PanelPlacement(Box card, Box workArea, double panelW, double panelH, double gap)
        {
            double x = card.X - gap - panelW >= workArea.X ? card.X - gap - panelW : card.X + card.W + gap;
            x = Math.Max(workArea.X, Math.Min(x, workArea.X + workArea.W - panelW));
            double y = Math.Max(workArea.Y, Math.Min(card.Y, workArea.Y + workArea.H - panelH));
            return new Box(x, y, panelW, panelH);
        }

        // "Below full speed" for the CPU limit: under 99.5, i.e. it would display as under 100 %.
        public static bool IsBelowFullSpeed(double limitPercent)
        {
            return limitPercent < 99.5;
        }

        public static string FormatCpuSpeed(double? limitPercent, string reason)
        {
            if (!limitPercent.HasValue) return "--";
            return IsBelowFullSpeed(limitPercent.Value) ? "Limited to " + FormatPercent(limitPercent) + " \u00B7 " + reason : "Full speed";
        }

        // Whole GB below 1000 GB, otherwise TB with one decimal; 1 GB = 1024^3 bytes, as Explorer shows it.
        public static string FormatDiskFree(double? freeBytes)
        {
            if (!freeBytes.HasValue) return "--";
            double gb = freeBytes.Value / (1024.0 * 1024 * 1024);
            if (Display(gb) < 1000) return Display(gb).ToString(CultureInfo.InvariantCulture) + " GB";
            return (gb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " TB";
        }

        // Whole MB below 1000 MB, otherwise GB with one decimal.
        public static string FormatMemory(long bytes)
        {
            double mb = bytes / (1024.0 * 1024);
            if (Display(mb) < 1000) return Display(mb).ToString(CultureInfo.InvariantCulture) + " MB";
            return (mb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        public static string FormatBattery(double? percent, bool onAc, bool charging, int? minutesLeft)
        {
            if (!percent.HasValue) return "--";
            string pct = FormatPercent(percent);
            if (onAc) return (charging ? "Charging \u00B7 " : "Plugged in \u00B7 ") + pct;
            if (!minutesLeft.HasValue || minutesLeft.Value <= 0) return "On battery \u00B7 " + pct;
            int h = minutesLeft.Value / 60, m = minutesLeft.Value % 60;
            return (h > 0 ? h + " h " + m + " min left" : m + " min left") + " \u00B7 " + pct;
        }

        // 60 -> "1 min", 120 -> "2 min", 90 -> "90 s".
        public static string FormatDuration(double seconds)
        {
            int s = (int)Display(seconds);
            return s >= 60 && s % 60 == 0 ? (s / 60) + " min" : s + " s";
        }
    }
}
