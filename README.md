# Desktop Monitor

A small gauge card on the Windows desktop showing CPU, RAM, GPU, disk, network, battery and temperatures for this HP ProBook 440 G8. Native C#/WPF built with the compiler that ships with Windows: nothing to install, no admin rights.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1          # bin\DesktopMonitor.exe
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test    # unit tests
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Dump    # bin\SamplerDump.exe: live readings in a console
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Power   # bin\PowerModeTool.exe: read or set the power mode
```

The built-in compiler only understands C# 5: no `$"..."` strings, `?.`, `nameof`, `=>` members or auto-property initialisers.

## Use

Run `bin\DesktopMonitor.exe`. The card sits on the desktop behind your windows (top-right by default) and clicks pass through it.

Tray icon (shows the CPU temperature), right-click:

- **Unlock to move**: drag the card; it locks again when you let go and remembers the spot.
- **Start with Windows**: adds or removes the entry under `HKCU\...\Run`.
- **Open settings**
- **Exit**

Colours: usage amber at 70 % and red at 85 %; temperatures amber at 80 °C and red at 85 °C; disk amber at 90 % and red at 95 %.

## Details panel

Double-click the tray icon (or tray menu → **Details**) to open it beside the card; Esc or a click elsewhere closes it.

- **CPU speed**: full speed, or "Limited to NN% · heat / power saving".
- **Top apps**: the 5 apps using the most CPU, with memory.
- **Network**: Wi-Fi name and signal, IP (click to copy) and gateway. Virtual adapters (VMware, VirtualBox, Hyper-V/WSL) are hidden.
- **COM ports**: each serial device by name. A newly plugged port is highlighted for 10 s; click the port to copy "COMn".
- **Alerts**: today's newest 5; all of them are in `alerts.csv`.
- **Power mode**: Efficiency / Balanced / Performance, the same setting as the Windows Settings slider.
- **Battery** and **CSV log** (click to switch logging off or on, open the log folder, open Task Manager).

## Alerts

Windows notifications, once per episode: CPU hot (≥ 90 °C for 1 min), throttled (2 min), disk low (< 10 GB free), battery low (< 20 % on battery), battery hot (≥ 45 °C for 1 min), COM port connected. Limits and on/off switches are in `settings.json` (`alert...` keys); `alertsEnabled: false` silences them all.

## CSV log

Every 10 s to `%LOCALAPPDATA%\DesktopMonitor\logs\YYYY-MM-DD.csv` (kept 30 days): CPU, CPU limit, RAM, GPU, C: free, network, battery, temperatures, top app, power mode. CPU, GPU and network are 10 s averages; CPU temperature is the 10 s peak. Opens in Excel; rows written while the file is open in Excel are kept and added once it closes.

## Settings

`%APPDATA%\DesktopMonitor\settings.json`. Saved changes apply within about a second, except `x`/`y`, which are read at start-up. An invalid edit is ignored (the last good settings stay) and noted in the log.

| Key | Default | Meaning |
|---|---|---|
| `x`, `y` | `null` | Card position in DIPs; `null` = top-right |
| `opacity` | `0.93` | Card background opacity, 0.2–1 |
| `usageAmber`, `usageRed` | `70`, `85` | CPU / RAM / GPU thresholds (%) |
| `tempAmber`, `tempRed` | `80`, `85` | Temperature thresholds (°C) |
| `diskAmber`, `diskRed` | `90`, `95` | Disk C: thresholds (%) |
| `tempRingMin`, `tempRingMax` | `30`, `100` | °C range of the CPU temp ring |
| `zoneCpu`, `zoneSkin`, `zoneBattery` | `CPUZ`, `SK1Z`, `BATZ` | ACPI thermal zones to read |
| `logEnabled`, `logIntervalSeconds`, `logRetentionDays` | `true`, `10`, `30` | CSV log on/off, row interval (5–300 s), days kept |
| `alertsEnabled` and `alert...On` | `true` | All alerts, and each one |
| `alertCpuTemp`, `alertCpuClear`, `alertCpuSeconds` | `90`, `85`, `60` | CPU hot: fire at, clear below, seconds held |
| `alertThrottleSeconds` | `120` | Seconds below full speed before the throttled alert |
| `alertDiskFreeGb`, `alertDiskClearGb` | `10`, `12` | Disk low: fire below, clear above (GB free) |
| `alertBatteryPercent` | `20` | Battery low on battery |
| `alertBatteryTemp`, `alertBatteryTempClear`, `alertBatteryTempSeconds` | `45`, `42`, `60` | Battery hot |

## Troubleshooting

- Log: `%APPDATA%\DesktopMonitor\log.txt`.
- A value shows `--`: that reading failed or the sensor is empty; the log says which.
- Card missing after Explorer restarts: it re-attaches within 5 s.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools\inspect-window.ps1` shows the card's owner, click-through state and z-order.
- Temperatures are ACPI thermal-zone values (whole-package, updated every few seconds), not per-core readings.
