using System;
using System.Threading;

namespace DesktopMonitor
{
    // Prints live readings once a second for comparison with Task Manager, netsh and ipconfig (v2 plan Tasks 7 and 8).
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
                        + "  limit " + Rules.FormatPercent(s.CpuLimitPercent) + (s.Throttled ? " THROTTLED" : "")
                        + "  ram " + Rules.FormatPercent(s.RamPercent)
                        + "  gpu " + Rules.FormatPercent(s.GpuPercent)
                        + "  C: free " + Rules.FormatDiskFree(s.DiskFreeBytes)
                        + "  down " + Rules.FormatSpeed(s.DownBytesPerSec)
                        + "  up " + Rules.FormatSpeed(s.UpBytesPerSec)
                        + "  " + Rules.FormatBattery(s.BatteryPercent, s.OnAc, s.Charging, s.BatteryMinutesLeft)
                        + "  temps cpu " + Rules.FormatTemp(s.CpuTempC, true)
                        + " skin " + Rules.FormatTemp(s.SkinTempC, true)
                        + " battery " + Rules.FormatTemp(s.BatteryTempC, true));
                    Console.WriteLine("          top " + (s.TopApps.Count > 0 ? Rules.FormatTopApp(s.TopApps[0]) : "--")
                        + "  COM " + (s.ComPorts.Count == 0 ? "none" : string.Join(", ", s.ComPorts.ConvertAll(p => p.Port + (p.Name != null ? " " + p.Name : "") + (p.IsNew ? " (new)" : "")))));
                    Thread.Sleep(1000);
                }
            }
            foreach (AdapterInfo a in NetworkInfo.Read())
                Console.WriteLine("adapter " + a.Kind + ": " + (a.WifiName != null ? "wifi name present" : "no wifi name") + (a.SignalPercent.HasValue ? ", signal " + a.SignalPercent + "%" : "") + ", ip " + a.Ipv4 + ", gateway " + (a.Gateway ?? "none"));
            Console.WriteLine("power mode " + PowerMode.Name(PowerMode.Get()) + " (effective " + PowerMode.Name(PowerMode.GetEffective()) + ")");
        }
    }
}
