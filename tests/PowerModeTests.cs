using System;

namespace DesktopMonitor.Tests
{
    internal static class PowerModeTests
    {
        public static void Run()
        {
            PowerMode_GuidMapping();
        }

        private static void PowerMode_GuidMapping()
        {
            foreach (PowerModeKind m in new[] { PowerModeKind.Efficiency, PowerModeKind.Balanced, PowerModeKind.Performance })
                TestMain.Equal(m, PowerMode.FromGuid(PowerMode.ToGuid(m)), "round trip " + m);
            TestMain.Equal(PowerModeKind.Unknown, PowerMode.FromGuid(new Guid("11111111-2222-3333-4444-555555555555")), "unknown GUID");
            TestMain.Equal("--", PowerMode.Name(PowerModeKind.Unknown), "unknown shows --");
        }
    }
}
