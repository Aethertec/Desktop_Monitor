using System;
using System.Collections.Generic;

namespace DesktopMonitor
{
    // One sample of every reading; null means "could not be read" and is shown as "--".
    // The sampler creates a new instance each tick and nothing modifies it afterwards.
    public sealed class Snapshot
    {
        public DateTime Time = DateTime.Now;
        public double? CpuPercent;
        public double? RamPercent;
        public double? GpuPercent;
        public double? DiskPercent;
        public double? DiskFreeBytes;
        public double? DownBytesPerSec;
        public double? UpBytesPerSec;
        public double? BatteryPercent;
        public bool OnAc;
        public bool Charging;
        public int? BatteryMinutesLeft;
        public double? CpuTempC;
        public double? SkinTempC;
        public double? BatteryTempC;
        public double? CpuLimitPercent;
        public bool Throttled;
        public List<AppUsage> TopApps = new List<AppUsage>();       // up to 5, highest CPU first
        public List<ComPortInfo> ComPorts = new List<ComPortInfo>(); // natural port order
    }
}
