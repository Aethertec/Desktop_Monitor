using System;

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
        public double? DownBytesPerSec;
        public double? UpBytesPerSec;
        public double? BatteryPercent;
        public bool OnAc;
        public double? CpuTempC;
        public double? SkinTempC;
        public double? BatteryTempC;
    }
}
