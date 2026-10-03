# Power-mode spike result (2026-10-03)

| Check | Result |
|---|---|
| Read selected / effective mode | Pass (selected Balanced, effective Efficiency) |
| Set Efficiency returns ok | Pass |
| Settings shows Best power efficiency | Pass |
| Restored to the original mode, Settings shows Balanced | Pass |

Decision: PowerSwitchAvailable = true

Notes: On this Windows 11 build the setting is the "Power mode" drop-down under System > Power & battery > Power (not a slider). Settings only showed the change after the page was reopened, so the check closed and reopened it. The effective mode stayed Efficiency while Balanced was selected, which is consistent with Windows' energy saver being active; the panel shows the selected mode, as the spec says.
