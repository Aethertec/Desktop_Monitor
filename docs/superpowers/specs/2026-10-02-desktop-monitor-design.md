# Desktop Monitor — design spec

Date: 2026-10-02
Status: implemented 2026-10-02 (see spike/RESULT.md for the pinning mode). Open: sleep/resume check, start-with-Windows reboot check; working set measured 110 MB against the < 50 MB target (decision pending).

## 1. Goal

A small always-present card on the Windows desktop showing this laptop's live usage and temperatures at a glance, without admin rights, extra installs or noticeable load.

Target machine: HP ProBook 440 G8 — i5-1135G7 (4C/8T), 15.7 GB RAM, Intel Iris Xe, C: 279 GB, Windows 11 Home, 1366×768 display.

Success criteria:

- Card is visible on the desktop layer (behind app windows) and survives Win+D and an Explorer restart.
- Mouse clicks pass through the card to the desktop beneath, except in move mode.
- All values refresh live and match Task Manager within normal sampling noise.
- Average CPU use of the app < 1%, working set < 50 MB.
- Runs with no admin rights and nothing to install beyond the built .exe.

## 2. Decisions made during brainstorming

| Topic | Decision |
|---|---|
| Visual style | Option B, "gauge card" (three rings + stats grid), dark card |
| Window layer | Desktop layer (behind all app windows) |
| Mouse | Click-through always, except while unlocked for moving |
| Default position | Top-right, 12 px margins; remembers dragged position |
| Stack | Native C# / WPF on .NET Framework 4.8, compiled with the built-in `csc.exe` |
| Temp thresholds | Amber ≥ 80 °C, red ≥ 85 °C |
| Usage thresholds | Amber ≥ 70 %, red ≥ 85 % (CPU, RAM, GPU) |
| Disk thresholds | Amber ≥ 90 %, red ≥ 95 % |
| Extra metric | Upload speed added alongside download |

## 3. Card content and layout

Fixed width 250 px (DIPs), padding 14 px, height set by content (~190 px). Font Segoe UI.

```
┌──────────────────────────────────┐
│ System                     14:32 │  header: label + clock (HH:mm)
│   ◯ 23%      ◯ 62%      ◯ 77°    │  rings: 64 px, stroke 6, radius 25
│    CPU        RAM      CPU temp  │
│ GPU        8%   Disk C:     87%  │  2-column grid, 14 px col gap,
│ Net ↓  1.2 MB/s Net ↑   80 KB/s  │  6 px row gap
│ Battery 99% ⏚ 39° Skin     56°C  │
└──────────────────────────────────┘
```

| Element | Value shown | Colour rule |
|---|---|---|
| CPU ring | % utility, integer | usage |
| RAM ring | used / total %, integer | usage |
| CPU temp ring | °C integer; arc maps 30–100 °C to 0–100 % | temp |
| GPU | % integer | usage (default text colour when below amber) |
| Disk C: | % used, integer | disk |
| Net ↓ / Net ↑ | `KB/s` below 1 MB/s, else `x.x MB/s` | none |
| Battery | `%`, plug glyph when on AC, then battery temp `°` | temp (on the temp part) |
| Skin | °C integer | temp |

Colours (fixed in code):

| Role | Hex |
|---|---|
| Card background | `#141416` at opacity 0.93 (opacity configurable) |
| Primary text | `#ECECEC` |
| Label text | `#A3A3A3` |
| Ring track | white at 13 % alpha |
| OK | `#5DCAA5` |
| Amber | `#FAC775` |
| Red | `#F09595` |

A value that cannot be read shows `--` in label colour and its ring shows track only.

## 4. Data sources

All available without admin on the target machine (verified 2026-10-02).

| Metric | Source | Interval |
|---|---|---|
| CPU % | PerformanceCounter `Processor Information` / `% Processor Utility` / `_Total` (matches Task Manager) | 1 s |
| RAM | `GlobalMemoryStatusEx` (P/Invoke) | 1 s |
| GPU % | PerformanceCounter `GPU Engine` / `Utilization Percentage`, summed over instances containing `engtype_3D`; instance list re-enumerated every 10 s | 1 s |
| Net ↓ / ↑ | `NetworkInterface.GetAllNetworkInterfaces()`, sum of `BytesReceived` / `BytesSent` over interfaces that are Up and not Loopback/Tunnel; delta ÷ elapsed (Stopwatch) | 1 s |
| Battery | `SystemInformation.PowerStatus` (`BatteryLifePercent`, `PowerLineStatus`) | 10 s |
| Disk C: | `DriveInfo("C")` total vs free | 60 s |
| Temperatures | PerformanceCounter `Thermal Zone Information` / `High Precision Temperature` (tenths of K), falling back to `Temperature` (K). Instance matched case-insensitively by suffix: CPU = `CPUZ`, skin = `SK1Z`, battery = `BATZ` (configurable). °C = K − 273.2 (ACPI convention: an empty zone reads exactly 273.2 K). A reading ≤ 0 °C or > 150 °C is treated as unavailable. | 2 s |

Zones `PCHZ` and `SK2Z` read empty on this machine and are not used.

## 5. Window behaviour

- **Window type:** WPF `Window`, `WindowStyle=None`, `AllowsTransparency=true`, `ShowInTaskbar=false`, `ResizeMode=NoResize`. Extended styles: `WS_EX_TOOLWINDOW` (hidden from Alt+Tab), `WS_EX_NOACTIVATE`, `WS_EX_LAYERED`, plus `WS_EX_TRANSPARENT` while locked.
- **Desktop layer (primary):** find the window hosting `SHELLDLL_DefView` (Progman, or the WorkerW holding it), set it as our owner via `SetWindowLongPtr(GWLP_HWNDPARENT)`, and keep our z-order at the bottom by handling `WM_WINDOWPOSCHANGING` (force `HWND_BOTTOM` placement). Expected effect: above wallpaper and icons, below every app window, still visible after Win+D.
- **Desktop layer (fallback):** if the primary approach fails the spike (section 10, step 1), drop the owner change and keep only the `HWND_BOTTOM` pinning. Win+D may then hide the card until the next window change; acceptable fallback.
- **Re-attach:** listen for the registered `TaskbarCreated` message (sent when Explorer restarts) and re-run the attach; a 5 s watchdog also re-attaches if the owner handle is no longer a valid window.
- **Click-through:** `WS_EX_TRANSPARENT` set at all times while locked.
- **Move mode:** tray → "Unlock to move" clears `WS_EX_TRANSPARENT`, shows a 1 px dashed white outline and the hint "Drag to move · locks when you let go". Left-drag calls `DragMove()`. On mouse-up: save position, re-lock automatically. Menu item reads "Lock position" while unlocked.
- **Position:** stored in DIPs in settings. On start, if there is no saved position, or less than half of the card's area would fall inside a single screen's working area, place top-right of the primary working area with 12 px margins.
- **Single instance:** named mutex `Local\DesktopMonitor`; a second launch exits silently.
- **DPI:** system-DPI-aware via embedded `app.manifest` (WPF default scaling). Per-monitor DPI is out of scope.

## 6. Tray icon

- `System.Windows.Forms.NotifyIcon`.
- Icon: 16×16 bitmap drawn at runtime showing the CPU temperature as a number (e.g. `77`), coloured by the temp rule, redrawn every 2 s. Old `HICON` freed with `DestroyIcon` each redraw.
- Tooltip: `CPU 23% · 77°C`.
- Right-click menu:
  1. Unlock to move / Lock position
  2. Start with Windows (checkable) — `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `DesktopMonitor` = quoted exe path. Registry is the single source of truth; not stored in settings.
  3. Open settings — opens `settings.json` in the default editor
  4. Exit

## 7. Settings

File: `%APPDATA%\DesktopMonitor\settings.json`, created with defaults on first run. Parsed with `JavaScriptSerializer` (System.Web.Extensions). Reloaded automatically on save via `FileSystemWatcher` (debounced 300 ms). A malformed file is ignored (last good settings stay active) and the error is logged.

```json
{
  "x": null,
  "y": null,
  "opacity": 0.93,
  "usageAmber": 70, "usageRed": 85,
  "tempAmber": 80,  "tempRed": 85,
  "diskAmber": 90,  "diskRed": 95,
  "tempRingMin": 30, "tempRingMax": 100,
  "zoneCpu": "CPUZ", "zoneSkin": "SK1Z", "zoneBattery": "BATZ"
}
```

`x`/`y` null means default top-right placement.

## 8. Code structure

C# 5 only (the built-in `csc.exe` 4.8 does not support C# 6+): no string interpolation, no `?.`, no `nameof`, no expression-bodied members, no auto-property initialisers. UI is built in code (no XAML, since `csc` cannot compile it).

```
D:\Projects\Desktop_Monitor\
  build.ps1            compile with csc.exe → bin\DesktopMonitor.exe; -Test builds and runs tests
  app.manifest         asInvoker, DPI awareness
  README.md            build, run, settings, troubleshooting
  src\
    Program.cs         entry point, single-instance mutex, wires units, unhandled-exception logging
    Snapshot.cs        immutable bag of nullable readings (one sample)
    MetricsSampler.cs  owns all counters/readers; Sample() → Snapshot; each reader isolated in try/catch
    Rules.cs           pure logic: colour level for usage/temp/disk, speed formatting, K→°C, temp-ring mapping, on-screen position check
    Settings.cs        defaults, load/save/validate, file watcher → Changed event
    CardView.cs        FrameworkElement; OnRender draws the whole card from Snapshot + Settings
    CardWindow.cs      transparent window, ex-styles, lock/unlock, drag, position save
    DesktopPin.cs      attach to desktop layer, fallback, TaskbarCreated + watchdog re-attach
    TrayIcon.cs        NotifyIcon, temp icon rendering, menu, autostart registry
    Native.cs          P/Invoke declarations and constants
    Log.cs             append to %APPDATA%\DesktopMonitor\log.txt, truncated at 100 KB
  tests\
    RulesTests.cs      console test runner (no framework) for Rules and Settings parsing
  docs\superpowers\specs\2026-10-02-desktop-monitor-design.md
```

Data flow: a `DispatcherTimer` (1 s) calls `MetricsSampler.Sample()`, which returns a `Snapshot`. The `CardView` gets the snapshot and calls `InvalidateVisual()`, and `TrayIcon.Update(snapshot)` refreshes the tray icon. `Settings.Changed` re-applies opacity, thresholds and zones live.

## 9. Error handling

- Every reader returns `null` on failure → shown as `--`; never throws out of `Sample()`.
- GPU counter instances vanish after sleep or driver reset: on exception, drop cached counters and re-enumerate on the next 10 s cycle.
- `SystemEvents.PowerModeChanged` (Resume): reset network baseline and re-create perf counters.
- First sample of rate counters (CPU, GPU) is discarded (perf counters return 0 on first read).
- `Application.DispatcherUnhandledException` and `AppDomain.UnhandledException` → log; dispatcher exceptions marked handled so the card keeps running.
- Desktop-pin failure → log and fall back (section 5).

## 10. Testing and verification

Build order puts the riskiest part first:

1. **Desktop-pin spike:** minimal transparent window + `DesktopPin` only. Verify on this PC: sits behind apps, visible after Win+D, survives `Stop-Process -Name explorer`, click-through to a desktop icon, unlock and drag works. Choose primary or fallback based on the result.
2. **Unit tests** (`build.ps1 -Test`): `Rules` (all threshold boundaries, e.g. temp 79.9 → OK, 80 → amber, 85 → red; disk 89/90/95; speed formatting at 999 KB/s vs 1.0 MB/s; K→°C; invalid readings ≤ 0 or > 150 °C → null; on-screen check) and `Settings` (defaults, missing keys, malformed JSON keeps last good).
3. **Live check:** compare CPU, RAM, GPU and network against Task Manager for 1 minute under idle and load; compare temps against `Get-Counter '\Thermal Zone Information(*)\High Precision Temperature'`.
4. **Resource check:** after 10 minutes, the app's CPU averages < 1 % and working set < 50 MB (Task Manager details).
5. **Lifecycle:** sleep/resume, start with Windows after reboot, second launch exits, settings edit applies live, off-screen saved position resets.

## 11. Out of scope

- Per-core CPU temperatures and fan speed (need LibreHardwareMonitor + admin)
- History graphs, logging metrics to file
- Light theme, resizable card, per-monitor DPI, multi-monitor placement beyond the off-screen reset
- Installer (exe + build script only)
- Possible later: thermal throttling flag from `Thermal Zone Information` / `% Passive Limit`
