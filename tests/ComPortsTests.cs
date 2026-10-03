using System;
using System.Collections.Generic;

namespace DesktopMonitor.Tests
{
    internal static class ComPortsTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 3, 14, 0, 0);

        public static void Run()
        {
            PortsPresentAtStart_AreNotNew();
            PluggedInPort_IsNewForTenSeconds();
            UnpluggedPort_Disappears_AndReplugAppearsAgain();
            Ports_AreInNaturalOrder();
            Names_ShowUpOnceKnown();
            Reader_ListsPortsWithoutError();
            ApplyNames_FillsNames_AndMarksUnnamedOnlyForPortsLookedUp();
        }

        // Final review M4: a finished lookup must end the "…" state for the ports it covered, but must not touch a port
        // that appeared after it started (that one gets its own lookup).
        private static void ApplyNames_FillsNames_AndMarksUnnamedOnlyForPortsLookedUp()
        {
            var t = new ComPortTracker();
            t.Update(Ports("COM5", "COM7", "COM8"), T0);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            names["COM5"] = "Silicon Labs CP210x";
            t.ApplyNames(names, Ports("COM5", "COM7"));
            List<ComPortInfo> c = t.Current(T0);
            TestMain.Equal("Silicon Labs CP210x", c[0].Name, "found name applied");
            TestMain.Equal("", c[1].Name, "looked up but no name: marked unnamed, not pending");
            TestMain.Equal<string>(null, c[2].Name, "not part of that lookup: still pending");
        }

        private static List<string> Ports(params string[] p)
        {
            return new List<string>(p);
        }

        private static void PortsPresentAtStart_AreNotNew()
        {
            var t = new ComPortTracker();
            TestMain.Equal(0, t.Update(Ports("COM3"), T0).Count, "first poll reports nothing as appeared");
            ComPortInfo p = t.Current(T0)[0];
            TestMain.True(!p.AppearedLive && !p.IsNew, "a port there at start-up is neither live nor new");
        }

        private static void PluggedInPort_IsNewForTenSeconds()
        {
            var t = new ComPortTracker();
            t.Update(Ports(), T0);
            List<string> appeared = t.Update(Ports("COM5"), T0.AddSeconds(2));
            TestMain.Equal(1, appeared.Count, "COM5 appeared");
            TestMain.True(t.Current(T0.AddSeconds(11))[0].IsNew, "still new 9 s after it appeared");
            TestMain.True(!t.Current(T0.AddSeconds(12))[0].IsNew, "not new 10 s after it appeared");
            TestMain.True(t.Current(T0.AddSeconds(12))[0].AppearedLive, "it did appear while running");
        }

        private static void UnpluggedPort_Disappears_AndReplugAppearsAgain()
        {
            var t = new ComPortTracker();
            t.Update(Ports(), T0);
            t.Update(Ports("COM5"), T0.AddSeconds(2));
            t.Update(Ports(), T0.AddSeconds(4));
            TestMain.Equal(0, t.Current(T0.AddSeconds(4)).Count, "unplugged port is gone");
            TestMain.Equal(1, t.Update(Ports("COM5"), T0.AddSeconds(6)).Count, "plugging it back in counts as appearing again");
        }

        private static void Ports_AreInNaturalOrder()
        {
            var t = new ComPortTracker();
            t.Update(Ports("COM10", "COM3", "COM4"), T0);
            List<ComPortInfo> c = t.Current(T0);
            TestMain.Equal("COM3,COM4,COM10", c[0].Port + "," + c[1].Port + "," + c[2].Port, "COM3 before COM10");
        }

        private static void Names_ShowUpOnceKnown()
        {
            var t = new ComPortTracker();
            t.Update(Ports("COM5"), T0);
            TestMain.Equal<string>(null, t.Current(T0)[0].Name, "no name before the lookup");
            t.SetName("COM5", "Silicon Labs CP210x USB to UART Bridge");
            TestMain.Equal("Silicon Labs CP210x USB to UART Bridge", t.Current(T0)[0].Name, "name after the lookup");
        }

        // Live: the registry list reads on this PC (an empty list is fine when nothing is plugged in).
        private static void Reader_ListsPortsWithoutError()
        {
            List<string> ports = ComPortReader.ReadPorts();
            TestMain.True(ports.TrueForAll(x => x.StartsWith("COM", StringComparison.OrdinalIgnoreCase)), "every entry is a COM port (" + ports.Count + " now)");
        }
    }
}
