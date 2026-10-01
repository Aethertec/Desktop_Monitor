using System;
using System.Threading;

namespace DesktopMonitor
{
    // Prints live readings once a second for comparison with Task Manager (plan Task 4).
    //   SamplerDump.exe [seconds]   default 10
    internal static class SamplerDump
    {
        [STAThread]
        private static void Main(string[] args)
        {
            int seconds = args.Length > 0 ? int.Parse(args[0]) : 10;
            using (var sampler = new MetricsSampler(new AppSettings()))
            {
                for (int i = 0; i < seconds; i++)
                {
                    Snapshot s = sampler.Sample();
                    Console.WriteLine(s.Time.ToString("HH:mm:ss")
                        + "  cpu " + Rules.FormatPercent(s.CpuPercent)
                        + "  ram " + Rules.FormatPercent(s.RamPercent)
                        + "  gpu " + Rules.FormatPercent(s.GpuPercent)
                        + "  disk " + Rules.FormatPercent(s.DiskPercent)
                        + "  down " + Rules.FormatSpeed(s.DownBytesPerSec)
                        + "  up " + Rules.FormatSpeed(s.UpBytesPerSec)
                        + "  battery " + Rules.FormatPercent(s.BatteryPercent) + (s.OnAc ? " AC" : "")
                        + "  temps cpu " + Rules.FormatTemp(s.CpuTempC, true)
                        + " skin " + Rules.FormatTemp(s.SkinTempC, true)
                        + " battery " + Rules.FormatTemp(s.BatteryTempC, true));
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
