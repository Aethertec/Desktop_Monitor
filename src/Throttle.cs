namespace DesktopMonitor
{
    // Debounces the CPU "% Performance Limit" reading for the card's pill: two consecutive reads below full speed
    // show it, one read at full speed hides it, a missing read changes nothing.
    public sealed class Throttle
    {
        private int _below;
        private bool _shown;

        public bool Shown { get { return _shown; } }

        public void Feed(double? limitPercent)
        {
            if (!limitPercent.HasValue) return;
            if (Rules.IsBelowFullSpeed(limitPercent.Value))
            {
                _below++;
                if (_below >= 2) _shown = true;
            }
            else
            {
                _below = 0;
                _shown = false;
            }
        }

        // Windows does not say why the CPU is limited, so the reason is inferred: heat first, then battery power saving.
        public static string Reason(double? cpuTempC, double tempRed, bool onAc)
        {
            if (cpuTempC.HasValue && Rules.Display(cpuTempC.Value) >= tempRed) return "heat";
            if (!onAc) return "power saving";
            return "limit";
        }
    }
}
