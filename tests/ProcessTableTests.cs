using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DesktopMonitor.Tests
{
    internal static class ProcessTableTests
    {
        private const long Second = 10000000; // 100 ns units
        private const long Mb = 1024 * 1024;

        public static void Run()
        {
            CpuPercent_IsShareOfAllLogicalCpus();
            Groups_ProcessesWithTheSameName();
            NewProcess_CountsMemoryButNoCpu();
            ExitedProcess_IsGone();
            ReusedPid_DoesNotJump();
            Idle_IsLeftOut();
            Sorted_ByCpuThenMemory();
            ZeroElapsed_GivesZeroNotInfinity();
            Clamped_At100();
            FormatTopApp_Text();
            Reader_FindsThisProcess();
        }

        private static ProcessSample P(int pid, string name, long cpu, long mem, long created = 1)
        {
            return new ProcessSample(pid, created, name, cpu, mem);
        }

        private static List<ProcessSample> L(params ProcessSample[] items)
        {
            return new List<ProcessSample>(items);
        }

        private static void CpuPercent_IsShareOfAllLogicalCpus()
        {
            var apps = ProcessTable.Compare(L(P(10, "brave.exe", 0, 100 * Mb)), L(P(10, "brave.exe", 2 * Second, 100 * Mb)), 1.0, 8);
            TestMain.Near(25, apps[0].CpuPercent, "2 s of CPU in 1 s on 8 logical CPUs is 25 %");
            TestMain.Equal("brave", apps[0].Name, ".exe is dropped");
        }

        private static void Groups_ProcessesWithTheSameName()
        {
            var before = L(P(10, "brave.exe", 0, 100 * Mb), P(11, "Brave.exe", 0, 50 * Mb));
            var after = L(P(10, "brave.exe", Second, 100 * Mb), P(11, "Brave.exe", Second, 50 * Mb));
            var apps = ProcessTable.Compare(before, after, 1.0, 8);
            TestMain.Equal(1, apps.Count, "both processes are one app, whatever the case");
            TestMain.Near(25, apps[0].CpuPercent, "CPU is summed");
            TestMain.Equal(150 * Mb, apps[0].PrivateBytes, "memory is summed");
        }

        private static void NewProcess_CountsMemoryButNoCpu()
        {
            var apps = ProcessTable.Compare(L(), L(P(20, "Code.exe", 5 * Second, 800 * Mb)), 1.0, 8);
            TestMain.Near(0, apps[0].CpuPercent, "no CPU without an earlier sample");
            TestMain.Equal(800 * Mb, apps[0].PrivateBytes, "memory is known straight away");
        }

        private static void ExitedProcess_IsGone()
        {
            var apps = ProcessTable.Compare(L(P(30, "old.exe", 0, Mb)), L(), 1.0, 8);
            TestMain.Equal(0, apps.Count, "a process that exited is not listed");
        }

        private static void ReusedPid_DoesNotJump()
        {
            var apps = ProcessTable.Compare(L(P(40, "a.exe", 0, Mb, 1)), L(P(40, "b.exe", 900 * Second, Mb, 2)), 1.0, 8);
            TestMain.Near(0, apps[0].CpuPercent, "same PID, new create time: a different process");
        }

        private static void Idle_IsLeftOut()
        {
            var apps = ProcessTable.Compare(L(P(0, "Idle", 0, 0)), L(P(0, "Idle", 7 * Second, 0)), 1.0, 8);
            TestMain.Equal(0, apps.Count, "the Idle process is never an app");
        }

        private static void Sorted_ByCpuThenMemory()
        {
            var before = L(P(1, "a.exe", 0, 10 * Mb), P(2, "b.exe", 0, 10 * Mb), P(3, "c.exe", 0, 90 * Mb));
            var after = L(P(1, "a.exe", Second / 10, 10 * Mb), P(2, "b.exe", Second, 10 * Mb), P(3, "c.exe", Second / 10, 90 * Mb));
            var apps = ProcessTable.Compare(before, after, 1.0, 8);
            TestMain.Equal("b", apps[0].Name, "highest CPU first");
            TestMain.Equal("c", apps[1].Name, "equal CPU: more memory first");
            TestMain.Equal("a", apps[2].Name, "then the rest");
        }

        private static void ZeroElapsed_GivesZeroNotInfinity()
        {
            var apps = ProcessTable.Compare(L(P(1, "a.exe", 0, Mb)), L(P(1, "a.exe", Second, Mb)), 0, 8);
            TestMain.Near(0, apps[0].CpuPercent, "no time passed");
        }

        private static void Clamped_At100()
        {
            var apps = ProcessTable.Compare(L(P(1, "a.exe", 0, Mb)), L(P(1, "a.exe", 100 * Second, Mb)), 1.0, 8);
            TestMain.Near(100, apps[0].CpuPercent, "never more than 100 %");
        }

        private const double Gb = 1024.0 * 1024 * 1024;

        private static void FormatTopApp_Text()
        {
            TestMain.Equal("brave \u00B7 34% CPU \u00B7 1.9 GB", Rules.FormatTopApp(new AppUsage { Name = "brave", CpuPercent = 34.2, PrivateBytes = (long)(1.9 * Gb) }), "top app line");
            TestMain.Equal("--", Rules.FormatTopApp(null), "no app yet");
        }

        // Live: the NtQuerySystemInformation reader sees this test process with the right name and CPU time.
        private static void Reader_FindsThisProcess()
        {
            Process me = Process.GetCurrentProcess();
            using (var reader = new ProcessReader())
            {
                List<ProcessSample> all = reader.Read();
                long expected = me.TotalProcessorTime.Ticks;
                ProcessSample found = all.Find(x => x.Pid == me.Id);
                TestMain.Equal(me.ProcessName, ProcessTable.DisplayName(found.Name), "this process is listed under its own name");
                TestMain.True(Math.Abs(found.CpuTime - expected) < 5000000, "its CPU time matches Process.TotalProcessorTime within 0.5 s");
                TestMain.True(found.PrivateBytes > 0, "its private memory is known");
                TestMain.True(all.Count > 20, "a normal desktop has well over 20 processes (" + all.Count + ")");
            }
        }
    }
}
