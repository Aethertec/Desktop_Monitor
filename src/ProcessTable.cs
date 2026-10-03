using System;
using System.Collections.Generic;

namespace DesktopMonitor
{
    // One process as read from NtQuerySystemInformation. CpuTime is user + kernel time in 100 ns units.
    public struct ProcessSample
    {
        public int Pid;
        public long CreateTime;
        public string Name;
        public long CpuTime;
        public long PrivateBytes;

        public ProcessSample(int pid, long createTime, string name, long cpuTime, long privateBytes)
        {
            Pid = pid; CreateTime = createTime; Name = name; CpuTime = cpuTime; PrivateBytes = privateBytes;
        }
    }

    // One app: all processes with the same image name, e.g. every brave.exe.
    public sealed class AppUsage
    {
        public string Name;
        public double CpuPercent;
        public long PrivateBytes;
    }

    public static class ProcessTable
    {
        // Per-app CPU % between two samples, Task Manager style (share of all logical CPUs), highest first.
        // A process only contributes CPU when the same PID with the same create time is in both samples,
        // so exited processes and reused PIDs never produce a jump. The Idle process (PID 0) is left out.
        public static List<AppUsage> Compare(IList<ProcessSample> before, IList<ProcessSample> after, double elapsedSeconds, int logicalCpus)
        {
            var previous = new Dictionary<string, ProcessSample>();
            foreach (ProcessSample p in before) previous[Key(p)] = p;
            var apps = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
            double scale = elapsedSeconds > 0 && logicalCpus > 0 ? 100.0 / (elapsedSeconds * 1e7 * logicalCpus) : 0;
            foreach (ProcessSample p in after)
            {
                if (p.Pid == 0) continue;
                string name = DisplayName(p.Name);
                AppUsage app;
                if (!apps.TryGetValue(name, out app))
                {
                    app = new AppUsage { Name = name };
                    apps[name] = app;
                }
                app.PrivateBytes += p.PrivateBytes;
                ProcessSample q;
                if (previous.TryGetValue(Key(p), out q) && p.CpuTime >= q.CpuTime) app.CpuPercent += (p.CpuTime - q.CpuTime) * scale;
            }
            var list = new List<AppUsage>(apps.Values);
            foreach (AppUsage a in list) a.CpuPercent = Rules.ClampPercent(a.CpuPercent);
            list.Sort(delegate(AppUsage x, AppUsage y)
            {
                int byCpu = y.CpuPercent.CompareTo(x.CpuPercent);
                return byCpu != 0 ? byCpu : y.PrivateBytes.CompareTo(x.PrivateBytes);
            });
            return list;
        }

        // "brave.exe" shows as "brave"; names without .exe (System, Registry) stay as they are.
        public static string DisplayName(string imageName)
        {
            if (string.IsNullOrEmpty(imageName)) return "System";
            return imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName.Substring(0, imageName.Length - 4) : imageName;
        }

        private static string Key(ProcessSample p)
        {
            return p.Pid + ":" + p.CreateTime;
        }
    }
}
