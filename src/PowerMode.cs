using System;
using System.Runtime.InteropServices;

namespace DesktopMonitor
{
    public enum PowerModeKind { Unknown, Efficiency, Balanced, Performance }

    // The Windows 11 power mode (Settings > System > Power & battery slider) via the overlay-scheme calls in
    // powrprof.dll. They are undocumented, so the v2 plan's Task 1 spike decides whether the panel may switch modes.
    internal static class PowerMode
    {
        public static readonly Guid EfficiencyId = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid BalancedId = Guid.Empty;
        public static readonly Guid PerformanceId = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActualOverlayScheme(out Guid overlay);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid overlay);

        // The user's selection (what the Settings slider shows).
        public static PowerModeKind Get()
        {
            Guid g;
            return PowerGetActualOverlayScheme(out g) == 0 ? FromGuid(g) : PowerModeKind.Unknown;
        }

        // What Windows is applying right now (differs from Get() while energy saver is on).
        public static PowerModeKind GetEffective()
        {
            Guid g;
            return PowerGetEffectiveOverlayScheme(out g) == 0 ? FromGuid(g) : PowerModeKind.Unknown;
        }

        public static bool Set(PowerModeKind mode)
        {
            if (mode == PowerModeKind.Unknown) return false;
            return PowerSetActiveOverlayScheme(ToGuid(mode)) == 0;
        }

        public static PowerModeKind FromGuid(Guid g)
        {
            if (g == EfficiencyId) return PowerModeKind.Efficiency;
            if (g == BalancedId) return PowerModeKind.Balanced;
            if (g == PerformanceId) return PowerModeKind.Performance;
            return PowerModeKind.Unknown;
        }

        public static Guid ToGuid(PowerModeKind mode)
        {
            switch (mode)
            {
                case PowerModeKind.Efficiency: return EfficiencyId;
                case PowerModeKind.Performance: return PerformanceId;
                default: return BalancedId;
            }
        }

        public static string Name(PowerModeKind mode)
        {
            return mode == PowerModeKind.Unknown ? "--" : mode.ToString();
        }
    }
}
