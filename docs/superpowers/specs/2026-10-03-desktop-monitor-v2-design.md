# Desktop Monitor v2 — design spec

Date: 2026-10-03
Status: implemented 2026-10-03 (see spike/POWER-RESULT.md for the power switch)
Builds on: `2026-10-02-desktop-monitor-design.md` (v1). Everything in v1 stays unless this document changes it.

## 1. Goal

Turn the card from "it's hot" into "it's hot, here's why, and here's whether your fix worked", and add the day-to-day information an electronics / IoT engineer reaches for, without crowding the desktop.

Success criteria:

- The card shows when the CPU is being held below full speed, the last 10 minutes of CPU temperature, and which app is using the CPU.
- A details panel, one double-click away, shows top apps, network, COM ports, alerts, power mode and battery.
- Alerts arrive as Windows notifications for heat, throttling, low disk, low or hot battery, and newly connected COM ports, each once per episode.
- A CSV log records readings every 10 s for before/after comparisons.
- App CPU < 1 % with the panel closed and < 2 % while it is open; working set < 150 MB (v1 target as relaxed on 2026-10-03).
- Still no admin rights, no installs, built with the built-in `csc.exe` (C# 5).

## 2. Decisions made during brainstorming

| Topic | Decision |
|---|---|
| Layout | Option B: card plus a details panel |
| Card additions | Throttling badge, 10-minute CPU-temp history, top app line, disk as GB free |
| Panel open/close | Double-click tray icon or tray menu "Details"; closes on Esc, click elsewhere, or double-click again |
| Panel position | Beside the card (left if there is room, else right), top-aligned with the card |
| Alerts | Windows notifications plus an in-panel list; once per episode with hysteresis |
| CSV log | On by default, every 10 s, one file per day, 30-day retention, in `%LOCALAPPDATA%` (not OneDrive-synced Documents) |
| Memory target | < 150 MB working set |
| v1 items folded in | Sampling off the UI thread; re-check placement on display change; stop the old pin watchdog when the card is recreated |

## 3. Card changes

Width stays 250 DIPs. Height grows from 204 to 282 (304 when unlocked). Layout from the top, in DIPs:

| y | Row |
|---|---|
| 14 | Header: "System" left; right side shows the throttling pill (when active) then the clock |
| 38 | Three rings, unchanged |
| 104 | Ring labels |
| 128 | "CPU temp · 10 min" label left, "peak NN°" right (peak coloured by the temp rule) |
| 147 | History graph, 222 × 30 |
| 183 | "Top app" label left, "<name> · NN% CPU · N.N GB" right |
| 208 / 230 / 252 | Grid rows: GPU, C: free · Net ↓, Net ↑ · Battery, Skin |

- **Throttling pill:** text "Throttled NN%", red text `#F09595` on red at 18 % alpha, 11 px. "Below full speed" means a CPU limit reading under 99.5 (it would display as under 100 %). The pill shows after two consecutive 2 s reads below full speed and hides after one read at full speed. The throttling alert and the panel's CPU speed line use the same definition.
- **History graph:** 60 points, one every 10 s, y range 40–100 °C. Area fill at 15 % alpha and a 1.5 px line, both in the colour of the 10-minute peak's temp level. Dashed line (3/3) at the `tempAmber` threshold, amber at 50 % alpha. Starts empty at app start and fills from the right.
- **Top app:** the app (processes grouped by image name, so all `brave.exe` processes count as one) with the highest CPU over the last 3 s; memory is the group's private working set (Task Manager's "Memory" column). Shows "--" until two process samples exist.
- **Disk:** label "C: free", value in whole GB below 1000 GB ("36 GB"), otherwise TB with one decimal ("1.2 TB"); 1 GB = 1024³ bytes, matching Explorer. Colour still follows `diskAmber` / `diskRed` on % used.

## 4. Details panel

### Behaviour

- Opens on tray double-click or tray menu **Details** (new first item, shown bold as the default). A second double-click closes it.
- Normal top-level window: borderless, dark (`#1C1C1F` at 98 %, 1 px border white at 14 %, 10 px corners), 300 DIPs wide, height to content (about 480). Topmost while open, not in the taskbar or Alt+Tab, not click-through.
- Activates when opened so Esc works; closes on Esc, on losing activation (click elsewhere), or tray double-click.
- **Position:** top-aligned with the card. Left of the card with a 10 px gap when `card.Left - 310 >= workArea.Left`, otherwise right of the card. Clamped so it stays inside the work area of the card's screen.
- Refreshes once a second while open. Panel-only readers (top 5, network) run only while it is open.

### Contents, top to bottom

| Section | Content | Interaction |
|---|---|---|
| Header | "Details" left, "Esc to close" right (label colour) | |
| CPU speed | "Full speed" (teal) or "Limited to NN% · heat" / "· power saving" / "· limit" (red). Reason inferred: heat when CPU temp ≥ `tempRed`; power saving when on battery; otherwise "limit" | |
| Top apps | Top 5 groups by CPU: name left, "NN% · N.N GB" right | Read-only |
| Network | Per connected adapter: "Wi-Fi" with network name and signal %, or "Ethernet"; "IP" (first IPv4); "Gateway" | Click an IP to copy it; the text reads "Copied" for 1.5 s |
| COM ports | "COMn" left, device friendly name right; "None connected" when empty. A port that appeared within the last 10 s has a teal 18 % background and " · new" | Click a port name to copy "COMn" |
| Alerts | Today's alerts, newest first, up to 10: time left, title right in the alert's colour; "No alerts today" when empty | |
| Power mode | Segmented control: Efficiency / Balanced / Performance, current selection highlighted teal | Click to switch (section 6) |
| Battery | "Plugged in · NN%", "Charging · NN%", or "N h NN min left · NN%" | |
| Footer | "CSV log · on · every 10 s" (or "off") left; "Open log folder" and "Task Manager ↗" links right | Clicking the log label toggles logging |

## 5. Data sources (no admin)

| Reading | Source | Interval |
|---|---|---|
| CPU limit | PerformanceCounter `Processor Information` / `% Performance Limit` / `_Total` (100 = full speed; verified present, reads 100 at idle on this PC) | 2 s |
| Processes | `NtQuerySystemInformation(SystemProcessInformation)`, one call for all processes. CPU % per group = Δ(UserTime + KernelTime) ÷ (Δwall × logical CPUs) × 100; memory = WorkingSetPrivateSize. The Idle process (PID 0) is excluded | 3 s (card) |
| Wi-Fi name and signal | Native Wi-Fi API (`wlanapi.dll`: `WlanOpenHandle`, `WlanEnumInterfaces`, `WlanQueryInterface` current connection) | 5 s, panel open only |
| IP / gateway | `NetworkInterface.GetIPProperties()` for adapters that are Up and not Loopback/Tunnel | 5 s, panel open only |
| COM ports | Registry `HKLM\HARDWARE\DEVICEMAP\SERIALCOMM` (port list, sub-millisecond). When the list changes, friendly names come from WMI `Win32_PnPEntity WHERE Name LIKE '%(COM%'` on a background thread (about 0.9 s on this PC); until then the row shows the port with "…" | 2 s |
| Power mode | `powrprof.dll` `PowerGetActualOverlayScheme` (user's selection) | 5 s, panel open only |
| Battery time | `SystemInformation.PowerStatus.BatteryLifeRemaining` (seconds, −1 when unknown or on AC) | 10 s |

Overlay GUIDs: Efficiency `961cc777-2547-4f9d-8174-7d86181b8a7a`, Balanced `00000000-0000-0000-0000-000000000000`, Performance `ded574b5-45a0-4f42-8737-46345c09c238`.

## 6. Power-mode switch

- Sets the mode with `powrprof.dll` `PowerSetActiveOverlayScheme(Guid)`, the call behind the Windows Settings slider. It is undocumented, so it is proven first in a spike (section 10, step 1): the switch must change the slider in Settings → System → Power & battery, apply to the current power source, and survive a restart of the app.
- If the spike fails, the panel shows the current mode read-only with an "Open power settings ↗" link (`ms-settings:powersleep`) instead of the switch.
- After a switch the panel re-reads the mode once, so the highlight always shows what Windows actually applied.

## 7. Alerts

Each alert is a state machine: it fires once when its condition has held for the stated time, then stays quiet until its clear condition has held, then re-arms.

| Alert | Fires when | Re-arms when | Colour |
|---|---|---|---|
| CPU hot | CPU temp ≥ `alertCpuTemp` (90) for `alertCpuSeconds` (60) | below `alertCpuClear` (85) for `alertCpuSeconds` | red |
| Throttled | CPU limit < 100 for `alertThrottleSeconds` (120) | limit back at 100 | amber |
| Disk low | C: free < `alertDiskFreeGb` (10) | C: free > `alertDiskClearGb` (12) | amber |
| Battery low | on battery and < `alertBatteryPercent` (20) | on AC | amber |
| Battery hot | battery temp ≥ `alertBatteryTemp` (45) for `alertBatteryTempSeconds` (60) | below `alertBatteryTempClear` (42) for `alertBatteryTempSeconds` | red |
| COM port connected | a port appears in the list | that port disappears | teal |

- Delivery: `NotifyIcon.ShowBalloonTip` (Windows 11 shows it as a notification and honours Do Not Disturb), title as in the table, body with the value (e.g. "93 °C for over a minute", "COM5 · Silicon Labs CP210x").
- A missing reading (null) never fires or clears an alert; the timers simply pause.
- `alertsEnabled` (true) switches all of them; each also has its own on/off key, all true by default: `alertCpuHotOn`, `alertThrottledOn`, `alertDiskLowOn`, `alertBatteryLowOn`, `alertBatteryHotOn`, `alertComPortsOn`.
- Every fired alert is appended to `alerts.csv` (time, alert, value) and added to the panel list.

## 8. CSV log

- File: `%LOCALAPPDATA%\DesktopMonitor\logs\YYYY-MM-DD.csv` (local date), one per day, header row on creation.
- Row every `logIntervalSeconds` (10; 5–300), aggregated from the 1 s snapshots in that window: CPU %, GPU %, down/up KB/s are averages; CPU temp is the window's peak; everything else is the latest value.
- Columns: `time` (ISO-8601 local, seconds), `cpu_pct`, `cpu_limit_pct`, `ram_pct`, `gpu_pct`, `disk_free_gb`, `down_kbps`, `up_kbps`, `battery_pct`, `on_ac` (0/1), `cpu_temp_c`, `skin_temp_c`, `battery_temp_c`, `top_app`, `top_app_cpu_pct`, `power_mode`. Numbers use `.` as the decimal point; a missing reading is an empty field; `top_app` is quoted when it contains a comma or quote.
- Each row is appended and flushed immediately. A write failure is logged once and retried on the next row; it never stops the card.
- Retention: on start and after each midnight, daily files whose date is more than `logRetentionDays` (30; 1–365) days old are deleted. Only files named exactly `YYYY-MM-DD.csv` are ever deleted; anything else in the folder is left alone.
- `alerts.csv` lives in the same folder and is never deleted automatically.
- `logEnabled` (true) switches logging; the panel footer toggles it and saves the setting.

## 9. Architecture changes

- **Background sampling:** a dedicated background thread runs the sampler every second and posts each `Snapshot` to the UI thread with `Dispatcher.BeginInvoke`. The UI thread only draws. (v1 deferred minor: network enumeration took up to ~400 ms on the UI thread.)
- **Snapshot** gains: `CpuLimitPercent`, `DiskFreeGb`, `TopApp` (name, CPU %, private bytes), `BatteryMinutesLeft`. Panel-only data (top 5, network, power mode) travels in a separate `PanelData` produced only while the panel is open.
- **Display changes:** on `SystemEvents.DisplaySettingsChanged` the card re-runs the v1 off-screen check and moves to top-right if needed. (v1 deferred minor.)
- **Card recreation:** a closed `CardWindow` stops its `DesktopPin` watchdog. (v1 deferred minor.)

New units, each with one job:

| File | Responsibility |
|---|---|
| `src\History.cs` | Fixed-size ring buffer of temperature points plus peak |
| `src\Throttle.cs` | Debounce of the CPU limit reading (two reads below 100 to show, one at 100 to hide) and reason inference |
| `src\ProcessTable.cs` | Pure: groups raw process samples by name and computes CPU % and memory between two samples |
| `src\ProcessReader.cs` | `NtQuerySystemInformation` P/Invoke and struct walking, producing raw samples |
| `src\ComPorts.cs` | Pure diff of port lists (appeared, disappeared, "new" window) plus registry and WMI readers |
| `src\NetworkInfo.cs` | Wi-Fi (wlanapi) and IP/gateway readers |
| `src\PowerMode.cs` | Overlay read/set |
| `src\AlertEngine.cs` | Pure state machines for the six alerts (section 7) |
| `src\CsvLog.cs` | Pure row aggregation and formatting; file append, daily rotation and retention |
| `src\DetailsPanel.cs` | The panel window: layout, placement, interactions |
| `src\SamplerThread.cs` | Background loop owning `MetricsSampler` and the panel-only readers |

`Rules.cs` gains pure helpers for disk formatting ("36 GB", "1.2 TB"), battery time text, and panel placement.

## 10. Testing and verification

Riskiest first:

1. **Power-mode spike:** a console tool reads the overlay, sets Efficiency, confirms the Settings slider moved (user looks), sets it back. Decides switch vs read-only (section 6).
2. **Unit tests** (same framework-free runner): History (wrap, peak, empty), Throttle (debounce both ways, reasons), ProcessTable (grouping, CPU % maths, new and exited processes, PID reuse with a different create time, Idle excluded), ComPorts diff (appear, disappear, 10 s "new" window), AlertEngine (each alert: fire after duration, no repeat while held, re-arm after clear, null pauses), CsvLog (aggregation averages and peak, formatting, quoting, empty fields, retention selection by date), Rules helpers (disk text, battery text, placement left/right/clamp).
3. **Live checks:** top app matches Task Manager's Processes tab within a few %; Wi-Fi name, signal and IP match `netsh wlan show interfaces` and `ipconfig`; plugging a USB-serial board shows the port within 2 s and a notification; the CSV file gains a row every 10 s and opens cleanly in Excel.
4. **Interaction checks:** panel opens by double-click and from the menu, sits beside the card, closes on Esc and click-away; IP and COM copy; power switch (if kept) changes the Settings slider.
5. **Alerts end-to-end:** temporarily lower `alertCpuTemp` below the current temperature and confirm exactly one notification until it clears.
6. **Resources:** with the panel closed for 10 minutes: CPU < 1 %, working set < 150 MB; with the panel open for 2 minutes: CPU < 2 %.
7. **v1 open checks** still to run: sleep/resume (watch for network spikes and stale values) and start-with-Windows after a reboot.

## 11. Out of scope

- Per-core temperatures and fan speed (need admin drivers)
- History longer than 10 minutes on the card (the CSV log covers that)
- Ending processes from the panel
- Editing settings in a UI (still `settings.json`)
- Light theme, per-monitor DPI
- Charts of the CSV log inside the app

Still-deferred v1 minors (not in v2): ring "--" colour, spec example 79.9, NaN in settings, `FileSystemWatcher.Error` handling.
