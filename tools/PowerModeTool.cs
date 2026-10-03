using System;

namespace DesktopMonitor
{
    // Spike and diagnostic tool for the power-mode switch (v2 plan Task 1).
    //   PowerModeTool.exe                     prints the selected and effective mode
    //   PowerModeTool.exe set Efficiency      sets a mode (Efficiency, Balanced or Performance), then prints again
    internal static class PowerModeTool
    {
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "set")
            {
                PowerModeKind mode;
                if (!Enum.TryParse(args[1], true, out mode) || mode == PowerModeKind.Unknown)
                {
                    Console.WriteLine("Unknown mode '" + args[1] + "'. Use Efficiency, Balanced or Performance.");
                    return 2;
                }
                Console.WriteLine("set " + mode + ": " + (PowerMode.Set(mode) ? "ok" : "FAILED"));
            }
            Console.WriteLine("selected: " + PowerMode.Name(PowerMode.Get()) + "  effective: " + PowerMode.Name(PowerMode.GetEffective()));
            return 0;
        }
    }
}
