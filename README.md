# Desktop Monitor

A small gauge card on the Windows desktop showing CPU, RAM, GPU, disk, network, battery and temperatures for this HP ProBook 440 G8. Native C#/WPF built with the compiler that ships with Windows: nothing to install, no admin rights.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1          # bin\DesktopMonitor.exe
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test    # unit tests
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Dump    # bin\SamplerDump.exe: live readings in a console
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

## Troubleshooting

- Log: `%APPDATA%\DesktopMonitor\log.txt`.
- A value shows `--`: that reading failed or the sensor is empty; the log says which.
- Card missing after Explorer restarts: it re-attaches within 5 s.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools\inspect-window.ps1` shows the card's owner, click-through state and z-order.
- Temperatures are ACPI thermal-zone values (whole-package, updated every few seconds), not per-core readings.
