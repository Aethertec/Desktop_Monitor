# Desktop Monitor v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add throttling, a 10-minute temperature history and the top app to the card, plus a details panel, alerts and a CSV log, without crowding the desktop.

**Architecture:** Readings move to a background `SamplerThread` that posts each `Snapshot` to the UI thread. New pure units (History, Throttle, ProcessTable, ComPortTracker, AlertEngine, LogWindow/CsvLog) hold all decisions and are unit-tested; thin Windows readers (ProcessReader, ComPortReader, NetworkInfo, PowerMode) feed them. The card gains three rows; a custom-drawn `DetailsPanel` opens beside it from the tray.

**Tech Stack:** C# 5, .NET Framework 4.8 (WPF, WinForms `NotifyIcon`, `PerformanceCounter`, `System.Management`), Win32 / ntdll / wlanapi / powrprof P/Invoke, PowerShell 5.1 build script, git.

**Spec:** `docs/superpowers/specs/2026-10-03-desktop-monitor-v2-design.md` (builds on `docs/superpowers/specs/2026-10-02-desktop-monitor-design.md`)

Every file in this plan was compiled with the target compiler, and the whole plan was dry-run task by task from the `v2-build` branch: each RED step failed for the stated reason, each GREEN step passed with the stated count, and the app built after every task. Copy the code verbatim.

## Global Constraints

- Project root `D:\Projects\Desktop_Monitor`, branch `v2-build` (already created from the fixed v1). All paths are relative to the root.
- Compiler: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, only through `build.ps1`. **C# 5 only:** no `$"..."`, no `?.`, no `nameof`, no `=>` members, no auto-property initialisers, no exception filters, no `using static`.
- No NuGet, no installs; only the Framework assemblies listed in `build.ps1` (v2 adds `System.Management.dll`).
- 64-bit (`/platform:x64`), runs as the normal user, never needs admin. UI built in code, no XAML.
- Non-ASCII characters in C# string and char literals are written as `\u` escapes (`\u00B7` ·, `\u00B0` °, `\u2026` …, `\u2193` ↓, `\u2191` ↑, `\u2197` ↗).
- v1 thresholds unchanged: usage amber ≥ 70 % red ≥ 85 %; temperature amber ≥ 80 °C red ≥ 85 °C; disk amber ≥ 90 % red ≥ 95 %; compared on the displayed rounded value.
- "Below full speed" = CPU limit reading under 99.5.
- Card 250 × 282 DIPs (304 unlocked). Panel 300 DIPs wide, beside the card, 10 px gap.
- Alerts (defaults): CPU hot ≥ 90 °C for 60 s, clears below 85 °C for 60 s; throttled 120 s; disk < 10 GB free, clears > 12 GB; battery < 20 % on battery, clears on AC; battery hot ≥ 45 °C for 60 s, clears below 42 °C for 60 s; COM port connected.
- CSV log in `%LOCALAPPDATA%\DesktopMonitor\logs\YYYY-MM-DD.csv`, every 10 s, 30-day retention; `alerts.csv` never deleted.
- Targets: CPU < 1 % with the panel closed, < 2 % while open; working set < 150 MB.
- Run scripts as `powershell -NoProfile -ExecutionPolicy Bypass -File <script> [args]` from the project root.
- Commit after every task on `v2-build`, each message ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Changing the power mode, restarting Explorer, sleeping or rebooting happens only after asking the user in chat.

### Refinements to the spec found while verifying this plan

- **Panel alerts:** the panel shows the newest **5** of today's alerts plus "+N earlier today (all in alerts.csv)". Ten rows pushed the panel past the 720 px work area (rendered: 616 px with 12 alerts, 2 COM ports and Wi-Fi).
- **Panel footer:** on two rows under a "CSV log" heading ("On · every 10 s · click to turn off", then "Open log folder" and "Task Manager ↗"). One row does not fit 272 px.
- **Network:** adapters whose description contains "virtual" (VMware, VirtualBox, Hyper-V/WSL) are hidden; this PC has two VMware adapters. Physical adapters without a gateway are kept (a cable straight to a board).
- **Top 5 apps** come from the same 3 s process read as the card's top app (2–5 ms measured), so they are not panel-only.
- **Alert timing with a missing reading:** a null reading restarts the timing in progress (it never fires or clears); the spec's "pause" is implemented as restart.
- **CSV rows while the file is locked** (open in Excel) are kept in memory, up to one day, and written with the next successful row.
- **Build:** tests and tools now compile all of `src\` and choose their entry point with `/main`, so `build.ps1` no longer keeps per-target file lists.

## Review Focus

1. **Today's CSV open in Excel.** Excel locks the file; rows written meanwhile must arrive once it is closed, in order. Pinned in Task 6 (`Writer_KeepsRowsWhileTheFileIsLocked`).
2. **Sleep in the middle of a log window.** After waking, the log must carry on at its cadence, not write a burst of catch-up rows. Pinned in Task 6 (`Window_KeepsItsCadence_AndSkipsAfterAGap`).
3. **A sensor dropping out mid-episode.** A missing reading must neither fire nor clear an alert. Pinned in Task 5 (`CpuHot_MissingReadingRestartsTheTiming`).
4. **Boards already plugged in at start-up, and replugging.** No notification for ports present at start; a replug alerts again. Pinned in Task 4 (`PortsPresentAtStart_AreNotNew`, `UnpluggedPort_Disappears_AndReplugAppearsAgain`) and Task 5 (`ComPort_PresentAtStartNeverAlerts`, `ComPort_WithName_AlertsOnce_AndAgainAfterReplug`).
5. **Processes exiting or a PID being reused between samples.** No absurd CPU % for the top app. Pinned in Task 3 (`ExitedProcess_IsGone`, `ReusedPid_DoesNotJump`).

## File Map

| File | Responsibility | Task |
|---|---|---|
| `build.ps1` | `/main`-based targets, `-Power`, `System.Management` reference | 1 |
| `src\PowerMode.cs`, `tools\PowerModeTool.cs`, `spike\POWER-RESULT.md` | Power-mode overlay read/set, spike tool, spike outcome | 1 |
| `src\History.cs`, `src\Throttle.cs` | Temperature history buffer; CPU-limit debounce and reason | 2 |
| `src\Rules.cs` | New formatting and placement helpers | 2, 3 |
| `src\ProcessTable.cs`, `src\ProcessReader.cs` | Per-app CPU/memory maths; NtQuerySystemInformation reader | 3 |
| `src\ComPorts.cs` | Port tracker (pure) and registry/WMI readers | 4 |
| `src\Settings.cs`, `src\Snapshot.cs`, `src\AlertEngine.cs` | Alert and log settings; new snapshot fields; the six alerts | 5 |
| `src\CsvLog.cs` | Row aggregation, formatting, retention, file writer | 6 |
| `src\NetworkInfo.cs` | Adapters, IPv4/gateway, Wi-Fi name and signal | 7 |
| `src\MetricsSampler.cs`, `src\SamplerThread.cs`, `tools\SamplerDump.cs` | New readings; background sampling; live dump | 8 |
| `src\Drawing.cs`, `src\CardView.cs`, `src\CardWindow.cs`, `src\DesktopPin.cs` | Shared palette/text; v2 card; display-change and bounds; watchdog stop | 9 |
| `src\DetailsPanel.cs`, `src\TrayIcon.cs`, `src\SafeTimer.cs`, `src\Program.cs` | Panel view and window; tray Details/double-click/notifications; `Guard`; wiring | 10 |
| `tests\*Tests.cs` | One test file per unit, registered in `tests\TestMain.cs` | 1–7 |
| `README.md`, spec status | Docs and final verification | 11 |

Registering a test class always means: in `tests\TestMain.cs`, insert the given line(s) directly before the line

```csharp
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
```

---

### Task 1: Build script and power-mode spike

The riskiest v2 piece first: the power-mode switch relies on undocumented `powrprof.dll` calls.

**Files:**
- Modify: `build.ps1` (full replacement)
- Create: `src\PowerMode.cs`, `tools\PowerModeTool.cs`, `tests\PowerModeTests.cs`, `spike\POWER-RESULT.md`
- Modify: `tests\TestMain.cs` (register `PowerModeTests`)

**Interfaces:**
- Consumes: nothing new.
- Produces: `public enum PowerModeKind { Unknown, Efficiency, Balanced, Performance }`; `internal static class PowerMode` with `PowerModeKind Get()`, `PowerModeKind GetEffective()`, `bool Set(PowerModeKind)`, `PowerModeKind FromGuid(Guid)`, `Guid ToGuid(PowerModeKind)`, `string Name(PowerModeKind)`; `build.ps1 -Power` → `bin\PowerModeTool.exe`; `spike\POWER-RESULT.md` with `PowerSwitchAvailable = true|false` (consumed by Task 10).

- [ ] **Step 1: Replace `build.ps1`**

```powershell
# Builds Desktop Monitor with the C# compiler that ships with Windows (.NET Framework 4.8, C# 5 only).
#   build.ps1          -> bin\DesktopMonitor.exe
#   build.ps1 -Test    -> bin\Tests.exe, then runs it (exit code 1 when any test fails)
#   build.ps1 -Spike   -> bin\PinSpike.exe (desktop-layer experiment)
#   build.ps1 -Dump    -> bin\SamplerDump.exe (prints live readings)
#   build.ps1 -Power   -> bin\PowerModeTool.exe (reads or sets the Windows power mode)
# Tests and tools compile all of src\ and pick their own entry point with /main.
param([switch]$Test, [switch]$Spike, [switch]$Dump, [switch]$Power)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fw 'WPF'
$csc = Join-Path $fw 'csc.exe'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

$common = @('/nologo', '/codepage:65001', '/platform:x64', '/optimize+')
$refs = @(
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $fw 'System.Xaml.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Web.Extensions.dll'),
    (Join-Path $fw 'System.Management.dll')
) | ForEach-Object { '/r:' + $_ }

function Compile([string]$target, [string]$out, [string[]]$sources, [string[]]$extra = @()) {
    $files = @($sources | ForEach-Object { Join-Path $root $_ })  # @() so a single file is not splatted char by char
    & $csc @common @refs @extra "/target:$target" "/out:$(Join-Path $bin $out)" @files
    if ($LASTEXITCODE -ne 0) { throw "Build of $out failed" }
    Write-Output "Built bin\$out"
}

$app = @('src\*.cs')

if ($Test) {
    Compile 'exe' 'Tests.exe' (@('tests\*.cs') + $app) @('/main:DesktopMonitor.Tests.TestMain')
    & (Join-Path $bin 'Tests.exe')
    exit $LASTEXITCODE
}
elseif ($Spike) {
    Compile 'winexe' 'PinSpike.exe' (@('spike\PinSpike.cs') + $app) @('/main:DesktopMonitor.PinSpike')
}
elseif ($Dump) {
    Compile 'exe' 'SamplerDump.exe' (@('tools\SamplerDump.cs') + $app) @('/main:DesktopMonitor.SamplerDump')
}
elseif ($Power) {
    Compile 'exe' 'PowerModeTool.exe' (@('tools\PowerModeTool.cs') + $app) @('/main:DesktopMonitor.PowerModeTool')
}
else {
    Compile 'winexe' 'DesktopMonitor.exe' $app @("/win32manifest:$(Join-Path $root 'app.manifest')")
}
```

- [ ] **Step 2: Confirm the existing tests still build and pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `Built bin\Tests.exe` then `131 passed, 0 failed`.

- [ ] **Step 3: Write the failing test `tests\PowerModeTests.cs` and register it**

```csharp
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
```

Register:

```csharp
            PowerModeTests.Run();
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0103: The name 'PowerMode' does not exist in the current context` (and `PowerModeKind`).

- [ ] **Step 5: Create `src\PowerMode.cs`**

```csharp
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
```

- [ ] **Step 6: Create `tools\PowerModeTool.cs`**

```csharp
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
```

- [ ] **Step 7: Run the tests and build the tool**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Power
.\bin\PowerModeTool.exe
```
Expected: `136 passed, 0 failed`; `Built bin\PowerModeTool.exe`; a line like `selected: Balanced  effective: Efficiency` (note the selected value, it is restored in Step 9).

- [ ] **Step 8: Spike: switch the mode (ask first)**

Ask the user: "May I switch Windows to the Efficiency power mode for a moment to test the switch? I'll put it back straight after." Only on a yes:
```powershell
.\bin\PowerModeTool.exe set Efficiency
```
Expected: `set Efficiency: ok` and `selected: Efficiency`. Ask the user to open Settings → System → Power & battery and confirm the Power mode slider shows "Best power efficiency".

- [ ] **Step 9: Restore the user's mode and check it sticks**

Run `.\bin\PowerModeTool.exe set <mode noted in Step 7>` then `.\bin\PowerModeTool.exe` again.
Expected: `selected:` is back to the noted mode; the user confirms the slider moved back.

- [ ] **Step 10: Record the outcome in `spike\POWER-RESULT.md`**

Fill in the actual observations:

```markdown
# Power-mode spike result (2026-10-03)

| Check | Result |
|---|---|
| Read selected / effective mode | Pass |
| Set Efficiency returns ok | Pass |
| Settings slider moved to Best power efficiency | Pass |
| Restored to the original mode, slider moved back | Pass |

Decision: PowerSwitchAvailable = true

Notes: <selected and effective modes seen, anything unexpected>
```

If any check failed, write `Decision: PowerSwitchAvailable = false`; Task 10 then shows the mode read-only with an "Open power settings ↗" link (already built into the panel).

- [ ] **Step 11: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add build.ps1 src/PowerMode.cs tools/PowerModeTool.cs tests/PowerModeTests.cs tests/TestMain.cs spike/POWER-RESULT.md && git commit -m "Add power-mode reader/switch and its spike; build tests and tools with /main

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: History, throttle debounce and formatting rules

**Files:**
- Create: `src\History.cs`, `src\Throttle.cs`, `tests\HistoryThrottleTests.cs`, `tests\RulesV2Tests.cs`
- Modify: `src\Rules.cs` (full replacement), `tests\TestMain.cs`

**Interfaces:**
- Consumes: `Rules.Display`, `Rules.FormatPercent`, `Box` (v1).
- Produces:
  - `public sealed class History` — `History(int capacity, TimeSpan every)`, `int Capacity`, `int Count`, `bool Offer(DateTime now, double? value)`, `double?[] ToArray()` (oldest first), `double? Peak`.
  - `public sealed class Throttle` — `bool Shown`, `void Feed(double? limitPercent)`, `static string Reason(double? cpuTempC, double tempRed, bool onAc)` → `"heat"`, `"power saving"` or `"limit"`.
  - `Rules`: `Box PanelPlacement(Box card, Box workArea, double panelW, double panelH, double gap)`, `bool IsBelowFullSpeed(double)`, `string FormatCpuSpeed(double? limit, string reason)`, `string FormatDiskFree(double? freeBytes)`, `string FormatMemory(long bytes)`, `string FormatBattery(double? percent, bool onAc, bool charging, int? minutesLeft)`, `string FormatDuration(double seconds)`.

- [ ] **Step 1: Write the failing tests**

`tests\HistoryThrottleTests.cs`:

```csharp
using System;

namespace DesktopMonitor.Tests
{
    internal static class HistoryThrottleTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 3, 14, 0, 0);

        public static void Run()
        {
            History_AddsOnePointPerInterval();
            History_KeepsTheLastCapacityPointsInOrder();
            History_PeakIgnoresGaps();
            Throttle_NeedsTwoReadsToShowAndOneToHide();
            Throttle_FullSpeedBoundary();
            Throttle_Reason();
        }

        private static void History_AddsOnePointPerInterval()
        {
            var h = new History(60, TimeSpan.FromSeconds(10));
            TestMain.True(h.Offer(T0, 70), "first point is added");
            TestMain.True(!h.Offer(T0.AddSeconds(5), 71), "5 s later is too early");
            TestMain.True(h.Offer(T0.AddSeconds(10), 72), "10 s later is added");
            TestMain.Equal(2, h.Count, "two points");
            double?[] a = h.ToArray();
            TestMain.Near(70, a[0], "oldest first");
            TestMain.Near(72, a[1], "newest last");
        }

        private static void History_KeepsTheLastCapacityPointsInOrder()
        {
            var h = new History(3, TimeSpan.FromSeconds(10));
            for (int i = 0; i < 5; i++) h.Offer(T0.AddSeconds(10 * i), 60 + i);
            double?[] a = h.ToArray();
            TestMain.Equal(3, a.Length, "capped at capacity");
            TestMain.Near(62, a[0], "oldest kept is the third point");
            TestMain.Near(64, a[2], "newest is the fifth point");
        }

        private static void History_PeakIgnoresGaps()
        {
            var h = new History(60, TimeSpan.FromSeconds(10));
            TestMain.Equal<double?>(null, h.Peak, "empty history has no peak");
            h.Offer(T0, 70);
            h.Offer(T0.AddSeconds(10), null);
            h.Offer(T0.AddSeconds(20), 80);
            TestMain.Near(80, h.Peak, "peak of 70, gap, 80");
            TestMain.Equal<double?>(null, h.ToArray()[1], "missing reading kept as a gap");
        }

        private static void Throttle_NeedsTwoReadsToShowAndOneToHide()
        {
            var t = new Throttle();
            t.Feed(72);
            TestMain.True(!t.Shown, "one read below full speed is not enough");
            t.Feed(72);
            TestMain.True(t.Shown, "two consecutive reads show the pill");
            t.Feed(null);
            TestMain.True(t.Shown, "a missing read changes nothing");
            t.Feed(100);
            TestMain.True(!t.Shown, "one read at full speed hides it");
            t.Feed(72);
            t.Feed(100);
            t.Feed(72);
            TestMain.True(!t.Shown, "reads that are not consecutive do not show it");
        }

        private static void Throttle_FullSpeedBoundary()
        {
            TestMain.True(!Rules.IsBelowFullSpeed(99.5), "99.5 displays as 100%, so it is full speed");
            TestMain.True(Rules.IsBelowFullSpeed(99.4), "99.4 displays as 99%, so it is below");
        }

        private static void Throttle_Reason()
        {
            TestMain.Equal("heat", Throttle.Reason(86, 85, true), "hot CPU on AC");
            TestMain.Equal("heat", Throttle.Reason(86, 85, false), "heat wins over battery");
            TestMain.Equal("power saving", Throttle.Reason(60, 85, false), "cool CPU on battery");
            TestMain.Equal("power saving", Throttle.Reason(null, 85, false), "no temperature, on battery");
            TestMain.Equal("limit", Throttle.Reason(60, 85, true), "cool CPU on AC");
        }
    }
}
```

`tests\RulesV2Tests.cs`:

```csharp
using System;

namespace DesktopMonitor.Tests
{
    internal static class RulesV2Tests
    {
        private const double Gb = 1024.0 * 1024 * 1024;

        public static void Run()
        {
            PanelPlacement_LeftRightAndClamp();
            FormatCpuSpeed_Cases();
            FormatDiskFree_GbAndTb();
            FormatMemory_MbAndGb();
            FormatBattery_Cases();
            FormatDuration_Cases();
        }

        private static void PanelPlacement_LeftRightAndClamp()
        {
            var wa = new Box(0, 0, 1366, 720);
            Box left = Rules.PanelPlacement(new Box(1104, 12, 250, 282), wa, 300, 480, 10);
            TestMain.Near(794, left.X, "card at top-right: panel 10 px to its left");
            TestMain.Near(12, left.Y, "top-aligned with the card");
            Box right = Rules.PanelPlacement(new Box(20, 12, 250, 282), wa, 300, 480, 10);
            TestMain.Near(280, right.X, "card at the left edge: panel to its right");
            Box low = Rules.PanelPlacement(new Box(1104, 400, 250, 282), wa, 300, 480, 10);
            TestMain.Near(240, low.Y, "card low on screen: panel pushed up to stay inside");
        }

        private static void FormatCpuSpeed_Cases()
        {
            TestMain.Equal("Full speed", Rules.FormatCpuSpeed(100, "heat"), "full speed");
            TestMain.Equal("Limited to 72% \u00B7 heat", Rules.FormatCpuSpeed(72, "heat"), "limited");
            TestMain.Equal("--", Rules.FormatCpuSpeed(null, "heat"), "missing");
        }

        private static void FormatDiskFree_GbAndTb()
        {
            TestMain.Equal("36 GB", Rules.FormatDiskFree(36 * Gb), "36 GB");
            TestMain.Equal("999 GB", Rules.FormatDiskFree(999 * Gb), "999 GB");
            TestMain.Equal("1.2 TB", Rules.FormatDiskFree(1229 * Gb), "TB with one decimal");
            TestMain.Equal("--", Rules.FormatDiskFree(null), "missing");
        }

        private static void FormatMemory_MbAndGb()
        {
            TestMain.Equal("820 MB", Rules.FormatMemory(820L * 1024 * 1024), "MB");
            TestMain.Equal("1.9 GB", Rules.FormatMemory((long)(1.9 * Gb)), "GB with one decimal");
        }

        private static void FormatBattery_Cases()
        {
            TestMain.Equal("Plugged in \u00B7 100%", Rules.FormatBattery(100, true, false, null), "full on AC");
            TestMain.Equal("Charging \u00B7 87%", Rules.FormatBattery(87, true, true, null), "charging");
            TestMain.Equal("1 h 45 min left \u00B7 62%", Rules.FormatBattery(62, false, false, 105), "hours and minutes");
            TestMain.Equal("45 min left \u00B7 30%", Rules.FormatBattery(30, false, false, 45), "minutes only");
            TestMain.Equal("On battery \u00B7 50%", Rules.FormatBattery(50, false, false, null), "time not known yet");
            TestMain.Equal("--", Rules.FormatBattery(null, true, false, null), "no battery reading");
        }

        private static void FormatDuration_Cases()
        {
            TestMain.Equal("1 min", Rules.FormatDuration(60), "60 s");
            TestMain.Equal("2 min", Rules.FormatDuration(120), "120 s");
            TestMain.Equal("90 s", Rules.FormatDuration(90), "90 s");
            TestMain.Equal("10 s", Rules.FormatDuration(10), "10 s");
        }

    }
}
```

Register:

```csharp
            HistoryThrottleTests.Run();
            RulesV2Tests.Run();
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0246: The type or namespace name 'History' could not be found`, the same for `Throttle`, and `'DesktopMonitor.Rules' does not contain a definition for 'IsBelowFullSpeed'`.

- [ ] **Step 3: Create `src\History.cs`**

```csharp
using System;

namespace DesktopMonitor
{
    // The card's temperature history: the last Capacity points, one per interval, oldest first.
    // A missing reading is stored as a gap (null) so the time axis stays honest.
    public sealed class History
    {
        private readonly double?[] _points;
        private readonly TimeSpan _every;
        private int _count, _next;
        private DateTime _lastAt = DateTime.MinValue;

        public History(int capacity, TimeSpan every)
        {
            _points = new double?[capacity];
            _every = every;
        }

        public int Capacity { get { return _points.Length; } }

        public int Count { get { return _count; } }

        // Adds the value when at least one interval has passed since the last point. Returns true when a point was added.
        public bool Offer(DateTime now, double? value)
        {
            if (_lastAt != DateTime.MinValue && now - _lastAt < _every) return false;
            _lastAt = now;
            _points[_next] = value;
            _next = (_next + 1) % _points.Length;
            if (_count < _points.Length) _count++;
            return true;
        }

        public double?[] ToArray()
        {
            var result = new double?[_count];
            int start = (_next - _count + _points.Length) % _points.Length;
            for (int i = 0; i < _count; i++) result[i] = _points[(start + i) % _points.Length];
            return result;
        }

        public double? Peak
        {
            get
            {
                double? peak = null;
                foreach (double? p in ToArray())
                    if (p.HasValue && (!peak.HasValue || p.Value > peak.Value)) peak = p;
                return peak;
            }
        }
    }
}
```

- [ ] **Step 4: Create `src\Throttle.cs`**

```csharp
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
```

- [ ] **Step 5: Replace `src\Rules.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopMonitor
{
    public enum Level { Unknown, Ok, Amber, Red }

    public struct Box
    {
        public double X, Y, W, H;

        public Box(double x, double y, double w, double h)
        {
            X = x; Y = y; W = w; H = h;
        }
    }

    // Pure rules shared by the card, tray icon and sampler: thresholds, unit conversion, formatting, placement.
    public static class Rules
    {
        // Rounds the way every number on the card is displayed, so colour and text always agree.
        public static double Display(double v)
        {
            return Math.Round(v, MidpointRounding.AwayFromZero);
        }

        public static Level Classify(double? value, double amber, double red)
        {
            if (!value.HasValue) return Level.Unknown;
            double v = Display(value.Value);
            if (v >= red) return Level.Red;
            if (v >= amber) return Level.Amber;
            return Level.Ok;
        }

        // ACPI thermal zones report Kelvin with 273.2 K as 0 °C ("High Precision Temperature" in tenths of Kelvin).
        // A zone without a sensor reads exactly 273.2 K, so anything at or below 0 °C or above 150 °C is not a reading.
        public static double? ThermalToCelsius(double raw, bool tenthsOfKelvin)
        {
            double c = (tenthsOfKelvin ? raw / 10.0 : raw) - 273.2;
            if (double.IsNaN(c) || c <= 0 || c > 150) return null;
            return c;
        }

        public static double ClampPercent(double v)
        {
            if (double.IsNaN(v) || v < 0) return 0;
            return v > 100 ? 100 : v;
        }

        public static double? Percent(double part, double whole)
        {
            if (whole <= 0 || double.IsNaN(part) || part < 0) return null;
            return ClampPercent(part / whole * 100.0);
        }

        // Bytes per second from per-adapter cumulative counters. Only adapters present in both samples count, so an adapter
        // that just (re)appeared never shows its lifetime total as one second of traffic; a counter that went backwards adds 0.
        // Null when there is no baseline, no time passed, or no adapter is common to both samples.
        public static double? AdapterRate(IDictionary<string, long> previous, IDictionary<string, long> current, double seconds)
        {
            if (previous == null || current == null || seconds <= 0) return null;
            long sum = 0;
            bool common = false;
            foreach (KeyValuePair<string, long> adapter in current)
            {
                long before;
                if (!previous.TryGetValue(adapter.Key, out before)) continue;
                common = true;
                if (adapter.Value >= before) sum += adapter.Value - before;
            }
            return common ? sum / seconds : (double?)null;
        }

        // PowerStatus.BatteryLifePercent is 0..1, or 2.55 when the charge is unknown.
        public static double? BatteryPercent(float lifePercent)
        {
            if (float.IsNaN(lifePercent) || lifePercent < 0 || lifePercent > 1) return null;
            return Display(lifePercent * 100.0);
        }

        public static double TempRingFraction(double? celsius, double min, double max)
        {
            if (!celsius.HasValue || max <= min) return 0;
            double f = (celsius.Value - min) / (max - min);
            return f < 0 ? 0 : (f > 1 ? 1 : f);
        }

        public static string FormatNumber(double? v)
        {
            return v.HasValue ? Display(v.Value).ToString(CultureInfo.InvariantCulture) : "--";
        }

        public static string FormatPercent(double? v)
        {
            return v.HasValue ? FormatNumber(v) + "%" : "--";
        }

        public static string FormatTemp(double? celsius, bool withUnit)
        {
            return celsius.HasValue ? FormatNumber(celsius) + (withUnit ? "\u00B0C" : "\u00B0") : "--";
        }

        // KB/s (1 KB = 1024 bytes) below 1000 KB/s, otherwise MB/s with one decimal.
        public static string FormatSpeed(double? bytesPerSecond)
        {
            if (!bytesPerSecond.HasValue) return "--";
            double kb = bytesPerSecond.Value / 1024.0;
            if (Display(kb) < 1000) return Display(kb).ToString(CultureInfo.InvariantCulture) + " KB/s";
            return (kb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s";
        }

        public static string Tooltip(double? cpuPercent, double? cpuTempC)
        {
            return "CPU " + FormatPercent(cpuPercent) + " \u00B7 " + FormatTemp(cpuTempC, true);
        }

        // True when at least half of the card's area lies inside a single work area.
        public static bool IsMostlyOnScreen(Box card, IList<Box> workAreas)
        {
            double area = card.W * card.H;
            if (area <= 0) return false;
            foreach (Box wa in workAreas)
            {
                double w = Math.Min(card.X + card.W, wa.X + wa.W) - Math.Max(card.X, wa.X);
                double h = Math.Min(card.Y + card.H, wa.Y + wa.H) - Math.Max(card.Y, wa.Y);
                if (w > 0 && h > 0 && w * h >= area / 2) return true;
            }
            return false;
        }

        public static Box DefaultPlacement(Box workArea, double cardW, double cardH, double margin)
        {
            return new Box(workArea.X + workArea.W - cardW - margin, workArea.Y + margin, cardW, cardH);
        }

        // Details panel beside the card: left of it when there is room, otherwise right of it, top-aligned with the card,
        // and always kept inside the work area.
        public static Box PanelPlacement(Box card, Box workArea, double panelW, double panelH, double gap)
        {
            double x = card.X - gap - panelW >= workArea.X ? card.X - gap - panelW : card.X + card.W + gap;
            x = Math.Max(workArea.X, Math.Min(x, workArea.X + workArea.W - panelW));
            double y = Math.Max(workArea.Y, Math.Min(card.Y, workArea.Y + workArea.H - panelH));
            return new Box(x, y, panelW, panelH);
        }

        // "Below full speed" for the CPU limit: under 99.5, i.e. it would display as under 100 %.
        public static bool IsBelowFullSpeed(double limitPercent)
        {
            return limitPercent < 99.5;
        }

        public static string FormatCpuSpeed(double? limitPercent, string reason)
        {
            if (!limitPercent.HasValue) return "--";
            return IsBelowFullSpeed(limitPercent.Value) ? "Limited to " + FormatPercent(limitPercent) + " \u00B7 " + reason : "Full speed";
        }

        // Whole GB below 1000 GB, otherwise TB with one decimal; 1 GB = 1024^3 bytes, as Explorer shows it.
        public static string FormatDiskFree(double? freeBytes)
        {
            if (!freeBytes.HasValue) return "--";
            double gb = freeBytes.Value / (1024.0 * 1024 * 1024);
            if (Display(gb) < 1000) return Display(gb).ToString(CultureInfo.InvariantCulture) + " GB";
            return (gb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " TB";
        }

        // Whole MB below 1000 MB, otherwise GB with one decimal.
        public static string FormatMemory(long bytes)
        {
            double mb = bytes / (1024.0 * 1024);
            if (Display(mb) < 1000) return Display(mb).ToString(CultureInfo.InvariantCulture) + " MB";
            return (mb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        public static string FormatBattery(double? percent, bool onAc, bool charging, int? minutesLeft)
        {
            if (!percent.HasValue) return "--";
            string pct = FormatPercent(percent);
            if (onAc) return (charging ? "Charging \u00B7 " : "Plugged in \u00B7 ") + pct;
            if (!minutesLeft.HasValue || minutesLeft.Value <= 0) return "On battery \u00B7 " + pct;
            int h = minutesLeft.Value / 60, m = minutesLeft.Value % 60;
            return (h > 0 ? h + " h " + m + " min left" : m + " min left") + " \u00B7 " + pct;
        }

        // 60 -> "1 min", 120 -> "2 min", 90 -> "90 s".
        public static string FormatDuration(double seconds)
        {
            int s = (int)Display(seconds);
            return s >= 60 && s % 60 == 0 ? (s / 60) + " min" : s + " s";
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `183 passed, 0 failed`.

- [ ] **Step 7: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/History.cs src/Throttle.cs src/Rules.cs tests/HistoryThrottleTests.cs tests/RulesV2Tests.cs tests/TestMain.cs && git commit -m "Add temperature history, throttle debounce and v2 formatting rules

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Per-app CPU and memory

**Files:**
- Create: `src\ProcessTable.cs`, `src\ProcessReader.cs`, `tests\ProcessTableTests.cs`
- Modify: `src\Rules.cs` (add `FormatTopApp`), `tests\TestMain.cs`

**Interfaces:**
- Consumes: `Rules.ClampPercent`, `Rules.FormatPercent`, `Rules.FormatMemory` (Task 2).
- Produces:
  - `public struct ProcessSample` — fields `int Pid; long CreateTime; string Name; long CpuTime; long PrivateBytes;`, constructor in that order. `CpuTime` is user + kernel in 100 ns units.
  - `public sealed class AppUsage` — fields `string Name; double CpuPercent; long PrivateBytes;`.
  - `public static class ProcessTable` — `List<AppUsage> Compare(IList<ProcessSample> before, IList<ProcessSample> after, double elapsedSeconds, int logicalCpus)` (highest CPU first), `string DisplayName(string imageName)`.
  - `internal sealed class ProcessReader : IDisposable` — `List<ProcessSample> Read()`.
  - `Rules.FormatTopApp(AppUsage)` → `"brave · 34% CPU · 1.9 GB"` or `"--"`.

- [ ] **Step 1: Write the failing tests `tests\ProcessTableTests.cs` and register them**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DesktopMonitor.Tests
{
    internal static class ProcessTableTests
    {
        private const long Second = 10000000; // 100 ns units
        private const long Mb = 1024 * 1024;

        public static void Run()
        {
            CpuPercent_IsShareOfAllLogicalCpus();
            Groups_ProcessesWithTheSameName();
            NewProcess_CountsMemoryButNoCpu();
            ExitedProcess_IsGone();
            ReusedPid_DoesNotJump();
            Idle_IsLeftOut();
            Sorted_ByCpuThenMemory();
            ZeroElapsed_GivesZeroNotInfinity();
            Clamped_At100();
            FormatTopApp_Text();
            Reader_FindsThisProcess();
        }

        private static ProcessSample P(int pid, string name, long cpu, long mem, long created = 1)
        {
            return new ProcessSample(pid, created, name, cpu, mem);
        }

        private static List<ProcessSample> L(params ProcessSample[] items)
        {
            return new List<ProcessSample>(items);
        }

        private static void CpuPercent_IsShareOfAllLogicalCpus()
        {
            var apps = ProcessTable.Compare(L(P(10, "brave.exe", 0, 100 * Mb)), L(P(10, "brave.exe", 2 * Second, 100 * Mb)), 1.0, 8);
            TestMain.Near(25, apps[0].CpuPercent, "2 s of CPU in 1 s on 8 logical CPUs is 25 %");
            TestMain.Equal("brave", apps[0].Name, ".exe is dropped");
        }

        private static void Groups_ProcessesWithTheSameName()
        {
            var before = L(P(10, "brave.exe", 0, 100 * Mb), P(11, "Brave.exe", 0, 50 * Mb));
            var after = L(P(10, "brave.exe", Second, 100 * Mb), P(11, "Brave.exe", Second, 50 * Mb));
            var apps = ProcessTable.Compare(before, after, 1.0, 8);
            TestMain.Equal(1, apps.Count, "both processes are one app, whatever the case");
            TestMain.Near(25, apps[0].CpuPercent, "CPU is summed");
            TestMain.Equal(150 * Mb, apps[0].PrivateBytes, "memory is summed");
        }

        private static void NewProcess_CountsMemoryButNoCpu()
        {
            var apps = ProcessTable.Compare(L(), L(P(20, "Code.exe", 5 * Second, 800 * Mb)), 1.0, 8);
            TestMain.Near(0, apps[0].CpuPercent, "no CPU without an earlier sample");
            TestMain.Equal(800 * Mb, apps[0].PrivateBytes, "memory is known straight away");
        }

        private static void ExitedProcess_IsGone()
        {
            var apps = ProcessTable.Compare(L(P(30, "old.exe", 0, Mb)), L(), 1.0, 8);
            TestMain.Equal(0, apps.Count, "a process that exited is not listed");
        }

        private static void ReusedPid_DoesNotJump()
        {
            var apps = ProcessTable.Compare(L(P(40, "a.exe", 0, Mb, 1)), L(P(40, "b.exe", 900 * Second, Mb, 2)), 1.0, 8);
            TestMain.Near(0, apps[0].CpuPercent, "same PID, new create time: a different process");
        }

        private static void Idle_IsLeftOut()
        {
            var apps = ProcessTable.Compare(L(P(0, "Idle", 0, 0)), L(P(0, "Idle", 7 * Second, 0)), 1.0, 8);
            TestMain.Equal(0, apps.Count, "the Idle process is never an app");
        }

        private static void Sorted_ByCpuThenMemory()
        {
            var before = L(P(1, "a.exe", 0, 10 * Mb), P(2, "b.exe", 0, 10 * Mb), P(3, "c.exe", 0, 90 * Mb));
            var after = L(P(1, "a.exe", Second / 10, 10 * Mb), P(2, "b.exe", Second, 10 * Mb), P(3, "c.exe", Second / 10, 90 * Mb));
            var apps = ProcessTable.Compare(before, after, 1.0, 8);
            TestMain.Equal("b", apps[0].Name, "highest CPU first");
            TestMain.Equal("c", apps[1].Name, "equal CPU: more memory first");
            TestMain.Equal("a", apps[2].Name, "then the rest");
        }

        private static void ZeroElapsed_GivesZeroNotInfinity()
        {
            var apps = ProcessTable.Compare(L(P(1, "a.exe", 0, Mb)), L(P(1, "a.exe", Second, Mb)), 0, 8);
            TestMain.Near(0, apps[0].CpuPercent, "no time passed");
        }

        private static void Clamped_At100()
        {
            var apps = ProcessTable.Compare(L(P(1, "a.exe", 0, Mb)), L(P(1, "a.exe", 100 * Second, Mb)), 1.0, 8);
            TestMain.Near(100, apps[0].CpuPercent, "never more than 100 %");
        }

        private const double Gb = 1024.0 * 1024 * 1024;

        private static void FormatTopApp_Text()
        {
            TestMain.Equal("brave \u00B7 34% CPU \u00B7 1.9 GB", Rules.FormatTopApp(new AppUsage { Name = "brave", CpuPercent = 34.2, PrivateBytes = (long)(1.9 * Gb) }), "top app line");
            TestMain.Equal("--", Rules.FormatTopApp(null), "no app yet");
        }

        // Live: the NtQuerySystemInformation reader sees this test process with the right name and CPU time.
        private static void Reader_FindsThisProcess()
        {
            Process me = Process.GetCurrentProcess();
            using (var reader = new ProcessReader())
            {
                List<ProcessSample> all = reader.Read();
                long expected = me.TotalProcessorTime.Ticks;
                ProcessSample found = all.Find(x => x.Pid == me.Id);
                TestMain.Equal(me.ProcessName, ProcessTable.DisplayName(found.Name), "this process is listed under its own name");
                TestMain.True(Math.Abs(found.CpuTime - expected) < 5000000, "its CPU time matches Process.TotalProcessorTime within 0.5 s");
                TestMain.True(found.PrivateBytes > 0, "its private memory is known");
                TestMain.True(all.Count > 20, "a normal desktop has well over 20 processes (" + all.Count + ")");
            }
        }
    }
}
```

Register:

```csharp
            ProcessTableTests.Run();
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0246: The type or namespace name 'ProcessSample' could not be found`.

- [ ] **Step 3: Create `src\ProcessTable.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace DesktopMonitor
{
    // One process as read from NtQuerySystemInformation. CpuTime is user + kernel time in 100 ns units.
    public struct ProcessSample
    {
        public int Pid;
        public long CreateTime;
        public string Name;
        public long CpuTime;
        public long PrivateBytes;

        public ProcessSample(int pid, long createTime, string name, long cpuTime, long privateBytes)
        {
            Pid = pid; CreateTime = createTime; Name = name; CpuTime = cpuTime; PrivateBytes = privateBytes;
        }
    }

    // One app: all processes with the same image name, e.g. every brave.exe.
    public sealed class AppUsage
    {
        public string Name;
        public double CpuPercent;
        public long PrivateBytes;
    }

    public static class ProcessTable
    {
        // Per-app CPU % between two samples, Task Manager style (share of all logical CPUs), highest first.
        // A process only contributes CPU when the same PID with the same create time is in both samples,
        // so exited processes and reused PIDs never produce a jump. The Idle process (PID 0) is left out.
        public static List<AppUsage> Compare(IList<ProcessSample> before, IList<ProcessSample> after, double elapsedSeconds, int logicalCpus)
        {
            var previous = new Dictionary<string, ProcessSample>();
            foreach (ProcessSample p in before) previous[Key(p)] = p;
            var apps = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
            double scale = elapsedSeconds > 0 && logicalCpus > 0 ? 100.0 / (elapsedSeconds * 1e7 * logicalCpus) : 0;
            foreach (ProcessSample p in after)
            {
                if (p.Pid == 0) continue;
                string name = DisplayName(p.Name);
                AppUsage app;
                if (!apps.TryGetValue(name, out app))
                {
                    app = new AppUsage { Name = name };
                    apps[name] = app;
                }
                app.PrivateBytes += p.PrivateBytes;
                ProcessSample q;
                if (previous.TryGetValue(Key(p), out q) && p.CpuTime >= q.CpuTime) app.CpuPercent += (p.CpuTime - q.CpuTime) * scale;
            }
            var list = new List<AppUsage>(apps.Values);
            foreach (AppUsage a in list) a.CpuPercent = Rules.ClampPercent(a.CpuPercent);
            list.Sort(delegate(AppUsage x, AppUsage y)
            {
                int byCpu = y.CpuPercent.CompareTo(x.CpuPercent);
                return byCpu != 0 ? byCpu : y.PrivateBytes.CompareTo(x.PrivateBytes);
            });
            return list;
        }

        // "brave.exe" shows as "brave"; names without .exe (System, Registry) stay as they are.
        public static string DisplayName(string imageName)
        {
            if (string.IsNullOrEmpty(imageName)) return "System";
            return imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName.Substring(0, imageName.Length - 4) : imageName;
        }

        private static string Key(ProcessSample p)
        {
            return p.Pid + ":" + p.CreateTime;
        }
    }
}
```

- [ ] **Step 4: Create `src\ProcessReader.cs`**

The offsets are the 64-bit `SYSTEM_PROCESS_INFORMATION` layout; the live test in Step 1 checks them against `System.Diagnostics.Process`.

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopMonitor
{
    // Every process in one call to NtQuerySystemInformation(SystemProcessInformation), as Task Manager does:
    // a few milliseconds instead of opening ~400 process handles. 64-bit layout of SYSTEM_PROCESS_INFORMATION.
    internal sealed class ProcessReader : IDisposable
    {
        private const int SystemProcessInformation = 5;
        private const uint StatusInfoLengthMismatch = 0xC0000004;
        private const int OffNext = 0x00, OffPrivateWs = 0x08, OffCreateTime = 0x20, OffUserTime = 0x28, OffKernelTime = 0x30;
        private const int OffNameLength = 0x38, OffNameBuffer = 0x40, OffPid = 0x50;

        [DllImport("ntdll.dll")]
        private static extern uint NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        private IntPtr _buffer;
        private int _size = 1 << 20;

        public List<ProcessSample> Read()
        {
            if (_buffer == IntPtr.Zero) _buffer = Marshal.AllocHGlobal(_size);
            int needed;
            uint status;
            while ((status = NtQuerySystemInformation(SystemProcessInformation, _buffer, _size, out needed)) == StatusInfoLengthMismatch)
            {
                Marshal.FreeHGlobal(_buffer);
                _size = Math.Max(_size * 2, needed + 65536);
                _buffer = Marshal.AllocHGlobal(_size);
            }
            if (status != 0) throw new Win32Exception("NtQuerySystemInformation failed with status 0x" + status.ToString("X8"));

            var list = new List<ProcessSample>(512);
            long offset = 0;
            while (true)
            {
                var p = new IntPtr(_buffer.ToInt64() + offset);
                int pid = (int)Marshal.ReadIntPtr(p, OffPid).ToInt64();
                int nameBytes = Marshal.ReadInt16(p, OffNameLength) & 0xFFFF;
                IntPtr name = Marshal.ReadIntPtr(p, OffNameBuffer);
                list.Add(new ProcessSample(
                    pid,
                    Marshal.ReadInt64(p, OffCreateTime),
                    name == IntPtr.Zero ? (pid == 0 ? "Idle" : "System") : Marshal.PtrToStringUni(name, nameBytes / 2),
                    Marshal.ReadInt64(p, OffUserTime) + Marshal.ReadInt64(p, OffKernelTime),
                    Marshal.ReadInt64(p, OffPrivateWs)));
                int next = Marshal.ReadInt32(p, OffNext);
                if (next == 0) break;
                offset += next;
            }
            return list;
        }

        public void Dispose()
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}
```

- [ ] **Step 5: Add `FormatTopApp` to `src\Rules.cs`**

Insert directly after the `FormatMemory` method:

```csharp
        public static string FormatTopApp(AppUsage app)
        {
            return app == null ? "--" : app.Name + " \u00B7 " + FormatPercent(app.CpuPercent) + " CPU \u00B7 " + FormatMemory(app.PrivateBytes);
        }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `204 passed, 0 failed` (includes the live `Reader_FindsThisProcess`).

- [ ] **Step 7: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/ProcessTable.cs src/ProcessReader.cs src/Rules.cs tests/ProcessTableTests.cs tests/TestMain.cs && git commit -m "Add per-app CPU and memory from NtQuerySystemInformation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: COM ports

**Files:**
- Create: `src\ComPorts.cs`, `tests\ComPortsTests.cs`
- Modify: `tests\TestMain.cs`

**Interfaces:**
- Consumes: nothing new (`System.Management` reference from Task 1).
- Produces:
  - `public sealed class ComPortInfo` — fields `string Port; string Name; DateTime FirstSeen; bool AppearedLive; bool IsNew;`.
  - `public sealed class ComPortTracker` — `List<string> Update(IEnumerable<string> ports, DateTime now)` (ports that appeared; none on the first poll), `void SetName(string port, string name)`, `List<ComPortInfo> Current(DateTime now)` (natural order, `IsNew` for 10 s), `static int ComparePorts(string a, string b)`.
  - `internal static class ComPortReader` — `List<string> ReadPorts()`, `Dictionary<string, string> ReadFriendlyNames()`.

- [ ] **Step 1: Write the failing tests `tests\ComPortsTests.cs` and register them**

```csharp
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
```

Register:

```csharp
            ComPortsTests.Run();
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0246: The type or namespace name 'ComPortTracker' could not be found` (and `ComPortInfo`, `ComPortReader`).

- [ ] **Step 3: Create `src\ComPorts.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DesktopMonitor
{
    // One serial port as shown in the panel. Name is null until the WMI lookup has finished.
    public sealed class ComPortInfo
    {
        public string Port;
        public string Name;
        public DateTime FirstSeen;
        public bool AppearedLive; // false for ports already present when the app started (they never alert)
        public bool IsNew;        // appeared live less than 10 s ago
    }

    // Tracks the port list across polls. Pure; tested in ComPortsTests.
    public sealed class ComPortTracker
    {
        private static readonly TimeSpan NewFor = TimeSpan.FromSeconds(10);
        private readonly Dictionary<string, ComPortInfo> _ports = new Dictionary<string, ComPortInfo>(StringComparer.OrdinalIgnoreCase);
        private bool _primed;

        // Feeds one poll. Returns the ports that appeared since the previous poll (none on the very first poll).
        public List<string> Update(IEnumerable<string> ports, DateTime now)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appeared = new List<string>();
            foreach (string port in ports)
            {
                seen.Add(port);
                if (_ports.ContainsKey(port)) continue;
                _ports[port] = new ComPortInfo { Port = port, FirstSeen = now, AppearedLive = _primed };
                if (_primed) appeared.Add(port);
            }
            foreach (string gone in new List<string>(_ports.Keys))
                if (!seen.Contains(gone)) _ports.Remove(gone);
            _primed = true;
            return appeared;
        }

        public void SetName(string port, string name)
        {
            ComPortInfo info;
            if (_ports.TryGetValue(port, out info)) info.Name = name;
        }

        // A copy of the current ports in natural order (COM3 before COM10), with IsNew worked out for `now`.
        public List<ComPortInfo> Current(DateTime now)
        {
            var list = new List<ComPortInfo>();
            foreach (ComPortInfo p in _ports.Values)
                list.Add(new ComPortInfo { Port = p.Port, Name = p.Name, FirstSeen = p.FirstSeen, AppearedLive = p.AppearedLive, IsNew = p.AppearedLive && now - p.FirstSeen < NewFor });
            list.Sort(delegate(ComPortInfo a, ComPortInfo b) { return ComparePorts(a.Port, b.Port); });
            return list;
        }

        public static int ComparePorts(string a, string b)
        {
            int na = PortNumber(a), nb = PortNumber(b);
            if (na >= 0 && nb >= 0 && na != nb) return na.CompareTo(nb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int PortNumber(string port)
        {
            int n;
            return port != null && port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(port.Substring(3), out n) ? n : -1;
        }
    }

    // Windows side: the port list from the registry (sub-millisecond) and friendly names from WMI (about 1 s).
    internal static class ComPortReader
    {
        private static readonly Regex PortInName = new Regex(@"^(.*?)\s*\((COM\d+)\)\s*$", RegexOptions.IgnoreCase);

        public static List<string> ReadPorts()
        {
            var ports = new List<string>();
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
            {
                if (key == null) return ports; // no serial ports registered at all
                foreach (string valueName in key.GetValueNames())
                {
                    var port = key.GetValue(valueName) as string;
                    if (!string.IsNullOrEmpty(port)) ports.Add(port.Trim());
                }
            }
            return ports;
        }

        // Port -> device name, e.g. "COM5" -> "Silicon Labs CP210x USB to UART Bridge".
        public static Dictionary<string, string> ReadFriendlyNames()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementBaseObject o in results)
                {
                    var name = o["Name"] as string;
                    Match m = name == null ? Match.Empty : PortInName.Match(name);
                    if (m.Success) names[m.Groups[2].Value] = m.Groups[1].Value;
                    o.Dispose();
                }
            }
            return names;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `216 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/ComPorts.cs tests/ComPortsTests.cs tests/TestMain.cs && git commit -m "Add COM port tracking and readers

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Alert settings, new snapshot fields and the alert engine

**Files:**
- Modify: `src\Settings.cs` (full replacement), `src\Snapshot.cs` (full replacement), `tests\TestMain.cs`
- Create: `src\AlertEngine.cs`, `tests\SettingsV2Tests.cs`, `tests\AlertEngineTests.cs`

**Interfaces:**
- Consumes: `Rules.*` (Tasks 2–3), `AppUsage` (Task 3), `ComPortInfo` (Task 4), `Level` (v1).
- Produces:
  - `AppSettings` new fields: `bool LogEnabled; double LogIntervalSeconds, LogRetentionDays; bool AlertsEnabled, AlertCpuHotOn, AlertThrottledOn, AlertDiskLowOn, AlertBatteryLowOn, AlertBatteryHotOn, AlertComPortsOn; double AlertCpuTemp, AlertCpuClear, AlertCpuSeconds, AlertThrottleSeconds, AlertDiskFreeGb, AlertDiskClearGb, AlertBatteryPercent, AlertBatteryTemp, AlertBatteryTempClear, AlertBatteryTempSeconds;` (JSON keys are the camelCase names).
  - `Snapshot` new fields: `double? DiskFreeBytes; bool Charging; int? BatteryMinutesLeft; double? CpuLimitPercent; bool Throttled; List<AppUsage> TopApps; List<ComPortInfo> ComPorts;`.
  - `public sealed class Alert` — `DateTime Time; string Kind; string Title; string Body; Level Severity;` (`Kind` is one of `cpu_hot`, `throttled`, `disk_low`, `battery_low`, `battery_hot`, `com_port`).
  - `public sealed class Episode` — `bool Step(DateTime now, bool? trigger, bool? clear, TimeSpan holdFor, TimeSpan clearFor)`.
  - `public sealed class AlertEngine` — `List<Alert> Evaluate(Snapshot s, AppSettings st)`, `List<Alert> Recent(DateTime now)` (today, newest first, at most 10).

- [ ] **Step 1: Write the failing tests and register them**

`tests\SettingsV2Tests.cs`:

```csharp
using System;

namespace DesktopMonitor.Tests
{
    internal static class SettingsV2Tests
    {
        public static void Run()
        {
            SettingsV2_DefaultsAndParsing();
        }

        private static void SettingsV2_DefaultsAndParsing()
        {
            AppSettings d = SettingsStore.Parse("{}");
            TestMain.True(d.LogEnabled && d.AlertsEnabled && d.AlertComPortsOn, "log and alerts on by default");
            TestMain.Near(10, d.LogIntervalSeconds, "log every 10 s");
            TestMain.Near(30, d.LogRetentionDays, "keep 30 days");
            TestMain.Near(90, d.AlertCpuTemp, "CPU hot at 90");
            TestMain.Near(85, d.AlertCpuClear, "clears below 85");
            TestMain.Near(120, d.AlertThrottleSeconds, "throttled after 2 min");
            TestMain.Near(12, d.AlertDiskClearGb, "disk clears above 12 GB");
            AppSettings s = SettingsStore.Parse(@"{""logEnabled"": false, ""alertCpuHotOn"": ""yes"", ""logIntervalSeconds"": 1, ""logRetentionDays"": 0, ""alertCpuTemp"": 88}");
            TestMain.True(!s.LogEnabled, "false is read");
            TestMain.True(s.AlertCpuHotOn, "\"yes\" is not a JSON bool: default kept");
            TestMain.Near(5, s.LogIntervalSeconds, "interval clamped to at least 5 s");
            TestMain.Near(1, s.LogRetentionDays, "retention clamped to at least 1 day");
            TestMain.Near(88, s.AlertCpuTemp, "number read");
            AppSettings back = SettingsStore.Parse(SettingsStore.ToJson(s));
            TestMain.True(!back.LogEnabled && Math.Abs(back.AlertCpuTemp - 88) < 1e-9, "new keys survive a save and reload");
        }
    }
}
```

`tests\AlertEngineTests.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace DesktopMonitor.Tests
{
    internal static class AlertEngineTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 3, 14, 0, 0);
        private const double Gb = 1024.0 * 1024 * 1024;

        public static void Run()
        {
            CpuHot_FiresAfterTheHoldTime_Once();
            CpuHot_ReArmsOnlyBelowTheClearTemperature();
            CpuHot_MissingReadingRestartsTheTiming();
            Throttled_FiresAfterTwoMinutes();
            DiskLow_FiresAtOnce_AndReArmsAboveTheClearLevel();
            BatteryLow_OnlyOnBattery_ReArmsWhenPluggedIn();
            BatteryHot_FiresAfterTheHoldTime();
            ComPort_PresentAtStartNeverAlerts();
            ComPort_WithName_AlertsOnce_AndAgainAfterReplug();
            ComPort_WithoutName_WaitsThreeSeconds();
            Switches_TurnAlertsOff();
            Recent_IsTodayNewestFirstAtMostTen();
        }

        // A calm snapshot: everything well inside its limits.
        private static Snapshot Calm(DateTime t)
        {
            return new Snapshot
            {
                Time = t, CpuTempC = 60, BatteryTempC = 35, CpuLimitPercent = 100,
                DiskFreeBytes = 50 * Gb, BatteryPercent = 80, OnAc = true
            };
        }

        private static List<Alert> Run(AlertEngine e, AppSettings st, Snapshot s)
        {
            return e.Evaluate(s, st);
        }

        // Feeds one snapshot per second from `from` for `seconds` seconds; returns how many alerts of `kind` fired.
        private static int Feed(AlertEngine e, AppSettings st, DateTime from, int seconds, Func<DateTime, Snapshot> make, string kind)
        {
            int n = 0;
            for (int i = 0; i < seconds; i++)
                foreach (Alert a in e.Evaluate(make(from.AddSeconds(i)), st))
                    if (a.Kind == kind) n++;
            return n;
        }

        private static Snapshot Hot(DateTime t, double temp)
        {
            Snapshot s = Calm(t);
            s.CpuTempC = temp;
            return s;
        }

        private static void CpuHot_FiresAfterTheHoldTime_Once()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            TestMain.Equal(0, Feed(e, st, T0, 60, t => Hot(t, 92), "cpu_hot"), "not within the first 59 s at 92 \u00B0C");
            List<Alert> fired = Run(e, st, Hot(T0.AddSeconds(60), 92));
            TestMain.Equal(1, fired.Count, "fires at 60 s");
            TestMain.Equal("CPU hot", fired[0].Title, "title");
            TestMain.Equal("92\u00B0C for 1 min", fired[0].Body, "body");
            TestMain.Equal(Level.Red, fired[0].Severity, "severity");
            TestMain.Equal(0, Feed(e, st, T0.AddSeconds(61), 300, t => Hot(t, 92), "cpu_hot"), "no repeat while it stays hot");
        }

        private static void CpuHot_ReArmsOnlyBelowTheClearTemperature()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Feed(e, st, T0, 61, t => Hot(t, 92), "cpu_hot");
            TestMain.Equal(0, Feed(e, st, T0.AddSeconds(61), 120, t => Hot(t, 87), "cpu_hot"), "87 \u00B0C is not below 85: still the same episode");
            Feed(e, st, T0.AddSeconds(181), 61, t => Hot(t, 80), "cpu_hot"); // a minute below 85 re-arms
            TestMain.Equal(1, Feed(e, st, T0.AddSeconds(242), 61, t => Hot(t, 92), "cpu_hot"), "a new hot minute fires again");
        }

        private static void CpuHot_MissingReadingRestartsTheTiming()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Feed(e, st, T0, 30, t => Hot(t, 92), "cpu_hot");
            Snapshot gap = Hot(T0.AddSeconds(30), 92);
            gap.CpuTempC = null;
            Run(e, st, gap);
            TestMain.Equal(0, Feed(e, st, T0.AddSeconds(31), 60, t => Hot(t, 92), "cpu_hot"), "30 s + gap + 59 s is not a full minute");
            TestMain.Equal(1, Feed(e, st, T0.AddSeconds(91), 1, t => Hot(t, 92), "cpu_hot"), "a full minute after the first reading after the gap");
        }

        private static void Throttled_FiresAfterTwoMinutes()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Func<DateTime, Snapshot> limited = t => { Snapshot s = Calm(t); s.CpuLimitPercent = 72; return s; };
            TestMain.Equal(0, Feed(e, st, T0, 120, limited, "throttled"), "not within 119 s");
            List<Alert> fired = Run(e, st, limited(T0.AddSeconds(120)));
            TestMain.Equal("CPU held at 72% of full speed for 2 min", fired.Count > 0 ? fired[0].Body : "(none)", "fires at 120 s with the limit in the text");
            Run(e, st, Calm(T0.AddSeconds(121))); // back at full speed re-arms at once
            TestMain.Equal(1, Feed(e, st, T0.AddSeconds(122), 121, limited, "throttled"), "a new 2-minute episode fires again");
        }

        private static void DiskLow_FiresAtOnce_AndReArmsAboveTheClearLevel()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Func<DateTime, double, Snapshot> disk = (t, gb) => { Snapshot s = Calm(t); s.DiskFreeBytes = gb * Gb; return s; };
            List<Alert> fired = Run(e, st, disk(T0, 9));
            TestMain.Equal("C: has 9 GB free", fired.Count > 0 ? fired[0].Body : "(none)", "fires at once below 10 GB");
            TestMain.Equal(0, Run(e, st, disk(T0.AddSeconds(1), 11)).Count, "11 GB is not above 12: same episode");
            TestMain.Equal(0, Run(e, st, disk(T0.AddSeconds(2), 9)).Count, "so dipping again does not repeat");
            Run(e, st, disk(T0.AddSeconds(3), 13));
            TestMain.Equal(1, Run(e, st, disk(T0.AddSeconds(4), 9)).Count, "after going above 12 GB it can fire again");
        }

        private static void BatteryLow_OnlyOnBattery_ReArmsWhenPluggedIn()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Func<DateTime, bool, double, Snapshot> bat = (t, ac, pct) => { Snapshot s = Calm(t); s.OnAc = ac; s.BatteryPercent = pct; return s; };
            TestMain.Equal(0, Run(e, st, bat(T0, true, 15)).Count, "15 % while plugged in is fine");
            List<Alert> fired = Run(e, st, bat(T0.AddSeconds(1), false, 15));
            TestMain.Equal("15% left, on battery", fired.Count > 0 ? fired[0].Body : "(none)", "fires on battery below 20 %");
            TestMain.Equal(0, Run(e, st, bat(T0.AddSeconds(2), false, 12)).Count, "no repeat while still on battery");
            Run(e, st, bat(T0.AddSeconds(3), true, 12));
            TestMain.Equal(1, Run(e, st, bat(T0.AddSeconds(4), false, 12)).Count, "unplugging again after charging fires again");
        }

        private static void BatteryHot_FiresAfterTheHoldTime()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            Func<DateTime, Snapshot> hot = t => { Snapshot s = Calm(t); s.BatteryTempC = 46; return s; };
            TestMain.Equal(0, Feed(e, st, T0, 60, hot, "battery_hot"), "not within 59 s");
            List<Alert> fired = Run(e, st, hot(T0.AddSeconds(60)));
            TestMain.Equal("Battery at 46\u00B0C for 1 min", fired.Count > 0 ? fired[0].Body : "(none)", "fires at 60 s");
        }

        private static ComPortInfo Port(string port, string name, DateTime firstSeen, bool live)
        {
            return new ComPortInfo { Port = port, Name = name, FirstSeen = firstSeen, AppearedLive = live };
        }

        private static Snapshot WithPorts(DateTime t, params ComPortInfo[] ports)
        {
            Snapshot s = Calm(t);
            s.ComPorts = new List<ComPortInfo>(ports);
            return s;
        }

        private static void ComPort_PresentAtStartNeverAlerts()
        {
            var e = new AlertEngine();
            TestMain.Equal(0, Run(e, new AppSettings(), WithPorts(T0.AddSeconds(10), Port("COM3", "USB Serial", T0, false))).Count, "port there at start-up");
        }

        private static void ComPort_WithName_AlertsOnce_AndAgainAfterReplug()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            List<Alert> fired = Run(e, st, WithPorts(T0, Port("COM5", "Silicon Labs CP210x", T0, true)));
            TestMain.Equal("COM5 connected", fired.Count > 0 ? fired[0].Title : "(none)", "title names the port");
            TestMain.Equal("Silicon Labs CP210x", fired.Count > 0 ? fired[0].Body : "(none)", "body names the device");
            TestMain.Equal(Level.Ok, fired.Count > 0 ? fired[0].Severity : Level.Unknown, "teal");
            TestMain.Equal(0, Run(e, st, WithPorts(T0.AddSeconds(2), Port("COM5", "Silicon Labs CP210x", T0, true))).Count, "only once while plugged in");
            Run(e, st, WithPorts(T0.AddSeconds(4)));
            TestMain.Equal(1, Run(e, st, WithPorts(T0.AddSeconds(6), Port("COM5", "Silicon Labs CP210x", T0.AddSeconds(6), true))).Count, "again after unplug and replug");
        }

        private static void ComPort_WithoutName_WaitsThreeSeconds()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            TestMain.Equal(0, Run(e, st, WithPorts(T0.AddSeconds(2), Port("COM7", null, T0, true))).Count, "waits for the name for up to 3 s");
            List<Alert> fired = Run(e, st, WithPorts(T0.AddSeconds(3), Port("COM7", null, T0, true)));
            TestMain.Equal("New serial device", fired.Count > 0 ? fired[0].Body : "(none)", "then alerts without a name");
        }

        private static void Switches_TurnAlertsOff()
        {
            var off = new AppSettings { AlertsEnabled = false };
            var e = new AlertEngine();
            Snapshot s = Calm(T0);
            s.DiskFreeBytes = 1 * Gb;
            TestMain.Equal(0, Run(e, off, s).Count, "master switch off: nothing");
            var diskOff = new AppSettings { AlertDiskLowOn = false };
            var e2 = new AlertEngine();
            Snapshot s2 = WithPorts(T0, Port("COM5", "X", T0, true));
            s2.DiskFreeBytes = 1 * Gb;
            List<Alert> fired = Run(e2, diskOff, s2);
            TestMain.Equal(1, fired.Count, "disk alert off, COM alert still on");
            TestMain.Equal("com_port", fired.Count > 0 ? fired[0].Kind : "(none)", "the one that fired is the COM alert");
        }

        private static void Recent_IsTodayNewestFirstAtMostTen()
        {
            var e = new AlertEngine();
            var st = new AppSettings();
            for (int i = 0; i < 12; i++)
            {
                Run(e, st, WithPorts(T0.AddSeconds(2 * i), Port("COM" + (i + 1), "X", T0.AddSeconds(2 * i), true)));
                Run(e, st, WithPorts(T0.AddSeconds(2 * i + 1))); // unplug so the next one is fresh
            }
            List<Alert> recent = e.Recent(T0.AddHours(1));
            TestMain.Equal(10, recent.Count, "at most 10");
            TestMain.Equal("COM12 connected", recent[0].Title, "newest first");
            TestMain.Equal(0, e.Recent(T0.AddDays(1)).Count, "yesterday's alerts are not today's");
        }
    }
}
```

Register:

```csharp
            SettingsV2Tests.Run();
            AlertEngineTests.Run();
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0246: The type or namespace name 'AlertEngine' could not be found` (and `Alert`), plus missing `AppSettings` / `Snapshot` members.

- [ ] **Step 3: Replace `src\Settings.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DesktopMonitor
{
    // User-editable settings (spec section 7). Defaults live in the field initialisers.
    public sealed class AppSettings
    {
        public double? X;
        public double? Y;
        public double Opacity = 0.93;
        public double UsageAmber = 70, UsageRed = 85;
        public double TempAmber = 80, TempRed = 85;
        public double DiskAmber = 90, DiskRed = 95;
        public double TempRingMin = 30, TempRingMax = 100;
        public string ZoneCpu = "CPUZ", ZoneSkin = "SK1Z", ZoneBattery = "BATZ";

        // v2: CSV log (spec section 8) and alerts (spec section 7).
        public bool LogEnabled = true;
        public double LogIntervalSeconds = 10, LogRetentionDays = 30;
        public bool AlertsEnabled = true;
        public bool AlertCpuHotOn = true, AlertThrottledOn = true, AlertDiskLowOn = true, AlertBatteryLowOn = true, AlertBatteryHotOn = true, AlertComPortsOn = true;
        public double AlertCpuTemp = 90, AlertCpuClear = 85, AlertCpuSeconds = 60;
        public double AlertThrottleSeconds = 120;
        public double AlertDiskFreeGb = 10, AlertDiskClearGb = 12;
        public double AlertBatteryPercent = 20;
        public double AlertBatteryTemp = 45, AlertBatteryTempClear = 42, AlertBatteryTempSeconds = 60;

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }

    public static class SettingsStore
    {
        public static string DefaultPath
        {
            get { return Path.Combine(Log.DataDir, "settings.json"); }
        }

        // Throws FormatException when the text is not a JSON object.
        // Missing or mistyped keys fall back to defaults; out-of-range numbers are clamped.
        public static AppSettings Parse(string json)
        {
            object root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(json ?? "");
            }
            catch (Exception ex)
            {
                throw new FormatException("settings.json is not valid JSON: " + ex.Message, ex);
            }
            var raw = root as Dictionary<string, object>;
            if (raw == null) throw new FormatException("settings.json must contain a JSON object");

            // Copied one by one: the dictionary constructor throws when two keys differ only in case; here the last one wins.
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kv in raw) map[kv.Key] = kv.Value;
            var d = new AppSettings();
            var s = new AppSettings();
            s.X = NullableNumber(map, "x");
            s.Y = NullableNumber(map, "y");
            s.Opacity = Number(map, "opacity", d.Opacity, 0.2, 1.0);
            s.UsageAmber = Number(map, "usageAmber", d.UsageAmber, 0, 100);
            s.UsageRed = Number(map, "usageRed", d.UsageRed, 0, 100);
            s.TempAmber = Number(map, "tempAmber", d.TempAmber, 0, 150);
            s.TempRed = Number(map, "tempRed", d.TempRed, 0, 150);
            s.DiskAmber = Number(map, "diskAmber", d.DiskAmber, 0, 100);
            s.DiskRed = Number(map, "diskRed", d.DiskRed, 0, 100);
            s.TempRingMin = Number(map, "tempRingMin", d.TempRingMin, 0, 150);
            s.TempRingMax = Number(map, "tempRingMax", d.TempRingMax, 0, 150);
            if (s.TempRingMax <= s.TempRingMin)
            {
                s.TempRingMin = d.TempRingMin;
                s.TempRingMax = d.TempRingMax;
            }
            s.ZoneCpu = Text(map, "zoneCpu", d.ZoneCpu);
            s.ZoneSkin = Text(map, "zoneSkin", d.ZoneSkin);
            s.ZoneBattery = Text(map, "zoneBattery", d.ZoneBattery);
            s.LogEnabled = Flag(map, "logEnabled", d.LogEnabled);
            s.LogIntervalSeconds = Number(map, "logIntervalSeconds", d.LogIntervalSeconds, 5, 300);
            s.LogRetentionDays = Number(map, "logRetentionDays", d.LogRetentionDays, 1, 365);
            s.AlertsEnabled = Flag(map, "alertsEnabled", d.AlertsEnabled);
            s.AlertCpuHotOn = Flag(map, "alertCpuHotOn", d.AlertCpuHotOn);
            s.AlertThrottledOn = Flag(map, "alertThrottledOn", d.AlertThrottledOn);
            s.AlertDiskLowOn = Flag(map, "alertDiskLowOn", d.AlertDiskLowOn);
            s.AlertBatteryLowOn = Flag(map, "alertBatteryLowOn", d.AlertBatteryLowOn);
            s.AlertBatteryHotOn = Flag(map, "alertBatteryHotOn", d.AlertBatteryHotOn);
            s.AlertComPortsOn = Flag(map, "alertComPortsOn", d.AlertComPortsOn);
            s.AlertCpuTemp = Number(map, "alertCpuTemp", d.AlertCpuTemp, 0, 150);
            s.AlertCpuClear = Number(map, "alertCpuClear", d.AlertCpuClear, 0, 150);
            s.AlertCpuSeconds = Number(map, "alertCpuSeconds", d.AlertCpuSeconds, 0, 3600);
            s.AlertThrottleSeconds = Number(map, "alertThrottleSeconds", d.AlertThrottleSeconds, 0, 3600);
            s.AlertDiskFreeGb = Number(map, "alertDiskFreeGb", d.AlertDiskFreeGb, 0, 100000);
            s.AlertDiskClearGb = Number(map, "alertDiskClearGb", d.AlertDiskClearGb, 0, 100000);
            s.AlertBatteryPercent = Number(map, "alertBatteryPercent", d.AlertBatteryPercent, 0, 100);
            s.AlertBatteryTemp = Number(map, "alertBatteryTemp", d.AlertBatteryTemp, 0, 150);
            s.AlertBatteryTempClear = Number(map, "alertBatteryTempClear", d.AlertBatteryTempClear, 0, 150);
            s.AlertBatteryTempSeconds = Number(map, "alertBatteryTempSeconds", d.AlertBatteryTempSeconds, 0, 3600);
            return s;
        }

        public static string ToJson(AppSettings s)
        {
            var js = new JavaScriptSerializer();
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"x\": ").Append(s.X.HasValue ? Num(s.X.Value) : "null").Append(",\r\n");
            sb.Append("  \"y\": ").Append(s.Y.HasValue ? Num(s.Y.Value) : "null").Append(",\r\n");
            sb.Append("  \"opacity\": ").Append(Num(s.Opacity)).Append(",\r\n");
            sb.Append("  \"usageAmber\": ").Append(Num(s.UsageAmber)).Append(",\r\n");
            sb.Append("  \"usageRed\": ").Append(Num(s.UsageRed)).Append(",\r\n");
            sb.Append("  \"tempAmber\": ").Append(Num(s.TempAmber)).Append(",\r\n");
            sb.Append("  \"tempRed\": ").Append(Num(s.TempRed)).Append(",\r\n");
            sb.Append("  \"diskAmber\": ").Append(Num(s.DiskAmber)).Append(",\r\n");
            sb.Append("  \"diskRed\": ").Append(Num(s.DiskRed)).Append(",\r\n");
            sb.Append("  \"tempRingMin\": ").Append(Num(s.TempRingMin)).Append(",\r\n");
            sb.Append("  \"tempRingMax\": ").Append(Num(s.TempRingMax)).Append(",\r\n");
            sb.Append("  \"zoneCpu\": ").Append(js.Serialize(s.ZoneCpu)).Append(",\r\n");
            sb.Append("  \"zoneSkin\": ").Append(js.Serialize(s.ZoneSkin)).Append(",\r\n");
            sb.Append("  \"zoneBattery\": ").Append(js.Serialize(s.ZoneBattery)).Append(",\r\n");
            sb.Append("  \"logEnabled\": ").Append(Bool(s.LogEnabled)).Append(",\r\n");
            sb.Append("  \"logIntervalSeconds\": ").Append(Num(s.LogIntervalSeconds)).Append(",\r\n");
            sb.Append("  \"logRetentionDays\": ").Append(Num(s.LogRetentionDays)).Append(",\r\n");
            sb.Append("  \"alertsEnabled\": ").Append(Bool(s.AlertsEnabled)).Append(",\r\n");
            sb.Append("  \"alertCpuHotOn\": ").Append(Bool(s.AlertCpuHotOn)).Append(",\r\n");
            sb.Append("  \"alertThrottledOn\": ").Append(Bool(s.AlertThrottledOn)).Append(",\r\n");
            sb.Append("  \"alertDiskLowOn\": ").Append(Bool(s.AlertDiskLowOn)).Append(",\r\n");
            sb.Append("  \"alertBatteryLowOn\": ").Append(Bool(s.AlertBatteryLowOn)).Append(",\r\n");
            sb.Append("  \"alertBatteryHotOn\": ").Append(Bool(s.AlertBatteryHotOn)).Append(",\r\n");
            sb.Append("  \"alertComPortsOn\": ").Append(Bool(s.AlertComPortsOn)).Append(",\r\n");
            sb.Append("  \"alertCpuTemp\": ").Append(Num(s.AlertCpuTemp)).Append(",\r\n");
            sb.Append("  \"alertCpuClear\": ").Append(Num(s.AlertCpuClear)).Append(",\r\n");
            sb.Append("  \"alertCpuSeconds\": ").Append(Num(s.AlertCpuSeconds)).Append(",\r\n");
            sb.Append("  \"alertThrottleSeconds\": ").Append(Num(s.AlertThrottleSeconds)).Append(",\r\n");
            sb.Append("  \"alertDiskFreeGb\": ").Append(Num(s.AlertDiskFreeGb)).Append(",\r\n");
            sb.Append("  \"alertDiskClearGb\": ").Append(Num(s.AlertDiskClearGb)).Append(",\r\n");
            sb.Append("  \"alertBatteryPercent\": ").Append(Num(s.AlertBatteryPercent)).Append(",\r\n");
            sb.Append("  \"alertBatteryTemp\": ").Append(Num(s.AlertBatteryTemp)).Append(",\r\n");
            sb.Append("  \"alertBatteryTempClear\": ").Append(Num(s.AlertBatteryTempClear)).Append(",\r\n");
            sb.Append("  \"alertBatteryTempSeconds\": ").Append(Num(s.AlertBatteryTempSeconds)).Append("\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        // First run writes the defaults. A malformed file is left untouched for the user to fix; defaults are used meanwhile.
        public static AppSettings LoadOrCreate(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var defaults = new AppSettings();
                    Save(path, defaults);
                    return defaults;
                }
                return Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Write("Settings: using defaults, " + ex.Message);
                return new AppSettings();
            }
        }

        public static void Save(string path, AppSettings s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ToJson(s), new UTF8Encoding(false));
        }

        private static double Number(Dictionary<string, object> map, string key, double fallback, double min, double max)
        {
            object v;
            if (!map.TryGetValue(key, out v) || !IsNumber(v)) return fallback;
            double x = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            return x < min ? min : (x > max ? max : x);
        }

        private static double? NullableNumber(Dictionary<string, object> map, string key)
        {
            object v;
            if (!map.TryGetValue(key, out v) || !IsNumber(v)) return null;
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        private static string Text(Dictionary<string, object> map, string key, string fallback)
        {
            object v;
            if (!map.TryGetValue(key, out v)) return fallback;
            var s = v as string;
            return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
        }

        // JSON true/false only; anything else (a string "yes", a number) keeps the default.
        private static bool Flag(Dictionary<string, object> map, string key, bool fallback)
        {
            object v;
            return map.TryGetValue(key, out v) && v is bool ? (bool)v : fallback;
        }

        private static string Bool(bool v)
        {
            return v ? "true" : "false";
        }

        private static bool IsNumber(object v)
        {
            return v is int || v is long || v is decimal || v is double;
        }

        private static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    // Raises Changed (on a thread-pool thread) 300 ms after settings.json stops changing. Invalid edits are logged and ignored.
    public sealed class SettingsWatcher : IDisposable
    {
        private readonly string _path;
        private readonly FileSystemWatcher _fsw;
        private readonly Timer _debounce;

        public event Action<AppSettings> Changed;

        public SettingsWatcher(string path)
        {
            _path = path;
            _debounce = new Timer(delegate { Reload(); }, null, Timeout.Infinite, Timeout.Infinite);
            _fsw = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path));
            _fsw.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            _fsw.Changed += delegate { Kick(); };
            _fsw.Created += delegate { Kick(); };
            _fsw.Renamed += delegate { Kick(); };
            _fsw.EnableRaisingEvents = true;
        }

        private void Kick()
        {
            _debounce.Change(300, Timeout.Infinite);
        }

        private void Reload()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    AppSettings s = SettingsStore.Parse(File.ReadAllText(_path));
                    Action<AppSettings> handler = Changed;
                    if (handler != null) handler(s);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(200); // the editor may still be writing the file
                }
                catch (FormatException ex)
                {
                    Log.Write("Settings: ignored invalid edit, " + ex.Message);
                    return;
                }
                catch (Exception ex) // runs on a timer thread, where anything unhandled would end the process
                {
                    Log.Write("Settings: reload failed, " + ex.GetType().Name + ": " + ex.Message);
                    return;
                }
            }
            Log.Write("Settings: reload skipped, settings.json stayed locked");
        }

        public void Dispose()
        {
            _fsw.Dispose();
            _debounce.Dispose();
        }
    }
}
```

- [ ] **Step 4: Replace `src\Snapshot.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace DesktopMonitor
{
    // One sample of every reading; null means "could not be read" and is shown as "--".
    // The sampler creates a new instance each tick and nothing modifies it afterwards.
    public sealed class Snapshot
    {
        public DateTime Time = DateTime.Now;
        public double? CpuPercent;
        public double? RamPercent;
        public double? GpuPercent;
        public double? DiskPercent;
        public double? DiskFreeBytes;
        public double? DownBytesPerSec;
        public double? UpBytesPerSec;
        public double? BatteryPercent;
        public bool OnAc;
        public bool Charging;
        public int? BatteryMinutesLeft;
        public double? CpuTempC;
        public double? SkinTempC;
        public double? BatteryTempC;
        public double? CpuLimitPercent;
        public bool Throttled;
        public List<AppUsage> TopApps = new List<AppUsage>();       // up to 5, highest CPU first
        public List<ComPortInfo> ComPorts = new List<ComPortInfo>(); // natural port order
    }
}
```

- [ ] **Step 5: Create `src\AlertEngine.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace DesktopMonitor
{
    public sealed class Alert
    {
        public DateTime Time;
        public string Kind;
        public string Title;
        public string Body;
        public Level Severity;
    }

    // One alert's life cycle: fires once after its condition has held for `holdFor`, then re-arms only after the clear
    // condition has held for `clearFor`. A missing reading (null) restarts the timing in progress and never fires or clears.
    public sealed class Episode
    {
        private DateTime? _since, _clearSince;
        private bool _fired;

        public bool Fired { get { return _fired; } }

        public bool Step(DateTime now, bool? trigger, bool? clear, TimeSpan holdFor, TimeSpan clearFor)
        {
            if (!trigger.HasValue || !clear.HasValue)
            {
                _since = null;
                _clearSince = null;
                return false;
            }
            if (!_fired)
            {
                if (!trigger.Value) { _since = null; return false; }
                if (!_since.HasValue) _since = now;
                if (now - _since.Value < holdFor) return false;
                _fired = true;
                _clearSince = null;
                return true;
            }
            if (!clear.Value) { _clearSince = null; return false; }
            if (!_clearSince.HasValue) _clearSince = now;
            if (now - _clearSince.Value >= clearFor)
            {
                _fired = false;
                _since = null;
            }
            return false;
        }
    }

    // The six alerts of spec section 7. Pure: feed it each Snapshot; it returns the alerts that fire now.
    public sealed class AlertEngine
    {
        private static readonly TimeSpan ComNameWait = TimeSpan.FromSeconds(3);
        private readonly Episode _cpuHot = new Episode();
        private readonly Episode _throttled = new Episode();
        private readonly Episode _diskLow = new Episode();
        private readonly Episode _batteryLow = new Episode();
        private readonly Episode _batteryHot = new Episode();
        private readonly HashSet<string> _comAlerted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Alert> _recent = new List<Alert>();

        public List<Alert> Evaluate(Snapshot s, AppSettings st)
        {
            var fired = new List<Alert>();
            DateTime now = s.Time;

            TimeSpan cpuHold = Seconds(st.AlertCpuSeconds);
            if (_cpuHot.Step(now, AtLeast(s.CpuTempC, st.AlertCpuTemp), Below(s.CpuTempC, st.AlertCpuClear), cpuHold, cpuHold) && st.AlertCpuHotOn)
                Add(fired, now, "cpu_hot", "CPU hot", Rules.FormatTemp(s.CpuTempC, true) + " for " + Rules.FormatDuration(st.AlertCpuSeconds), Level.Red);

            bool? limited = s.CpuLimitPercent.HasValue ? Rules.IsBelowFullSpeed(s.CpuLimitPercent.Value) : (bool?)null;
            bool? fullSpeed = limited.HasValue ? !limited.Value : (bool?)null;
            if (_throttled.Step(now, limited, fullSpeed, Seconds(st.AlertThrottleSeconds), TimeSpan.Zero) && st.AlertThrottledOn)
                Add(fired, now, "throttled", "Throttled", "CPU held at " + Rules.FormatPercent(s.CpuLimitPercent) + " of full speed for " + Rules.FormatDuration(st.AlertThrottleSeconds), Level.Amber);

            double? freeGb = s.DiskFreeBytes.HasValue ? s.DiskFreeBytes.Value / (1024.0 * 1024 * 1024) : (double?)null;
            bool? diskLow = freeGb.HasValue ? freeGb.Value < st.AlertDiskFreeGb : (bool?)null;
            bool? diskOk = freeGb.HasValue ? freeGb.Value > st.AlertDiskClearGb : (bool?)null;
            if (_diskLow.Step(now, diskLow, diskOk, TimeSpan.Zero, TimeSpan.Zero) && st.AlertDiskLowOn)
                Add(fired, now, "disk_low", "Disk low", "C: has " + Rules.FormatDiskFree(s.DiskFreeBytes) + " free", Level.Amber);

            bool? batteryLow = s.BatteryPercent.HasValue ? !s.OnAc && s.BatteryPercent.Value < st.AlertBatteryPercent : (bool?)null;
            bool? pluggedIn = s.BatteryPercent.HasValue ? s.OnAc : (bool?)null;
            if (_batteryLow.Step(now, batteryLow, pluggedIn, TimeSpan.Zero, TimeSpan.Zero) && st.AlertBatteryLowOn)
                Add(fired, now, "battery_low", "Battery low", Rules.FormatPercent(s.BatteryPercent) + " left, on battery", Level.Amber);

            TimeSpan batteryHold = Seconds(st.AlertBatteryTempSeconds);
            if (_batteryHot.Step(now, AtLeast(s.BatteryTempC, st.AlertBatteryTemp), Below(s.BatteryTempC, st.AlertBatteryTempClear), batteryHold, batteryHold) && st.AlertBatteryHotOn)
                Add(fired, now, "battery_hot", "Battery hot", "Battery at " + Rules.FormatTemp(s.BatteryTempC, true) + " for " + Rules.FormatDuration(st.AlertBatteryTempSeconds), Level.Red);

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ComPortInfo port in s.ComPorts)
            {
                present.Add(port.Port);
                if (!port.AppearedLive || _comAlerted.Contains(port.Port)) continue;
                if (port.Name == null && now - port.FirstSeen < ComNameWait) continue; // give the name lookup a moment
                _comAlerted.Add(port.Port);
                if (st.AlertComPortsOn) Add(fired, now, "com_port", port.Port + " connected", port.Name ?? "New serial device", Level.Ok);
            }
            _comAlerted.RemoveWhere(p => !present.Contains(p));

            if (!st.AlertsEnabled) fired.Clear();
            foreach (Alert a in fired) _recent.Insert(0, a);
            if (_recent.Count > 10) _recent.RemoveRange(10, _recent.Count - 10);
            return fired;
        }

        // Today's alerts, newest first, at most 10.
        public List<Alert> Recent(DateTime now)
        {
            return _recent.FindAll(a => a.Time.Date == now.Date);
        }

        private static void Add(List<Alert> list, DateTime now, string kind, string title, string body, Level severity)
        {
            list.Add(new Alert { Time = now, Kind = kind, Title = title, Body = body, Severity = severity });
        }

        private static bool? AtLeast(double? value, double limit)
        {
            return value.HasValue ? Rules.Display(value.Value) >= limit : (bool?)null;
        }

        private static bool? Below(double? value, double limit)
        {
            return value.HasValue ? Rules.Display(value.Value) < limit : (bool?)null;
        }

        private static TimeSpan Seconds(double s)
        {
            return TimeSpan.FromSeconds(s);
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `266 passed, 0 failed`.

- [ ] **Step 7: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/Settings.cs src/Snapshot.cs src/AlertEngine.cs tests/SettingsV2Tests.cs tests/AlertEngineTests.cs tests/TestMain.cs && git commit -m "Add alert settings, v2 snapshot fields and the alert engine

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: CSV log

**Files:**
- Create: `src\CsvLog.cs`, `tests\CsvLogTests.cs`
- Modify: `tests\TestMain.cs`

**Interfaces:**
- Consumes: `Snapshot`, `Alert` (Task 5), `Log.Write` (v1).
- Produces:
  - `public static class CsvLog` — `const string Header`, `const string AlertHeader`, `string DefaultDir`, `string FileNameFor(DateTime)`, `string Time(DateTime)`, `string Num(double?)`, `string Quote(string)`, `string AlertRow(Alert)`, `List<string> Expired(IEnumerable<string> fileNames, DateTime today, int retentionDays)`.
  - `public sealed class LogWindow` — `void Add(Snapshot)`, `bool Due(DateTime now, TimeSpan interval)`, `void Next(TimeSpan interval)`, `string ToRow(string powerMode)`.
  - `public sealed class CsvLogWriter` — `CsvLogWriter(string dir)`, `string Dir`, `void Append(DateTime time, string row, int retentionDays)`, `void AppendAlert(Alert)`, `void Prune(DateTime today, int retentionDays)`.

- [ ] **Step 1: Write the failing tests `tests\CsvLogTests.cs` and register them**

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopMonitor.Tests
{
    internal static class CsvLogTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 3, 14, 0, 0);

        public static void Run()
        {
            Row_HasOneFieldPerHeaderColumn();
            Row_AveragesPeaksAndLatest();
            Row_MissingReadingsAreEmptyFields();
            Quote_OnlyWhenNeeded();
            Window_KeepsItsCadence_AndSkipsAfterAGap();
            Expired_OnlyDailyFilesOlderThanRetention();
            Writer_WritesTheHeaderOnce();
            Writer_KeepsRowsWhileTheFileIsLocked();
        }

        private static Snapshot S(DateTime t, double cpu, double temp)
        {
            var s = new Snapshot
            {
                Time = t, CpuPercent = cpu, GpuPercent = 5, CpuTempC = temp, CpuLimitPercent = 100, RamPercent = 62,
                DownBytesPerSec = 1024, UpBytesPerSec = 2048, DiskFreeBytes = 36.0 * 1024 * 1024 * 1024,
                BatteryPercent = 99, OnAc = true, SkinTempC = 41, BatteryTempC = 33
            };
            s.TopApps.Add(new AppUsage { Name = "brave", CpuPercent = 12.34, PrivateBytes = 1 });
            return s;
        }

        private static void Row_HasOneFieldPerHeaderColumn()
        {
            var w = new LogWindow();
            w.Add(S(T0, 10, 70));
            TestMain.Equal(CsvLog.Header.Split(',').Length, w.ToRow("Balanced").Split(',').Length, "column count matches the header");
        }

        private static void Row_AveragesPeaksAndLatest()
        {
            var w = new LogWindow();
            w.Add(S(T0, 10, 70));
            w.Add(S(T0.AddSeconds(1), 20, 88));
            Snapshot last = S(T0.AddSeconds(2), 30, 75);
            last.GpuPercent = null; // ignored in the GPU average
            last.OnAc = false;
            w.Add(last);
            string[] f = w.ToRow("Efficiency").Split(',');
            TestMain.Equal("2026-10-03T14:00:02", f[0], "time of the latest sample");
            TestMain.Equal("20.0", f[1], "CPU is the average");
            TestMain.Equal("5.0", f[4], "GPU average skips the missing reading");
            TestMain.Equal("36.0", f[5], "disk free in GB");
            TestMain.Equal("1.0", f[6], "download in KB/s");
            TestMain.Equal("2.0", f[7], "upload in KB/s");
            TestMain.Equal("0", f[9], "on_ac is the latest value");
            TestMain.Equal("88.0", f[10], "CPU temp is the peak");
            TestMain.Equal("brave", f[13], "top app");
            TestMain.Equal("12.3", f[14], "top app CPU, one decimal");
            TestMain.Equal("Efficiency", f[15], "power mode");
        }

        private static void Row_MissingReadingsAreEmptyFields()
        {
            var w = new LogWindow();
            w.Add(new Snapshot { Time = T0 });
            string[] f = w.ToRow("--").Split(',');
            TestMain.Equal("", f[1], "no CPU reading: empty");
            TestMain.Equal("", f[10], "no temperature: empty");
            TestMain.Equal("", f[13], "no top app: empty");
        }

        private static void Quote_OnlyWhenNeeded()
        {
            TestMain.Equal("brave", CsvLog.Quote("brave"), "plain text unchanged");
            TestMain.Equal("\"a,b\"", CsvLog.Quote("a,b"), "comma quoted");
            TestMain.Equal("\"say \"\"hi\"\"\"", CsvLog.Quote("say \"hi\""), "quotes doubled");
            TestMain.Equal("", CsvLog.Quote(null), "null is empty");
        }

        private static void Window_KeepsItsCadence_AndSkipsAfterAGap()
        {
            var w = new LogWindow();
            var every = TimeSpan.FromSeconds(10);
            for (int i = 0; i < 10; i++) w.Add(S(T0.AddSeconds(i), 10, 70));
            TestMain.True(!w.Due(T0.AddSeconds(9), every), "not due after 9 s");
            w.Add(S(T0.AddSeconds(10), 10, 70));
            TestMain.True(w.Due(T0.AddSeconds(10), every), "due after 10 s");
            w.Next(every);
            w.Add(S(T0.AddSeconds(11), 10, 70));
            TestMain.True(!w.Due(T0.AddSeconds(19), every), "next window started at 10 s, so not due at 19 s");
            TestMain.True(w.Due(T0.AddSeconds(20), every), "due at 20 s");
            w.Add(S(T0.AddSeconds(3600), 10, 70)); // laptop slept for an hour
            w.Next(every);
            w.Add(S(T0.AddSeconds(3601), 10, 70));
            TestMain.True(!w.Due(T0.AddSeconds(3601), every), "after a long gap it restarts instead of writing catch-up rows");
        }

        private static void Expired_OnlyDailyFilesOlderThanRetention()
        {
            var names = new List<string> { "2026-09-02.csv", "2026-09-03.csv", "2026-10-03.csv", "alerts.csv", "notes.txt", "2026-13-40.csv", "2026-09-01.csv.bak" };
            List<string> expired = CsvLog.Expired(names, T0, 30);
            TestMain.Equal(1, expired.Count, "only one file is old enough");
            TestMain.Equal("2026-09-02.csv", expired.Count > 0 ? expired[0] : "(none)", "31 days old goes; 30 days old stays");
        }

        private static string TempDir()
        {
            return Path.Combine(Path.GetTempPath(), "DesktopMonitorTests-" + Guid.NewGuid().ToString("N"));
        }

        private static void Writer_WritesTheHeaderOnce()
        {
            string dir = TempDir();
            try
            {
                var writer = new CsvLogWriter(dir);
                writer.Append(T0, "row1", 30);
                writer.Append(T0.AddSeconds(10), "row2", 30);
                string[] lines = File.ReadAllLines(Path.Combine(dir, "2026-10-03.csv"));
                TestMain.Equal(3, lines.Length, "header plus two rows");
                TestMain.Equal(CsvLog.Header, lines[0], "header first");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // Review focus 3: opening today's CSV in Excel locks it; rows written meanwhile must not be lost.
        private static void Writer_KeepsRowsWhileTheFileIsLocked()
        {
            string dir = TempDir();
            string path = Path.Combine(dir, "2026-10-03.csv");
            try
            {
                var writer = new CsvLogWriter(dir);
                writer.Append(T0, "row1", 30);
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    writer.Append(T0.AddSeconds(10), "row2", 30); // fails quietly, row kept
                writer.Append(T0.AddSeconds(20), "row3", 30);
                string[] lines = File.ReadAllLines(path);
                TestMain.Equal("row1|row2|row3", string.Join("|", lines, 1, lines.Length - 1), "the row written while locked arrives, in order");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
```

Register:

```csharp
            CsvLogTests.Run();
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0246: The type or namespace name 'LogWindow' could not be found` (and `CsvLog`, `CsvLogWriter`).

- [ ] **Step 3: Create `src\CsvLog.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopMonitor
{
    // CSV formatting and retention rules (spec section 8). Pure; tested in CsvLogTests.
    public static class CsvLog
    {
        public const string Header = "time,cpu_pct,cpu_limit_pct,ram_pct,gpu_pct,disk_free_gb,down_kbps,up_kbps,battery_pct,on_ac,cpu_temp_c,skin_temp_c,battery_temp_c,top_app,top_app_cpu_pct,power_mode";
        public const string AlertHeader = "time,alert,title,detail";
        private static readonly Regex DailyFile = new Regex(@"^(\d{4}-\d{2}-\d{2})\.csv$", RegexOptions.IgnoreCase);

        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopMonitor", "logs"); }
        }

        public static string FileNameFor(DateTime day)
        {
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";
        }

        public static string Time(DateTime t)
        {
            return t.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        // One decimal, '.' as the decimal point, empty for a missing reading.
        public static string Num(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
        }

        public static string Quote(string s)
        {
            if (s == null) return "";
            return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static string AlertRow(Alert a)
        {
            return Time(a.Time) + "," + a.Kind + "," + Quote(a.Title) + "," + Quote(a.Body);
        }

        // Daily files (exactly YYYY-MM-DD.csv) whose date is more than `retentionDays` before `today`. Nothing else is ever listed.
        public static List<string> Expired(IEnumerable<string> fileNames, DateTime today, int retentionDays)
        {
            var expired = new List<string>();
            DateTime oldestKept = today.Date.AddDays(-retentionDays);
            foreach (string name in fileNames)
            {
                Match m = DailyFile.Match(name);
                DateTime day;
                if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) && day < oldestKept)
                    expired.Add(name);
            }
            return expired;
        }
    }

    // Collects the 1 s snapshots of one log row: averages for CPU, GPU and network, the CPU temperature peak,
    // and the latest value of everything else.
    public sealed class LogWindow
    {
        private DateTime _start = DateTime.MinValue;
        private int _count, _cpuN, _gpuN, _downN, _upN;
        private double _cpu, _gpu, _down, _up;
        private double? _peak;
        private Snapshot _last;

        public void Add(Snapshot s)
        {
            if (_start == DateTime.MinValue) _start = s.Time;
            _count++;
            if (s.CpuPercent.HasValue) { _cpu += s.CpuPercent.Value; _cpuN++; }
            if (s.GpuPercent.HasValue) { _gpu += s.GpuPercent.Value; _gpuN++; }
            if (s.DownBytesPerSec.HasValue) { _down += s.DownBytesPerSec.Value; _downN++; }
            if (s.UpBytesPerSec.HasValue) { _up += s.UpBytesPerSec.Value; _upN++; }
            if (s.CpuTempC.HasValue && (!_peak.HasValue || s.CpuTempC.Value > _peak.Value)) _peak = s.CpuTempC;
            _last = s;
        }

        public bool Due(DateTime now, TimeSpan interval)
        {
            return _count > 0 && now - _start >= interval;
        }

        // Starts the next window where this one ended, so rows stay exactly `interval` apart; after a long gap
        // (sleep) it restarts from the latest sample instead of writing a burst of catch-up rows.
        public void Next(TimeSpan interval)
        {
            DateTime end = _start + interval;
            DateTime lastTime = _last != null ? _last.Time : end;
            Clear();
            _start = lastTime - end >= interval ? lastTime : end;
        }

        public string ToRow(string powerMode)
        {
            Snapshot s = _last;
            AppUsage top = s.TopApps.Count > 0 ? s.TopApps[0] : null;
            var row = new StringBuilder();
            row.Append(CsvLog.Time(s.Time)).Append(',');
            row.Append(CsvLog.Num(Avg(_cpu, _cpuN))).Append(',');
            row.Append(CsvLog.Num(s.CpuLimitPercent)).Append(',');
            row.Append(CsvLog.Num(s.RamPercent)).Append(',');
            row.Append(CsvLog.Num(Avg(_gpu, _gpuN))).Append(',');
            row.Append(CsvLog.Num(s.DiskFreeBytes.HasValue ? s.DiskFreeBytes.Value / (1024.0 * 1024 * 1024) : (double?)null)).Append(',');
            row.Append(CsvLog.Num(Kb(Avg(_down, _downN)))).Append(',');
            row.Append(CsvLog.Num(Kb(Avg(_up, _upN)))).Append(',');
            row.Append(CsvLog.Num(s.BatteryPercent)).Append(',');
            row.Append(s.OnAc ? "1" : "0").Append(',');
            row.Append(CsvLog.Num(_peak)).Append(',');
            row.Append(CsvLog.Num(s.SkinTempC)).Append(',');
            row.Append(CsvLog.Num(s.BatteryTempC)).Append(',');
            row.Append(top == null ? "" : CsvLog.Quote(top.Name)).Append(',');
            row.Append(top == null ? "" : CsvLog.Num(top.CpuPercent)).Append(',');
            row.Append(CsvLog.Quote(powerMode));
            return row.ToString();
        }

        private void Clear()
        {
            _count = _cpuN = _gpuN = _downN = _upN = 0;
            _cpu = _gpu = _down = _up = 0;
            _peak = null;
            _last = null;
        }

        private static double? Avg(double sum, int n)
        {
            return n > 0 ? sum / n : (double?)null;
        }

        private static double? Kb(double? bytesPerSecond)
        {
            return bytesPerSecond.HasValue ? bytesPerSecond.Value / 1024 : (double?)null;
        }
    }

    // File side of the log: one file per day plus alerts.csv. Rows that cannot be written (for example while the
    // file is open in Excel) are kept in memory, up to a day's worth, and written with the next successful row.
    public sealed class CsvLogWriter
    {
        private const int MaxPending = 8640;
        private readonly string _dir;
        private readonly Dictionary<string, List<string>> _pending = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private string _failingPath;
        private DateTime _prunedFor = DateTime.MinValue;

        public CsvLogWriter(string dir)
        {
            _dir = dir;
        }

        public string Dir { get { return _dir; } }

        public void Append(DateTime time, string row, int retentionDays)
        {
            Write(Path.Combine(_dir, CsvLog.FileNameFor(time)), CsvLog.Header, row);
            if (_prunedFor != time.Date)
            {
                _prunedFor = time.Date;
                Prune(time, retentionDays);
            }
        }

        public void AppendAlert(Alert a)
        {
            Write(Path.Combine(_dir, "alerts.csv"), CsvLog.AlertHeader, CsvLog.AlertRow(a));
        }

        public void Prune(DateTime today, int retentionDays)
        {
            try
            {
                if (!Directory.Exists(_dir)) return;
                var names = new List<string>();
                foreach (string path in Directory.GetFiles(_dir, "*.csv")) names.Add(Path.GetFileName(path));
                foreach (string name in CsvLog.Expired(names, today, retentionDays)) File.Delete(Path.Combine(_dir, name));
            }
            catch (Exception ex)
            {
                Log.Write("CSV log: could not prune old files, " + ex.Message);
            }
        }

        private void Write(string path, string header, string row)
        {
            List<string> queue;
            if (!_pending.TryGetValue(path, out queue))
            {
                queue = new List<string>();
                _pending[path] = queue;
            }
            queue.Add(row);
            if (queue.Count > MaxPending) queue.RemoveRange(0, queue.Count - MaxPending);
            try
            {
                Directory.CreateDirectory(_dir);
                var text = new StringBuilder();
                if (!File.Exists(path)) text.Append(header).Append("\r\n");
                foreach (string r in queue) text.Append(r).Append("\r\n");
                File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
                queue.Clear();
                if (_failingPath == path) Log.Write("CSV log: writing " + Path.GetFileName(path) + " again");
                _failingPath = null;
            }
            catch (Exception ex)
            {
                if (_failingPath != path) Log.Write("CSV log: cannot write " + Path.GetFileName(path) + " (" + ex.Message + "), keeping rows until it can");
                _failingPath = path;
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `295 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/CsvLog.cs tests/CsvLogTests.cs tests/TestMain.cs && git commit -m "Add the CSV log: aggregation, daily files, retention, locked-file buffering

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Network details

**Files:**
- Create: `src\NetworkInfo.cs`, `tests\NetworkTests.cs`
- Modify: `tests\TestMain.cs`

**Interfaces:**
- Produces: `public sealed class AdapterInfo` — `string Kind; string WifiName; int? SignalPercent; string Ipv4; string Gateway;`; `internal static class NetworkInfo` — `List<AdapterInfo> Read()`, `bool IsVirtualAdapter(string description)`.

- [ ] **Step 1: Write the failing tests `tests\NetworkTests.cs` and register them**

```csharp
using System;

namespace DesktopMonitor.Tests
{
    internal static class NetworkTests
    {
        public static void Run()
        {
            VirtualAdapters_AreHidden();
            Read_ReturnsOnlyAdaptersWithAnAddress();
        }

        private static void VirtualAdapters_AreHidden()
        {
            TestMain.True(NetworkInfo.IsVirtualAdapter("VMware Virtual Ethernet Adapter for VMnet1"), "VMware");
            TestMain.True(NetworkInfo.IsVirtualAdapter("VirtualBox Host-Only Ethernet Adapter"), "VirtualBox");
            TestMain.True(NetworkInfo.IsVirtualAdapter("Hyper-V Virtual Ethernet Adapter"), "Hyper-V / WSL");
            TestMain.True(!NetworkInfo.IsVirtualAdapter("Intel(R) Wi-Fi 6 AX201 160MHz"), "real Wi-Fi");
            TestMain.True(!NetworkInfo.IsVirtualAdapter("Realtek USB GbE Family Controller"), "USB Ethernet to a board");
        }

        private static void Read_ReturnsOnlyAdaptersWithAnAddress()
        {
            bool allHaveIp = true;
            foreach (AdapterInfo a in NetworkInfo.Read()) if (string.IsNullOrEmpty(a.Ipv4)) allHaveIp = false;
            TestMain.True(allHaveIp, "every listed adapter has an IPv4 address (reads this PC without throwing)");
        }
    }
}
```

Register:

```csharp
            NetworkTests.Run();
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0103: The name 'NetworkInfo' does not exist in the current context`.

- [ ] **Step 3: Create `src\NetworkInfo.cs`**

The `WLAN_CONNECTION_ATTRIBUTES` offsets (SSID length 520, SSID 524, signal 576) were checked against `netsh wlan show interfaces` on this PC (same name, signal 87 %).

```csharp
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopMonitor
{
    // One connected adapter as shown in the panel's Network section.
    public sealed class AdapterInfo
    {
        public string Kind;          // "Wi-Fi", "Ethernet" or the adapter's own name
        public string WifiName;      // null when not Wi-Fi or when Windows hides it
        public int? SignalPercent;
        public string Ipv4;
        public string Gateway;
    }

    // Adapters that are up, not loopback/tunnel, and have an IPv4 address; Wi-Fi name and signal from the Native Wi-Fi API.
    internal static class NetworkInfo
    {
        private const int OpcodeCurrentConnection = 7;
        private const int InterfaceListHeader = 8, InterfaceInfoSize = 532;
        private const int OffSsidLength = 520, OffSsid = 524, OffSignal = 576;

        [DllImport("wlanapi.dll")]
        private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanQueryInterface(IntPtr handle, ref Guid interfaceGuid, int opCode, IntPtr reserved, out int dataSize, out IntPtr data, out int valueType);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr memory);

        public static List<AdapterInfo> Read()
        {
            Dictionary<Guid, KeyValuePair<string, int>> wifi = ReadWifi();
            var list = new List<AdapterInfo>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (IsVirtualAdapter(ni.Description)) continue;
                IPInterfaceProperties props = ni.GetIPProperties();
                string ip = null, gateway = null;
                foreach (UnicastIPAddressInformation a in props.UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork) { ip = a.Address.ToString(); break; }
                if (ip == null) continue;
                foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork) { gateway = g.Address.ToString(); break; }
                var info = new AdapterInfo { Ipv4 = ip, Gateway = gateway };
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    info.Kind = "Wi-Fi";
                    Guid id;
                    KeyValuePair<string, int> w;
                    if (Guid.TryParse(ni.Id, out id) && wifi.TryGetValue(id, out w))
                    {
                        info.WifiName = w.Key;
                        info.SignalPercent = w.Value;
                    }
                }
                else info.Kind = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : ni.Name;
                list.Add(info);
            }
            return list;
        }

        // VMware, VirtualBox and Hyper-V/WSL adapters all say "Virtual" in their description. Physical adapters without a
        // gateway (a cable straight to a board) are kept, because their IP is exactly what you need then.
        public static bool IsVirtualAdapter(string description)
        {
            return description != null && description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Interface GUID -> (network name, signal %), for connected Wi-Fi interfaces. Empty when the API is unavailable.
        private static Dictionary<Guid, KeyValuePair<string, int>> ReadWifi()
        {
            var result = new Dictionary<Guid, KeyValuePair<string, int>>();
            IntPtr handle, interfaces = IntPtr.Zero;
            uint version;
            try
            {
                if (WlanOpenHandle(2, IntPtr.Zero, out version, out handle) != 0) return result;
            }
            catch (DllNotFoundException)
            {
                return result; // no WLAN service on this machine
            }
            try
            {
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out interfaces) != 0) return result;
                int count = Marshal.ReadInt32(interfaces, 0);
                for (int i = 0; i < count; i++)
                {
                    var item = new IntPtr(interfaces.ToInt64() + InterfaceListHeader + i * InterfaceInfoSize);
                    var guid = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                    int size, type;
                    IntPtr data;
                    if (WlanQueryInterface(handle, ref guid, OpcodeCurrentConnection, IntPtr.Zero, out size, out data, out type) != 0) continue;
                    try
                    {
                        int ssidLength = Math.Min(32, Marshal.ReadInt32(data, OffSsidLength));
                        var ssid = new byte[ssidLength];
                        Marshal.Copy(new IntPtr(data.ToInt64() + OffSsid), ssid, 0, ssidLength);
                        int signal = Marshal.ReadInt32(data, OffSignal);
                        result[guid] = new KeyValuePair<string, int>(ssidLength > 0 ? Encoding.UTF8.GetString(ssid) : null, signal);
                    }
                    finally
                    {
                        WlanFreeMemory(data);
                    }
                }
            }
            finally
            {
                if (interfaces != IntPtr.Zero) WlanFreeMemory(interfaces);
                WlanCloseHandle(handle, IntPtr.Zero);
            }
            return result;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `301 passed, 0 failed`.

- [ ] **Step 5: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/NetworkInfo.cs tests/NetworkTests.cs tests/TestMain.cs && git commit -m "Add network details: adapters, IP, gateway, Wi-Fi name and signal

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Sampler readings and the background sampler thread

Readers are verified live; their decisions were unit-tested in Tasks 2–7.

**Files:**
- Modify: `src\MetricsSampler.cs` (full replacement), `tools\SamplerDump.cs` (full replacement)
- Create: `src\SamplerThread.cs`

**Interfaces:**
- Consumes: `Throttle` (2), `ProcessReader`, `ProcessTable` (3), `ComPortTracker`, `ComPortReader` (4), v2 `Snapshot` (5), `NetworkInfo` (7), `PowerMode` (1).
- Produces:
  - `MetricsSampler` fills every v2 `Snapshot` field: CPU limit and `Throttled` every 2 s, COM ports every 2 s (names via WMI on the thread pool when the set changes), top 5 apps every 3 s, battery with `Charging` and `BatteryMinutesLeft` every 10 s, `DiskFreeBytes` every 60 s.
  - `public sealed class PanelData` — `List<AdapterInfo> Adapters; PowerModeKind PowerMode;`.
  - `internal sealed class SamplerThread : IDisposable` — `SamplerThread(AppSettings, Dispatcher ui, Action<Snapshot> onSnapshot, Action<PanelData> onPanelData)`, `void Start()`, `void ApplySettings(AppSettings)`, `void RequestReset()`, `void SetPanelOpen(bool)`.

- [ ] **Step 1: Replace `src\MetricsSampler.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DesktopMonitor
{
    // Reads every metric (v1 spec section 4, v2 spec section 5). Each reader is isolated: a failure is logged once,
    // yields null until it recovers, and never escapes Sample(). Used from the sampler thread only.
    internal sealed class MetricsSampler : IDisposable
    {
        private const string ThermalCategory = "Thermal Zone Information";
        private const double TempEvery = 2, ComEvery = 2, ProcessEvery = 3, BatteryEvery = 10, DiskEvery = 60; // seconds

        private readonly HashSet<string> _failing = new HashSet<string>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private AppSettings _settings;

        private PerformanceCounter _cpu, _limit;
        private Dictionary<string, CounterSample> _gpuPrev;
        private Dictionary<string, long> _rxPrev, _txPrev; // per adapter Id
        private double _netPrevAt = -1;

        private PerformanceCounter _tCpu, _tSkin, _tBattery;
        private bool _thermalOpen, _thermalTenths;
        private double _tempAt = -1, _comAt = -1, _processAt = -1, _batteryAt = -1, _diskAt = -1;
        private double? _cpuTemp, _skinTemp, _batteryTemp, _battery, _disk, _diskFree, _cpuLimit;
        private bool _onAc, _charging;
        private int? _minutesLeft;
        private readonly Throttle _throttle = new Throttle();

        private ProcessReader _processReader;
        private List<ProcessSample> _processPrev;
        private double _processPrevAt;
        private List<AppUsage> _topApps = new List<AppUsage>();

        private readonly ComPortTracker _comTracker = new ComPortTracker();
        private List<ComPortInfo> _comPorts = new List<ComPortInfo>();
        private volatile Dictionary<string, string> _comNames; // latest finished WMI lookup
        private string _lookupFor;
        private int _lookupBusy;

        public MetricsSampler(AppSettings settings)
        {
            _settings = settings;
        }

        public void ApplySettings(AppSettings settings)
        {
            bool zonesChanged = settings.ZoneCpu != _settings.ZoneCpu || settings.ZoneSkin != _settings.ZoneSkin || settings.ZoneBattery != _settings.ZoneBattery;
            _settings = settings;
            if (zonesChanged) CloseThermal();
        }

        // After resume from sleep counters and baselines may be stale: rebuild everything on the next Sample().
        public void Reset()
        {
            CloseCpu();
            CloseLimit();
            _gpuPrev = null;
            _netPrevAt = -1;
            CloseThermal();
            _processPrev = null;
            _batteryAt = -1;
            _diskAt = -1;
        }

        public Snapshot Sample()
        {
            double now = _clock.Elapsed.TotalSeconds;
            DateTime wall = DateTime.Now;
            var s = new Snapshot { Time = wall };
            s.CpuPercent = Read("cpu", ReadCpu, CloseCpu);
            s.RamPercent = Read("ram", ReadRam, null);
            s.GpuPercent = Read("gpu", ReadGpu, delegate { _gpuPrev = null; });
            ReadNetwork(s, now);
            if (Due(ref _tempAt, now, TempEvery))
            {
                ReadTemps();
                _cpuLimit = Read("limit", ReadLimit, CloseLimit);
                _throttle.Feed(_cpuLimit);
            }
            if (Due(ref _comAt, now, ComEvery)) ReadComPorts(wall);
            if (Due(ref _processAt, now, ProcessEvery)) ReadProcesses(now);
            if (Due(ref _batteryAt, now, BatteryEvery)) ReadBattery();
            if (Due(ref _diskAt, now, DiskEvery)) _disk = Read("disk", ReadDisk, null);
            s.CpuTempC = _cpuTemp;
            s.SkinTempC = _skinTemp;
            s.BatteryTempC = _batteryTemp;
            s.CpuLimitPercent = _cpuLimit;
            s.Throttled = _throttle.Shown;
            s.BatteryPercent = _battery;
            s.OnAc = _onAc;
            s.Charging = _charging;
            s.BatteryMinutesLeft = _minutesLeft;
            s.DiskPercent = _disk;
            s.DiskFreeBytes = _diskFree;
            s.TopApps = _topApps;
            s.ComPorts = _comPorts;
            return s;
        }

        private static bool Due(ref double last, double now, double every)
        {
            if (last >= 0 && now - last < every) return false;
            last = now;
            return true;
        }

        private double? Read(string name, Func<double?> reader, Action reset)
        {
            try
            {
                double? v = reader();
                Ok(name);
                return v;
            }
            catch (Exception ex)
            {
                Fail(name, ex);
                if (reset != null) reset();
                return null;
            }
        }

        private void Ok(string name)
        {
            if (_failing.Remove(name)) Log.Write("Metrics: " + name + " recovered");
        }

        private void Fail(string name, Exception ex)
        {
            if (_failing.Add(name)) Log.Write("Metrics: " + name + " unavailable, " + ex.GetType().Name + ": " + ex.Message);
        }

        private double? ReadCpu()
        {
            if (_cpu == null)
            {
                _cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
                _cpu.NextValue(); // rate counter: the first read is always 0, so discard it
                return null;
            }
            return Rules.ClampPercent(_cpu.NextValue());
        }

        private void CloseCpu()
        {
            if (_cpu != null) _cpu.Dispose();
            _cpu = null;
        }

        // 100 = full speed; lower while the CPU is held back by heat or power limits.
        private double? ReadLimit()
        {
            if (_limit == null)
            {
                _limit = new PerformanceCounter("Processor Information", "% Performance Limit", "_Total", true);
                _limit.NextValue(); // discard the first read, as for the other counters
                return null;
            }
            return Rules.ClampPercent(_limit.NextValue());
        }

        private void CloseLimit()
        {
            if (_limit != null) _limit.Dispose();
            _limit = null;
        }

        private static double? ReadRam()
        {
            var m = new Native.MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (!Native.GlobalMemoryStatusEx(ref m)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return Rules.Percent(m.ullTotalPhys - m.ullAvailPhys, m.ullTotalPhys);
        }

        // One category read per tick (1-3 ms on this PC); utilisation is the rate between consecutive raw samples.
        private double? ReadGpu()
        {
            InstanceDataCollection data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
            var current = new Dictionary<string, CounterSample>();
            double sum = 0;
            foreach (InstanceData d in data.Values)
            {
                if (d.InstanceName.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                current[d.InstanceName] = d.Sample;
                CounterSample prev;
                if (_gpuPrev != null && _gpuPrev.TryGetValue(d.InstanceName, out prev)) sum += CounterSample.Calculate(prev, d.Sample);
            }
            bool primed = _gpuPrev != null;
            _gpuPrev = current;
            return primed ? Rules.ClampPercent(sum) : (double?)null;
        }

        private void ReadNetwork(Snapshot s, double now)
        {
            try
            {
                var rx = new Dictionary<string, long>();
                var tx = new Dictionary<string, long>();
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceStatistics st = ni.GetIPStatistics();
                    rx[ni.Id] = st.BytesReceived;
                    tx[ni.Id] = st.BytesSent;
                }
                if (_netPrevAt >= 0)
                {
                    s.DownBytesPerSec = Rules.AdapterRate(_rxPrev, rx, now - _netPrevAt);
                    s.UpBytesPerSec = Rules.AdapterRate(_txPrev, tx, now - _netPrevAt);
                }
                _rxPrev = rx;
                _txPrev = tx;
                _netPrevAt = now;
                Ok("net");
            }
            catch (Exception ex)
            {
                Fail("net", ex);
                _netPrevAt = -1;
            }
        }

        private void ReadTemps()
        {
            try
            {
                if (!_thermalOpen) OpenThermal();
                _cpuTemp = ReadZone(_tCpu);
                _skinTemp = ReadZone(_tSkin);
                _batteryTemp = ReadZone(_tBattery);
                Ok("temps");
            }
            catch (Exception ex)
            {
                Fail("temps", ex);
                CloseThermal();
                _cpuTemp = _skinTemp = _batteryTemp = null;
            }
        }

        private void OpenThermal()
        {
            string[] instances = new PerformanceCounterCategory(ThermalCategory).GetInstanceNames();
            _thermalTenths = PerformanceCounterCategory.CounterExists("High Precision Temperature", ThermalCategory);
            _tCpu = OpenZone(instances, _settings.ZoneCpu);
            _tSkin = OpenZone(instances, _settings.ZoneSkin);
            _tBattery = OpenZone(instances, _settings.ZoneBattery);
            _thermalOpen = true;
        }

        // Instances look like "\_TZ.CPUZ"; a zone that does not exist on this machine stays null and reads as "--".
        private PerformanceCounter OpenZone(string[] instances, string zone)
        {
            foreach (string inst in instances)
            {
                if (inst.EndsWith(zone, StringComparison.OrdinalIgnoreCase))
                    return new PerformanceCounter(ThermalCategory, _thermalTenths ? "High Precision Temperature" : "Temperature", inst, true);
            }
            return null;
        }

        private double? ReadZone(PerformanceCounter counter)
        {
            return counter == null ? (double?)null : Rules.ThermalToCelsius(counter.NextValue(), _thermalTenths);
        }

        private void CloseThermal()
        {
            foreach (PerformanceCounter c in new[] { _tCpu, _tSkin, _tBattery })
                if (c != null) c.Dispose();
            _tCpu = _tSkin = _tBattery = null;
            _thermalOpen = false;
            _tempAt = -1; // re-read on the next Sample()
        }

        private void ReadBattery()
        {
            try
            {
                PowerStatus p = SystemInformation.PowerStatus;
                _battery = Rules.BatteryPercent(p.BatteryLifePercent);
                _onAc = p.PowerLineStatus == PowerLineStatus.Online;
                _charging = (p.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
                _minutesLeft = p.BatteryLifeRemaining > 0 ? p.BatteryLifeRemaining / 60 : (int?)null;
                Ok("battery");
            }
            catch (Exception ex)
            {
                Fail("battery", ex);
                _battery = null;
                _minutesLeft = null;
            }
        }

        private double? ReadDisk()
        {
            var c = new DriveInfo("C");
            _diskFree = c.TotalFreeSpace;
            return Rules.Percent(c.TotalSize - c.TotalFreeSpace, c.TotalSize);
        }

        private void ReadProcesses(double now)
        {
            try
            {
                if (_processReader == null) _processReader = new ProcessReader();
                List<ProcessSample> current = _processReader.Read();
                if (_processPrev != null)
                {
                    List<AppUsage> apps = ProcessTable.Compare(_processPrev, current, now - _processPrevAt, Environment.ProcessorCount);
                    _topApps = apps.GetRange(0, Math.Min(5, apps.Count));
                }
                _processPrev = current;
                _processPrevAt = now;
                Ok("processes");
            }
            catch (Exception ex)
            {
                Fail("processes", ex);
                _processPrev = null;
                _topApps = new List<AppUsage>();
            }
        }

        // The port list is cheap (registry); device names need WMI (about 1 s), so that runs on the thread pool
        // whenever the set of ports changes and some of them have no name yet.
        private void ReadComPorts(DateTime now)
        {
            try
            {
                List<string> ports = ComPortReader.ReadPorts();
                _comTracker.Update(ports, now);
                Dictionary<string, string> names = _comNames;
                bool missing = false;
                foreach (string port in ports)
                {
                    string name;
                    if (names != null && names.TryGetValue(port, out name)) _comTracker.SetName(port, name);
                    else missing = true;
                }
                ports.Sort(ComPortTracker.ComparePorts);
                string key = string.Join(",", ports);
                if (missing && key != _lookupFor) StartNameLookup(key);
                _comPorts = _comTracker.Current(now);
                Ok("com");
            }
            catch (Exception ex)
            {
                Fail("com", ex);
            }
        }

        private void StartNameLookup(string key)
        {
            if (Interlocked.CompareExchange(ref _lookupBusy, 1, 0) != 0) return;
            _lookupFor = key;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    _comNames = ComPortReader.ReadFriendlyNames();
                }
                catch (Exception ex)
                {
                    Log.Write("Metrics: COM port names unavailable, " + ex.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _lookupBusy, 0);
                }
            });
        }

        public void Dispose()
        {
            CloseCpu();
            CloseLimit();
            CloseThermal();
            if (_processReader != null) _processReader.Dispose();
        }
    }
}
```

- [ ] **Step 2: Create `src\SamplerThread.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace DesktopMonitor
{
    // Data only the details panel needs; read every 5 s, and only while the panel is open.
    public sealed class PanelData
    {
        public List<AdapterInfo> Adapters = new List<AdapterInfo>();
        public PowerModeKind PowerMode;
    }

    // Runs MetricsSampler once a second on a background thread and posts each Snapshot to the UI thread,
    // so slow readers (network enumeration took up to ~400 ms in v1) never stall the card or the desktop.
    internal sealed class SamplerThread : IDisposable
    {
        private const double PanelEvery = 5; // seconds
        private readonly Dispatcher _ui;
        private readonly Action<Snapshot> _onSnapshot;
        private readonly Action<PanelData> _onPanelData;
        private readonly Thread _thread;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly object _gate = new object();
        private AppSettings _settings, _pendingSettings;
        private bool _resetRequested, _panelOpen, _panelDue;

        public SamplerThread(AppSettings settings, Dispatcher ui, Action<Snapshot> onSnapshot, Action<PanelData> onPanelData)
        {
            _settings = settings;
            _ui = ui;
            _onSnapshot = onSnapshot;
            _onPanelData = onPanelData;
            _thread = new Thread(Run) { IsBackground = true, Name = "Sampler" };
        }

        public void Start()
        {
            _thread.Start();
        }

        public void ApplySettings(AppSettings settings)
        {
            lock (_gate) _pendingSettings = settings;
        }

        public void RequestReset()
        {
            lock (_gate) _resetRequested = true;
        }

        // Opening the panel asks for panel data on the very next tick.
        public void SetPanelOpen(bool open)
        {
            lock (_gate)
            {
                _panelOpen = open;
                if (open) _panelDue = true;
            }
        }

        private void Run()
        {
            var clock = Stopwatch.StartNew();
            double nextTick = 0, nextPanel = 0;
            using (var sampler = new MetricsSampler(_settings))
            {
                while (true)
                {
                    double wait = Math.Max(0, nextTick - clock.Elapsed.TotalSeconds);
                    if (_stop.WaitOne(TimeSpan.FromSeconds(wait))) break;
                    nextTick = Math.Max(nextTick + 1, clock.Elapsed.TotalSeconds); // no burst of catch-up ticks after a stall
                    try
                    {
                        AppSettings pending;
                        bool reset, panelOpen, panelDue;
                        lock (_gate)
                        {
                            pending = _pendingSettings;
                            _pendingSettings = null;
                            reset = _resetRequested;
                            _resetRequested = false;
                            panelOpen = _panelOpen;
                            panelDue = _panelDue;
                            _panelDue = false;
                        }
                        if (pending != null) sampler.ApplySettings(pending);
                        if (reset) sampler.Reset();
                        _ui.BeginInvoke(_onSnapshot, sampler.Sample());
                        double now = clock.Elapsed.TotalSeconds;
                        if (panelOpen && (panelDue || now >= nextPanel))
                        {
                            nextPanel = now + PanelEvery;
                            var data = new PanelData();
                            try { data.Adapters = NetworkInfo.Read(); }
                            catch (Exception ex) { Log.Write("Panel: network details unavailable, " + ex.Message); }
                            data.PowerMode = PowerMode.Get();
                            _ui.BeginInvoke(_onPanelData, data);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Sampler thread: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }

        public void Dispose()
        {
            _stop.Set();
            if (_thread.IsAlive) _thread.Join(3000);
        }
    }
}
```

- [ ] **Step 3: Replace `tools\SamplerDump.cs`**

```csharp
using System;
using System.Threading;

namespace DesktopMonitor
{
    // Prints live readings once a second for comparison with Task Manager, netsh and ipconfig (v2 plan Tasks 7 and 8).
    //   SamplerDump.exe [seconds]   default 10
    internal static class SamplerDump
    {
        [STAThread]
        private static void Main(string[] args)
        {
            int seconds = args.Length > 0 ? int.Parse(args[0]) : 10;
            using (var sampler = new MetricsSampler(new AppSettings()))
            {
                for (int i = 0; i < seconds; i++)
                {
                    Snapshot s = sampler.Sample();
                    Console.WriteLine(s.Time.ToString("HH:mm:ss")
                        + "  cpu " + Rules.FormatPercent(s.CpuPercent)
                        + "  limit " + Rules.FormatPercent(s.CpuLimitPercent) + (s.Throttled ? " THROTTLED" : "")
                        + "  ram " + Rules.FormatPercent(s.RamPercent)
                        + "  gpu " + Rules.FormatPercent(s.GpuPercent)
                        + "  C: free " + Rules.FormatDiskFree(s.DiskFreeBytes)
                        + "  down " + Rules.FormatSpeed(s.DownBytesPerSec)
                        + "  up " + Rules.FormatSpeed(s.UpBytesPerSec)
                        + "  " + Rules.FormatBattery(s.BatteryPercent, s.OnAc, s.Charging, s.BatteryMinutesLeft)
                        + "  temps cpu " + Rules.FormatTemp(s.CpuTempC, true)
                        + " skin " + Rules.FormatTemp(s.SkinTempC, true)
                        + " battery " + Rules.FormatTemp(s.BatteryTempC, true));
                    Console.WriteLine("          top " + (s.TopApps.Count > 0 ? Rules.FormatTopApp(s.TopApps[0]) : "--")
                        + "  COM " + (s.ComPorts.Count == 0 ? "none" : string.Join(", ", s.ComPorts.ConvertAll(p => p.Port + (p.Name != null ? " " + p.Name : "") + (p.IsNew ? " (new)" : "")))));
                    Thread.Sleep(1000);
                }
            }
            foreach (AdapterInfo a in NetworkInfo.Read())
                Console.WriteLine("adapter " + a.Kind + ": " + (a.WifiName != null ? "wifi name present" : "no wifi name") + (a.SignalPercent.HasValue ? ", signal " + a.SignalPercent + "%" : "") + ", ip " + a.Ipv4 + ", gateway " + (a.Gateway ?? "none"));
            Console.WriteLine("power mode " + PowerMode.Name(PowerMode.Get()) + " (effective " + PowerMode.Name(PowerMode.GetEffective()) + ")");
        }
    }
}
```

- [ ] **Step 4: Build everything and run the tests**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Dump
```
Expected: `301 passed, 0 failed`; `Built bin\SamplerDump.exe`. (The v1 app still builds and runs unchanged: `Program.cs` calls `MetricsSampler.Sample()` on its timer until Task 10.)

- [ ] **Step 5: Read live values**

Run: `.\bin\SamplerDump.exe 10`
Expected, shape as below (values vary): the first line has `cpu --`, `limit --`, `gpu --`, `down --`, `top --` while counters prime; later lines have values; `limit 100%` unless the laptop is hot or on battery saver; `C: free` in GB; the battery text matches the plug state; after the loop, one `adapter` line per real adapter (no VMware ones) and the power mode.
```
07:17:10  cpu 69%  limit 100%  ram 77%  gpu 3%  C: free 43 GB  down 16 KB/s  up 6 KB/s  Plugged in · 100%  temps cpu 57°C skin 41°C battery 32°C
          top svchost · 7% CPU · 322 MB  COM none
```

- [ ] **Step 6: Cross-check the top app**

While `.\bin\SamplerDump.exe 15` runs, ask the user to compare its `top` line with Task Manager → Processes sorted by CPU (grouped app names, e.g. all Brave processes as one): same app at the top, CPU within a few %.

- [ ] **Step 7: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/MetricsSampler.cs src/SamplerThread.cs tools/SamplerDump.cs && git commit -m "Sample CPU limit, top apps, COM ports and battery time on a background thread

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: The v2 card

**Files:**
- Create: `src\Drawing.cs`
- Modify: `src\CardView.cs`, `src\CardWindow.cs`, `src\DesktopPin.cs` (full replacements)

**Interfaces:**
- Consumes: v2 `Snapshot` (5), `Rules` (2–3), `History` data (2), `DesktopPin` (v1).
- Produces:
  - `internal static class Palette` — brushes `Text, Label, Ok, Amber, Red, RedTint, OkTint, NewTint, Segment`, pen `Divider`, `Brush Colour(Level, Brush okBrush)`, `Brush Solid(byte a, byte r, byte g, byte b)`, `Pen Frozen(Brush, double)`.
  - `internal sealed class TextPainter` — `double PixelsPerDip`, `Make`, `Width`, `Left`, `Right`, `Centered`, `string Fit(string s, double maxWidth, double size)`.
  - `CardView` — adds `void SetHistory(double?[] points, double? peak, int capacity)`; `CardHeight(false) == 282`, `CardHeight(true) == 304`.
  - `CardWindow` — adds `Box Bounds`, `static List<Box> WorkAreas()`, `static Box WorkAreaContaining(Box)`; re-checks placement on display change; stops its pin watchdog when closed.
  - `DesktopPin.Stop()`.

- [ ] **Step 1: Create `src\Drawing.cs`**

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Colours shared by the card and the details panel (v1 spec section 3, v2 spec sections 3-4).
    internal static class Palette
    {
        public static readonly Brush Text = Solid(255, 0xEC, 0xEC, 0xEC);
        public static readonly Brush Label = Solid(255, 0xA3, 0xA3, 0xA3);
        public static readonly Brush Ok = Solid(255, 0x5D, 0xCA, 0xA5);
        public static readonly Brush Amber = Solid(255, 0xFA, 0xC7, 0x75);
        public static readonly Brush Red = Solid(255, 0xF0, 0x95, 0x95);
        public static readonly Brush RedTint = Solid(46, 0xF0, 0x95, 0x95);   // 18 %
        public static readonly Brush OkTint = Solid(64, 0x5D, 0xCA, 0xA5);    // 25 %
        public static readonly Brush NewTint = Solid(46, 0x5D, 0xCA, 0xA5);   // 18 %
        public static readonly Brush Segment = Solid(20, 255, 255, 255);      // 8 %
        public static readonly Pen Divider = Frozen(Solid(31, 255, 255, 255), 1); // 12 %

        public static Brush Colour(Level level, Brush okBrush)
        {
            switch (level)
            {
                case Level.Ok: return okBrush;
                case Level.Amber: return Amber;
                case Level.Red: return Red;
                default: return Label;
            }
        }

        public static Brush Solid(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        public static Pen Frozen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
    }

    // Segoe UI text at the element's DPI, plus left/right/centred drawing and ellipsis fitting.
    internal sealed class TextPainter
    {
        private static readonly Typeface Face = new Typeface("Segoe UI");
        public double PixelsPerDip = 1.0;

        public FormattedText Make(string s, double size, Brush brush)
        {
            return new FormattedText(s ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, PixelsPerDip);
        }

        public double Width(string s, double size)
        {
            return Make(s, size, Palette.Text).WidthIncludingTrailingWhitespace;
        }

        public void Left(DrawingContext dc, string s, double size, Brush brush, double x, double y)
        {
            dc.DrawText(Make(s, size, brush), new Point(x, y));
        }

        public void Right(DrawingContext dc, string s, double size, Brush brush, double right, double y)
        {
            FormattedText t = Make(s, size, brush);
            dc.DrawText(t, new Point(right - t.Width, y));
        }

        public void Centered(DrawingContext dc, string s, double size, Brush brush, double cx, double y)
        {
            FormattedText t = Make(s, size, brush);
            dc.DrawText(t, new Point(cx - t.Width / 2, y));
        }

        // Shortens the text with "…" until it fits maxWidth.
        public string Fit(string s, double maxWidth, double size)
        {
            if (s == null || Width(s, size) <= maxWidth) return s;
            for (int n = s.Length - 1; n > 0; n--)
            {
                string candidate = s.Substring(0, n).TrimEnd() + "\u2026";
                if (Width(candidate, size) <= maxWidth) return candidate;
            }
            return "\u2026";
        }
    }
}
```

- [ ] **Step 2: Replace `src\CardView.cs`**

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Draws the whole gauge card from the latest Snapshot (v1 spec section 3, v2 spec section 3).
    internal sealed class CardView : FrameworkElement
    {
        public const double CardWidth = 250;
        private const double Pad = 14, RingSize = 64, RingRadius = 25, RingStroke = 6;
        private const double ColGap = 14, RowHeight = 16, RowGap = 6, GraphHeight = 30, GraphMin = 40, GraphMax = 100;

        private static readonly Pen TrackPen = Palette.Frozen(Palette.Solid(33, 255, 255, 255), RingStroke); // white at 13 %
        private static readonly Pen OutlinePen = DashedPen(Brushes.White, 1);
        private static readonly Pen ThresholdPen = DashedPen(Palette.Solid(128, 0xFA, 0xC7, 0x75), 1); // amber at 50 %

        private readonly TextPainter _text = new TextPainter();
        private Snapshot _snap = new Snapshot();
        private AppSettings _settings = new AppSettings();
        private bool _unlocked;
        private double?[] _history = new double?[0];
        private double? _peak;
        private int _historyCapacity = 60;

        public CardView()
        {
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale); // ClearType cannot render on a transparent window
            Loaded += delegate { _text.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; InvalidateVisual(); };
        }

        public bool Unlocked
        {
            get { return _unlocked; }
            set { _unlocked = value; InvalidateMeasure(); InvalidateVisual(); }
        }

        public void Update(Snapshot s)
        {
            _snap = s ?? new Snapshot();
            InvalidateVisual();
        }

        public void SetHistory(double?[] points, double? peak, int capacity)
        {
            _history = points ?? new double?[0];
            _peak = peak;
            _historyCapacity = Math.Max(2, capacity);
            InvalidateVisual();
        }

        public void ApplySettings(AppSettings s)
        {
            _settings = s;
            InvalidateVisual();
        }

        public static double CardHeight(bool unlocked)
        {
            return unlocked ? 304 : 282;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(CardWidth, CardHeight(_unlocked));
        }

        protected override void OnRender(DrawingContext dc)
        {
            Snapshot s = _snap;
            AppSettings st = _settings;
            double height = CardHeight(_unlocked);
            var background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(st.Opacity * 255), 0x14, 0x14, 0x16));
            dc.DrawRoundedRectangle(background, null, new Rect(0, 0, CardWidth, height), 10, 10);
            if (_unlocked) dc.DrawRoundedRectangle(null, OutlinePen, new Rect(1.5, 1.5, CardWidth - 3, height - 3), 9, 9);

            double y = Pad;
            _text.Left(dc, "System", 12, Palette.Label, Pad, y);
            FormattedText clock = _text.Make(s.Time.ToString("HH:mm", CultureInfo.InvariantCulture), 12, Palette.Label);
            dc.DrawText(clock, new Point(CardWidth - Pad - clock.Width, y));
            if (s.Throttled) DrawPill(dc, "Throttled " + Rules.FormatPercent(s.CpuLimitPercent), CardWidth - Pad - clock.Width - 6, y);
            y += RowHeight + 8;

            double cpuFraction = s.CpuPercent.HasValue ? s.CpuPercent.Value / 100 : 0;
            double ramFraction = s.RamPercent.HasValue ? s.RamPercent.Value / 100 : 0;
            DrawRing(dc, Pad + RingSize / 2, y, cpuFraction, Rules.FormatPercent(s.CpuPercent), "CPU", Rules.Classify(s.CpuPercent, st.UsageAmber, st.UsageRed));
            DrawRing(dc, CardWidth / 2, y, ramFraction, Rules.FormatPercent(s.RamPercent), "RAM", Rules.Classify(s.RamPercent, st.UsageAmber, st.UsageRed));
            DrawRing(dc, CardWidth - Pad - RingSize / 2, y, Rules.TempRingFraction(s.CpuTempC, st.TempRingMin, st.TempRingMax),
                Rules.FormatTemp(s.CpuTempC, false), "CPU temp", Rules.Classify(s.CpuTempC, st.TempAmber, st.TempRed));
            y += RingSize + 2 + RowHeight + 8;

            _text.Left(dc, "CPU temp \u00B7 10 min", 12, Palette.Label, Pad, y);
            FormattedText peak = _text.Make(Rules.FormatTemp(_peak, false), 12, Palette.Colour(Rules.Classify(_peak, st.TempAmber, st.TempRed), Palette.Ok));
            dc.DrawText(peak, new Point(CardWidth - Pad - peak.Width, y));
            _text.Right(dc, "peak", 12, Palette.Label, CardWidth - Pad - peak.Width - 4, y); // explicit gap: FormattedText.Width ignores a trailing space
            y += RowHeight + 3;
            DrawHistory(dc, Pad, y, CardWidth - 2 * Pad, st);
            y += GraphHeight + 6;

            AppUsage top = s.TopApps.Count > 0 ? s.TopApps[0] : null;
            double labelW = _text.Width("Top app", 12);
            _text.Left(dc, "Top app", 12, Palette.Label, Pad, y);
            _text.Right(dc, _text.Fit(Rules.FormatTopApp(top), CardWidth - 2 * Pad - labelW - 8, 12), 12, top == null ? Palette.Label : Palette.Text, CardWidth - Pad, y);
            y += RowHeight + 9;

            double colW = (CardWidth - 2 * Pad - ColGap) / 2;
            double x1 = Pad, x2 = Pad + colW + ColGap;
            Cell(dc, x1, y, colW, "GPU", Rules.FormatPercent(s.GpuPercent), Palette.Colour(Rules.Classify(s.GpuPercent, st.UsageAmber, st.UsageRed), Palette.Text));
            Cell(dc, x2, y, colW, "C: free", Rules.FormatDiskFree(s.DiskFreeBytes), Palette.Colour(Rules.Classify(s.DiskPercent, st.DiskAmber, st.DiskRed), Palette.Text));
            y += RowHeight + RowGap;
            Cell(dc, x1, y, colW, "Net \u2193", Rules.FormatSpeed(s.DownBytesPerSec), s.DownBytesPerSec.HasValue ? Palette.Text : Palette.Label);
            Cell(dc, x2, y, colW, "Net \u2191", Rules.FormatSpeed(s.UpBytesPerSec), s.UpBytesPerSec.HasValue ? Palette.Text : Palette.Label);
            y += RowHeight + RowGap;
            BatteryCell(dc, x1, y, colW, s, st);
            Cell(dc, x2, y, colW, "Skin", Rules.FormatTemp(s.SkinTempC, true), Palette.Colour(Rules.Classify(s.SkinTempC, st.TempAmber, st.TempRed), Palette.Ok));

            if (_unlocked) _text.Centered(dc, "Drag to move \u00B7 locks when you let go", 11, Palette.Text, CardWidth / 2, 280);
        }

        // Rounded red pill whose right edge is at `right`.
        private void DrawPill(DrawingContext dc, string text, double right, double y)
        {
            FormattedText t = _text.Make(text, 11, Palette.Red);
            double w = t.Width + 12;
            dc.DrawRoundedRectangle(Palette.RedTint, null, new Rect(right - w, y + 1, w, 15), 6, 6);
            dc.DrawText(t, new Point(right - w + 6, y + 1));
        }

        // Last 10 minutes of CPU temperature, newest at the right edge; gaps where a reading was missing.
        private void DrawHistory(DrawingContext dc, double x0, double y0, double w, AppSettings st)
        {
            double ty = GraphY(st.TempAmber, y0);
            dc.DrawLine(ThresholdPen, new Point(x0, ty), new Point(x0 + w, ty));
            int n = _history.Length;
            if (n == 0) return;
            Level level = Rules.Classify(_peak, st.TempAmber, st.TempRed);
            Brush line = Palette.Colour(level, Palette.Ok);
            Brush fill = FillFor(level);
            var pen = new Pen(line, 1.5);
            double step = w / (_historyCapacity - 1);
            int i = 0;
            while (i < n)
            {
                if (!_history[i].HasValue) { i++; continue; }
                int start = i;
                while (i < n && _history[i].HasValue) i++;
                DrawSegment(dc, start, i - 1, n, x0 + w, step, y0, pen, fill);
            }
        }

        private void DrawSegment(DrawingContext dc, int first, int last, int n, double right, double step, double y0, Pen pen, Brush fill)
        {
            Func<int, Point> at = k => new Point(right - (n - 1 - k) * step, GraphY(_history[k].Value, y0));
            if (first == last)
            {
                dc.DrawEllipse(pen.Brush, null, at(first), 1.5, 1.5);
                return;
            }
            var area = new StreamGeometry();
            using (StreamGeometryContext ctx = area.Open())
            {
                ctx.BeginFigure(new Point(at(first).X, y0 + GraphHeight), true, true);
                for (int k = first; k <= last; k++) ctx.LineTo(at(k), false, false);
                ctx.LineTo(new Point(at(last).X, y0 + GraphHeight), false, false);
            }
            area.Freeze();
            dc.DrawGeometry(fill, null, area);
            var curve = new StreamGeometry();
            using (StreamGeometryContext ctx = curve.Open())
            {
                ctx.BeginFigure(at(first), false, false);
                for (int k = first + 1; k <= last; k++) ctx.LineTo(at(k), true, false);
            }
            curve.Freeze();
            dc.DrawGeometry(null, pen, curve);
        }

        private static double GraphY(double celsius, double y0)
        {
            double c = Math.Max(GraphMin, Math.Min(GraphMax, celsius));
            return y0 + GraphHeight - (c - GraphMin) / (GraphMax - GraphMin) * GraphHeight;
        }

        private static Brush FillFor(Level level)
        {
            switch (level)
            {
                case Level.Red: return Palette.Solid(38, 0xF0, 0x95, 0x95);
                case Level.Amber: return Palette.Solid(38, 0xFA, 0xC7, 0x75);
                default: return Palette.Solid(38, 0x5D, 0xCA, 0xA5); // 15 %
            }
        }

        private void DrawRing(DrawingContext dc, double cx, double top, double fraction, string value, string label, Level level)
        {
            var center = new Point(cx, top + RingSize / 2);
            dc.DrawEllipse(null, TrackPen, center, RingRadius, RingRadius);
            if (fraction > 0)
            {
                var pen = new Pen(Palette.Colour(level, Palette.Ok), RingStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (fraction >= 0.999) dc.DrawEllipse(null, pen, center, RingRadius, RingRadius);
                else dc.DrawGeometry(null, pen, Arc(center, RingRadius, fraction));
            }
            FormattedText v = _text.Make(value, 13, Palette.Text);
            dc.DrawText(v, new Point(cx - v.Width / 2, center.Y - v.Height / 2));
            _text.Centered(dc, label, 12, Palette.Label, cx, top + RingSize + 2);
        }

        // Clockwise arc starting at 12 o'clock.
        private static Geometry Arc(Point c, double r, double fraction)
        {
            double a0 = -Math.PI / 2, a1 = a0 + fraction * 2 * Math.PI;
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false, false);
                ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        // "Battery   99% [bolt] 39°": percentage, AC bolt when plugged in, battery temperature coloured by the temp rule.
        private void BatteryCell(DrawingContext dc, double x, double y, double w, Snapshot s, AppSettings st)
        {
            _text.Left(dc, "Battery", 12, Palette.Label, x, y);
            double right = x + w;
            FormattedText temp = _text.Make(Rules.FormatTemp(s.BatteryTempC, false), 12, Palette.Colour(Rules.Classify(s.BatteryTempC, st.TempAmber, st.TempRed), Palette.Ok));
            right -= temp.Width;
            dc.DrawText(temp, new Point(right, y));
            if (s.OnAc)
            {
                right -= 4 + 7;
                dc.DrawGeometry(Palette.Text, null, Bolt(right, y + 2.5));
            }
            FormattedText pct = _text.Make(Rules.FormatPercent(s.BatteryPercent), 12, s.BatteryPercent.HasValue ? Palette.Text : Palette.Label);
            right -= 4 + pct.Width;
            dc.DrawText(pct, new Point(right, y));
        }

        // 7 x 11 lightning bolt with its top-left corner at (x, y).
        private static Geometry Bolt(double x, double y)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x + 4, y), true, true);
                ctx.PolyLineTo(new[]
                {
                    new Point(x, y + 6), new Point(x + 3, y + 6), new Point(x + 2, y + 11),
                    new Point(x + 7, y + 4.5), new Point(x + 4, y + 4.5), new Point(x + 5, y)
                }, true, true);
            }
            g.Freeze();
            return g;
        }

        private void Cell(DrawingContext dc, double x, double y, double w, string label, string value, Brush valueBrush)
        {
            _text.Left(dc, label, 12, Palette.Label, x, y);
            _text.Right(dc, value, 12, valueBrush, x + w, y);
        }

        private static Pen DashedPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            pen.Freeze();
            return pen;
        }
    }
}
```

- [ ] **Step 3: Replace `src\CardWindow.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace DesktopMonitor
{
    // Borderless transparent window hosting the card (v1 spec section 5): click-through while locked,
    // draggable while unlocked, pinned to the desktop layer.
    internal sealed class CardWindow : Window
    {
        private const double EdgeMargin = 12;
        private readonly CardView _card = new CardView();
        private readonly bool _useOwner;
        private IntPtr _hwnd;
        private DesktopPin _pin;

        public event Action<double, double> PositionCommitted;
        public event Action<bool> UnlockedChanged;

        public CardWindow(AppSettings settings, bool useOwner)
        {
            _useOwner = useOwner;
            Title = "Desktop Monitor";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = _card;
            _card.ApplySettings(settings);
            _card.MouseLeftButtonDown += OnCardMouseDown;
            Place(settings);
            SourceInitialized += OnSourceInitialized;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Closed += delegate
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                if (_pin != null) _pin.Stop(); // a recreated card must not leave the old watchdog running
            };
        }

        public CardView Card { get { return _card; } }

        public bool Unlocked { get { return _card.Unlocked; } }

        public Box Bounds { get { return new Box(Left, Top, CardView.CardWidth, CardView.CardHeight(_card.Unlocked)); } }

        public void ApplySettings(AppSettings settings)
        {
            _card.ApplySettings(settings);
        }

        public void SetUnlocked(bool unlocked)
        {
            if (_card.Unlocked == unlocked) return;
            _card.Unlocked = unlocked;
            SetClickThrough(!unlocked);
            Action<bool> handler = UnlockedChanged;
            if (handler != null) handler(unlocked);
        }

        // Work areas of all screens in DIPs (the app is system-DPI aware).
        public static List<Box> WorkAreas()
        {
            double scale = SystemDpiScale();
            var areas = new List<Box>();
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                System.Drawing.Rectangle r = screen.WorkingArea;
                areas.Add(new Box(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale));
            }
            return areas;
        }

        // The work area holding the centre of `b`, or the primary one.
        public static Box WorkAreaContaining(Box b)
        {
            double cx = b.X + b.W / 2, cy = b.Y + b.H / 2;
            foreach (Box wa in WorkAreas())
                if (cx >= wa.X && cx < wa.X + wa.W && cy >= wa.Y && cy < wa.Y + wa.H) return wa;
            Rect p = SystemParameters.WorkArea;
            return new Box(p.X, p.Y, p.Width, p.Height);
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            _pin = new DesktopPin(_hwnd, _useOwner);
            _pin.Attach();
        }

        private void SetClickThrough(bool on)
        {
            if (_hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = on ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        }

        // DragMove returns when the button is released; that is when the position is saved and the card re-locks.
        private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_card.Unlocked) return;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                return; // button already released
            }
            Action<double, double> handler = PositionCommitted;
            if (handler != null) handler(Left, Top);
            SetUnlocked(false);
        }

        // A projector unplugged or a resolution change while running: bring the card back on screen without
        // overwriting the saved position, so it returns there when the old display comes back and the app restarts.
        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (Rules.IsMostlyOnScreen(Bounds, WorkAreas())) return;
                Rect wa = SystemParameters.WorkArea;
                Box p = Rules.DefaultPlacement(new Box(wa.X, wa.Y, wa.Width, wa.Height), CardView.CardWidth, CardView.CardHeight(false), EdgeMargin);
                Left = p.X;
                Top = p.Y;
            }));
        }

        private void Place(AppSettings s)
        {
            var saved = new Box(s.X ?? 0, s.Y ?? 0, CardView.CardWidth, CardView.CardHeight(false));
            if (s.X.HasValue && s.Y.HasValue && Rules.IsMostlyOnScreen(saved, WorkAreas()))
            {
                Left = s.X.Value;
                Top = s.Y.Value;
                return;
            }
            Rect wa = SystemParameters.WorkArea; // primary screen, in DIPs
            Box p = Rules.DefaultPlacement(new Box(wa.X, wa.Y, wa.Width, wa.Height), CardView.CardWidth, CardView.CardHeight(false), EdgeMargin);
            Left = p.X;
            Top = p.Y;
        }

        private static double SystemDpiScale()
        {
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                return g.DpiX / 96.0;
        }
    }
}
```

- [ ] **Step 4: Replace `src\DesktopPin.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopMonitor
{
    // Keeps a window on the desktop layer: owned by the window that hosts the desktop icons (owner mode)
    // and always placed at the bottom of the z-order. Re-attaches after Explorer restarts.
    internal sealed class DesktopPin
    {
        private readonly IntPtr _hwnd;
        private readonly bool _useOwner;
        private readonly uint _taskbarCreated;
        private readonly SafeTimer _watchdog;
        private IntPtr _host;

        public DesktopPin(IntPtr hwnd, bool useOwner)
        {
            _hwnd = hwnd;
            _useOwner = useOwner;
            _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            // Covers Explorer not being ready at login and a missed TaskbarCreated broadcast.
            _watchdog = new SafeTimer(TimeSpan.FromSeconds(5), "DesktopPin watchdog", delegate
            {
                if (_useOwner && !Native.IsWindow(_host)) Attach();
            });
            _watchdog.Start();
        }

        public IntPtr Host { get { return _host; } }

        public void Stop()
        {
            _watchdog.Stop();
        }

        public void Attach()
        {
            if (_useOwner)
            {
                _host = FindDesktopHost();
                if (_host == IntPtr.Zero) Log.Write("DesktopPin: desktop host not found yet, retrying in 5 s");
                else Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, _host);
            }
            if (!Native.IsWindowVisible(_hwnd)) Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // The desktop icons live in SHELLDLL_DefView, hosted either by Progman or by a top-level WorkerW.
        internal static IntPtr FindDesktopHost()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero && Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return progman;
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr h, IntPtr lParam)
            {
                if (Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                var pos = (Native.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                if ((pos.flags & Native.SWP_NOZORDER) == 0 && pos.hwndInsertAfter != Native.HWND_BOTTOM)
                {
                    pos.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            else if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
            {
                Log.Write("DesktopPin: Explorer restarted, re-attaching");
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(Attach));
            }
            return IntPtr.Zero;
        }
    }
}
```

- [ ] **Step 5: Build and run the tests**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
Stop-Process -Name DesktopMonitor -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```
Expected: `301 passed, 0 failed`; `Built bin\DesktopMonitor.exe`.

- [ ] **Step 6: Look at the card**

Run `Start-Process .\bin\DesktopMonitor.exe`, then ask the user to press Win+D. Expected: the card is taller (282 px) with the "CPU temp · 10 min" label and dashed 80° line (the graph itself stays empty until Task 10 feeds it), a "Top app" row with a real app name, "C: free NN GB", and a red "Throttled NN%" pill only while the CPU is limited. Everything from v1 still works (rings, grid, tray icon). Leave the app running.

- [ ] **Step 7: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/Drawing.cs src/CardView.cs src/CardWindow.cs src/DesktopPin.cs && git commit -m "Draw the v2 card: throttling pill, temperature history, top app, disk free

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Details panel, tray additions and app wiring

**Files:**
- Create: `src\DetailsPanel.cs`
- Modify: `src\TrayIcon.cs`, `src\SafeTimer.cs`, `src\Program.cs` (full replacements)

**Interfaces:**
- Consumes: everything above; the decision in `spike\POWER-RESULT.md`.
- Produces: the finished app.
  - `PanelView` — `const double PanelWidth = 300`, `void Update(Snapshot, PanelData, List<Alert>, AppSettings, bool powerSwitch)`, `void ShowCopied(string)`, events `CopyRequested(string)`, `PowerModeRequested(PowerModeKind)`, `LogToggleRequested`, `OpenLogFolderRequested`, `TaskManagerRequested`, `PowerSettingsRequested`.
  - `DetailsPanel : Window` — `PanelView View`, `DateTime LastAutoHide`, `void ShowBeside(Box card, Box workArea)`.
  - `TrayIcon` — adds `event Action DetailsRequested` (menu "Details" and left double-click), `void ShowAlert(Alert)`.
  - `static class Guard` — `void Run(string name, Action action)`.
  - `Program.PowerSwitchAvailable`.

- [ ] **Step 1: Create `src\DetailsPanel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Draws the details panel (v2 spec section 4). One Layout pass both measures and draws, and records the
    // clickable areas, so what you click is always what you see.
    internal sealed class PanelView : FrameworkElement
    {
        public const double PanelWidth = 300;
        private const double PadX = 14, PadY = 12, RowH = 16, Inner = PanelWidth - 2 * PadX;
        private const int AlertsShown = 5; // keeps the panel inside a 720 px work area
        private static readonly Brush Background = Palette.Solid(250, 0x1C, 0x1C, 0x1F);
        private static readonly Pen Border = Palette.Frozen(Palette.Solid(36, 255, 255, 255), 1);
        private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(1.5);

        private readonly TextPainter _text = new TextPainter();
        private readonly List<KeyValuePair<Rect, Action>> _hits = new List<KeyValuePair<Rect, Action>>();
        private Snapshot _snap = new Snapshot();
        private PanelData _data;
        private List<Alert> _alerts = new List<Alert>();
        private AppSettings _settings = new AppSettings();
        private bool _powerSwitch;
        private string _copied;
        private DateTime _copiedAt;

        public event Action<string> CopyRequested;
        public event Action<PowerModeKind> PowerModeRequested;
        public event Action LogToggleRequested;
        public event Action OpenLogFolderRequested;
        public event Action TaskManagerRequested;
        public event Action PowerSettingsRequested;

        public PanelView()
        {
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            Loaded += delegate { _text.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; InvalidateVisual(); };
        }

        public void Update(Snapshot s, PanelData data, List<Alert> alerts, AppSettings settings, bool powerSwitch)
        {
            _snap = s ?? new Snapshot();
            _data = data;
            _alerts = alerts ?? new List<Alert>();
            _settings = settings;
            _powerSwitch = powerSwitch;
            InvalidateMeasure();
            InvalidateVisual();
        }

        // Shows "Copied" in place of the copied text for 1.5 s.
        public void ShowCopied(string text)
        {
            _copied = text;
            _copiedAt = DateTime.Now;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(PanelWidth, Layout(null));
        }

        protected override void OnRender(DrawingContext dc)
        {
            double h = Layout(null);
            dc.DrawRoundedRectangle(Background, Border, new Rect(0.5, 0.5, PanelWidth - 1, h - 1), 10, 10);
            Layout(dc);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(this);
            foreach (KeyValuePair<Rect, Action> hit in _hits)
            {
                if (!hit.Key.Contains(p)) continue;
                hit.Value();
                e.Handled = true;
                return;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            bool over = _hits.Exists(h => h.Key.Contains(p));
            Cursor = over ? Cursors.Hand : Cursors.Arrow;
        }

        // Measures (dc == null) or draws the panel; returns its height.
        private double Layout(DrawingContext dc)
        {
            if (dc != null) _hits.Clear();
            Snapshot s = _snap;
            AppSettings st = _settings;
            double y = PadY;
            if (dc != null)
            {
                _text.Left(dc, "Details", 13, Palette.Text, PadX, y);
                _text.Right(dc, "Esc to close", 12, Palette.Label, PanelWidth - PadX, y + 1);
            }
            y += 20;

            y = Section(dc, y, "CPU speed");
            string speed = Rules.FormatCpuSpeed(s.CpuLimitPercent, Throttle.Reason(s.CpuTempC, st.TempRed, s.OnAc));
            Brush speedBrush = !s.CpuLimitPercent.HasValue ? Palette.Label : Rules.IsBelowFullSpeed(s.CpuLimitPercent.Value) ? Palette.Red : Palette.Ok;
            if (dc != null) _text.Left(dc, speed, 12, speedBrush, PadX, y);
            y += RowH;

            y = Section(dc, y, "Top apps");
            if (s.TopApps.Count == 0) y = Note(dc, y, "--");
            foreach (AppUsage app in s.TopApps)
                y = Row(dc, y, app.Name, Rules.FormatPercent(app.CpuPercent) + " \u00B7 " + Rules.FormatMemory(app.PrivateBytes), Palette.Text, null);

            y = Section(dc, y, "Network");
            if (_data == null) y = Note(dc, y, "Reading\u2026");
            else if (_data.Adapters.Count == 0) y = Note(dc, y, "Not connected");
            else
            {
                foreach (AdapterInfo a in _data.Adapters)
                {
                    string kindValue = a.Kind == "Wi-Fi"
                        ? (a.WifiName ?? "name hidden by Windows") + (a.SignalPercent.HasValue ? " \u00B7 " + a.SignalPercent.Value + "%" : "")
                        : "connected";
                    y = Row(dc, y, a.Kind, kindValue, Palette.Text, null);
                    string ip = a.Ipv4;
                    y = Row(dc, y, "IP", IsCopied(ip) ? "Copied" : ip, Palette.Ok, delegate { Raise(CopyRequested, ip); });
                    y = Row(dc, y, "Gateway", a.Gateway ?? "none", a.Gateway == null ? Palette.Label : Palette.Text, null);
                }
            }

            y = Section(dc, y, "COM ports");
            if (s.ComPorts.Count == 0) y = Note(dc, y, "None connected");
            foreach (ComPortInfo port in s.ComPorts)
            {
                if (dc != null && port.IsNew) dc.DrawRoundedRectangle(Palette.NewTint, null, new Rect(PadX - 4, y - 1, Inner + 8, RowH), 4, 4);
                string name = (port.Name ?? "\u2026") + (port.IsNew ? " \u00B7 new" : "");
                string portName = port.Port;
                y = RowClickableLabel(dc, y, IsCopied(portName) ? "Copied" : portName, _text.Fit(name, Inner - 60, 12), delegate { Raise(CopyRequested, portName); });
            }

            y = Section(dc, y, "Alerts");
            if (_alerts.Count == 0) y = Note(dc, y, "No alerts today");
            for (int i = 0; i < Math.Min(AlertsShown, _alerts.Count); i++)
                y = Row(dc, y, _alerts[i].Time.ToString("HH:mm", CultureInfo.InvariantCulture), _alerts[i].Title, Palette.Colour(_alerts[i].Severity, Palette.Ok), null);
            if (_alerts.Count > AlertsShown) y = Note(dc, y, "+" + (_alerts.Count - AlertsShown) + " earlier today (all in alerts.csv)");

            y = Section(dc, y, "Power mode");
            PowerModeKind mode = _data == null ? PowerModeKind.Unknown : _data.PowerMode;
            if (_powerSwitch) y = Segments(dc, y, mode);
            else
            {
                y = Row(dc, y, "Current", PowerMode.Name(mode), Palette.Text, null);
                y = Link(dc, y, "Open power settings \u2197", delegate { Raise(PowerSettingsRequested); });
            }

            y = Section(dc, y, "Battery");
            if (dc != null) _text.Left(dc, Rules.FormatBattery(s.BatteryPercent, s.OnAc, s.Charging, s.BatteryMinutesLeft), 12, Palette.Text, PadX, y);
            y += RowH;

            // Footer on two rows (one row does not fit 272 px): log toggle, then the two links.
            y = Section(dc, y, "CSV log");
            string log = st.LogEnabled ? "On \u00B7 every " + Rules.FormatDuration(st.LogIntervalSeconds) + " \u00B7 click to turn off" : "Off \u00B7 click to turn on";
            y = Link(dc, y, log, delegate { Raise(LogToggleRequested); });
            if (dc != null)
            {
                FormattedText folder = _text.Make("Open log folder", 12, Palette.Ok);
                FormattedText task = _text.Make("Task Manager \u2197", 12, Palette.Ok);
                dc.DrawText(folder, new Point(PadX, y));
                Hit(new Rect(PadX, y, folder.Width, RowH), delegate { Raise(OpenLogFolderRequested); });
                double tx = PanelWidth - PadX - task.Width;
                dc.DrawText(task, new Point(tx, y));
                Hit(new Rect(tx, y, task.Width, RowH), delegate { Raise(TaskManagerRequested); });
            }
            y += RowH;
            return y + PadY;
        }

        private double Section(DrawingContext dc, double y, string title)
        {
            y += 6;
            if (dc != null)
            {
                dc.DrawLine(Palette.Divider, new Point(PadX, y + 0.5), new Point(PanelWidth - PadX, y + 0.5));
                _text.Left(dc, title, 12, Palette.Label, PadX, y + 5);
            }
            return y + 5 + RowH;
        }

        private double Row(DrawingContext dc, double y, string label, string value, Brush valueBrush, Action onClickValue)
        {
            if (dc != null)
            {
                double labelW = _text.Width(label, 12);
                _text.Left(dc, label, 12, Palette.Label, PadX, y);
                FormattedText v = _text.Make(_text.Fit(value, Inner - labelW - 10, 12), 12, valueBrush);
                double vx = PanelWidth - PadX - v.Width;
                dc.DrawText(v, new Point(vx, y));
                if (onClickValue != null) Hit(new Rect(vx, y, v.Width, RowH), onClickValue);
            }
            return y + RowH;
        }

        // A row whose label (the COM port) is the clickable part.
        private double RowClickableLabel(DrawingContext dc, double y, string label, string value, Action onClickLabel)
        {
            if (dc != null)
            {
                FormattedText l = _text.Make(label, 12, Palette.Ok);
                dc.DrawText(l, new Point(PadX, y));
                Hit(new Rect(PadX, y, l.Width, RowH), onClickLabel);
                _text.Right(dc, value, 12, Palette.Text, PanelWidth - PadX, y);
            }
            return y + RowH;
        }

        private double Note(DrawingContext dc, double y, string text)
        {
            if (dc != null) _text.Left(dc, text, 12, Palette.Label, PadX, y);
            return y + RowH;
        }

        private double Link(DrawingContext dc, double y, string text, Action onClick)
        {
            if (dc != null)
            {
                FormattedText t = _text.Make(text, 12, Palette.Ok);
                dc.DrawText(t, new Point(PadX, y));
                Hit(new Rect(PadX, y, t.Width, RowH), onClick);
            }
            return y + RowH;
        }

        // Efficiency / Balanced / Performance, the current one highlighted.
        private double Segments(DrawingContext dc, double y, PowerModeKind current)
        {
            if (dc != null)
            {
                var modes = new[] { PowerModeKind.Efficiency, PowerModeKind.Balanced, PowerModeKind.Performance };
                double gap = 4, w = (Inner - 2 * gap) / 3;
                for (int i = 0; i < modes.Length; i++)
                {
                    PowerModeKind m = modes[i];
                    var r = new Rect(PadX + i * (w + gap), y, w, 20);
                    bool on = m == current;
                    dc.DrawRoundedRectangle(on ? Palette.OkTint : Palette.Segment, null, r, 5, 5);
                    FormattedText t = _text.Make(m.ToString(), 11, on ? Palette.Ok : Palette.Text);
                    dc.DrawText(t, new Point(r.X + (w - t.Width) / 2, y + (20 - t.Height) / 2));
                    Hit(r, delegate { Raise(PowerModeRequested, m); });
                }
            }
            return y + 24;
        }

        private bool IsCopied(string text)
        {
            return text != null && text == _copied && DateTime.Now - _copiedAt < CopiedFor;
        }

        private void Hit(Rect r, Action a)
        {
            _hits.Add(new KeyValuePair<Rect, Action>(r, a));
        }

        private static void Raise(Action a)
        {
            if (a != null) a();
        }

        private static void Raise<T>(Action<T> a, T value)
        {
            if (a != null) a(value);
        }
    }

    // The panel window: borderless, topmost while open, beside the card; closes on Esc or when it loses activation.
    internal sealed class DetailsPanel : Window
    {
        private readonly PanelView _view = new PanelView();
        private Box _card, _workArea;

        public DetailsPanel()
        {
            Title = "Desktop Monitor details";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = _view;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Hide(); };
            Deactivated += delegate
            {
                if (!IsVisible) return;
                LastAutoHide = DateTime.Now;
                Hide();
            };
            SizeChanged += delegate { if (IsVisible) Place(); };
            SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_TOOLWINDOW; // not in Alt+Tab
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            };
        }

        public PanelView View { get { return _view; } }

        // When the panel last closed because it lost activation. A tray double-click right after that is the user
        // closing the panel, not asking to reopen it.
        public DateTime LastAutoHide { get; private set; }

        public void ShowBeside(Box card, Box workArea)
        {
            _card = card;
            _workArea = workArea;
            _view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Place();
            Show();
            Activate();
        }

        private void Place()
        {
            double h = IsVisible && ActualHeight > 0 ? ActualHeight : _view.DesiredSize.Height;
            Box p = Rules.PanelPlacement(_card, _workArea, PanelView.PanelWidth, h, 10);
            Left = p.X;
            Top = p.Y;
        }
    }
}
```

- [ ] **Step 2: Replace `src\TrayIcon.cs`**

```csharp
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopMonitor
{
    // Notification-area icon (spec section 6): live CPU temperature, tooltip and the right-click menu.
    internal sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon = new NotifyIcon();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _unlock;
        private readonly ToolStripMenuItem _autostart;
        private readonly string _settingsPath;
        private Icon _current;
        private IntPtr _currentHandle;
        private string _shownKey;

        public event Action UnlockToggled;
        public event Action ExitRequested;
        public event Action DetailsRequested;

        public TrayIcon(string settingsPath)
        {
            _settingsPath = settingsPath;
            var details = new ToolStripMenuItem("Details", null, delegate { Raise(DetailsRequested); });
            details.Font = new Font(details.Font, FontStyle.Bold); // the default action, as on a double-click
            _menu.Items.Add(details);
            _icon.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) Raise(DetailsRequested); };
            _unlock = new ToolStripMenuItem("Unlock to move", null, delegate { Raise(UnlockToggled); });
            _autostart = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleAutostart(); });
            _menu.Items.Add(_unlock);
            _menu.Items.Add(_autostart);
            _menu.Items.Add(new ToolStripMenuItem("Open settings", null, delegate { OpenSettings(); }));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Raise(ExitRequested); }));
            _menu.Opening += delegate { _autostart.Checked = Autostart.IsEnabled(Application.ExecutablePath); };
            _icon.ContextMenuStrip = _menu;
            _icon.Text = "Desktop Monitor";
            SetIcon("--", Level.Unknown);
            _icon.Visible = true;
        }

        public void SetUnlocked(bool unlocked)
        {
            _unlock.Text = unlocked ? "Lock position" : "Unlock to move";
        }

        // Windows 11 shows this as a notification and holds it back during Do Not Disturb.
        public void ShowAlert(Alert a)
        {
            _icon.ShowBalloonTip(5000, a.Title, string.IsNullOrEmpty(a.Body) ? " " : a.Body, ToolTipIcon.None);
        }

        public void Update(Snapshot s, AppSettings st)
        {
            string text = Rules.FormatNumber(s.CpuTempC);
            Level level = Rules.Classify(s.CpuTempC, st.TempAmber, st.TempRed);
            if (text + level != _shownKey) SetIcon(text, level);
            _icon.Text = Rules.Tooltip(s.CpuPercent, s.CpuTempC);
        }

        private void SetIcon(string text, Level level)
        {
            _shownKey = text + level;
            using (Bitmap bmp = RenderGlyph(text, TrayColour(level)))
            {
                IntPtr handle = bmp.GetHicon();
                Icon icon = Icon.FromHandle(handle);
                _icon.Icon = icon;
                ReleaseCurrentIcon();
                _current = icon;
                _currentHandle = handle;
            }
        }

        // 16 x 16 dark tile with the temperature number, so it reads on light and dark taskbars.
        internal static Bitmap RenderGlyph(string text, Color colour)
        {
            var bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var bg = new SolidBrush(Color.FromArgb(0x14, 0x14, 0x16)))
            using (var fg = new SolidBrush(colour))
            using (var font = new Font("Segoe UI", text.Length >= 3 ? 8f : 10f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic))
            {
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                fmt.FormatFlags |= StringFormatFlags.NoWrap; // "100" must not wrap inside the 16 px tile
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.FillRectangle(bg, 0, 0, 16, 16);
                g.DrawString(text, font, fg, new RectangleF(0, 0, 16, 16), fmt);
            }
            return bmp;
        }

        private static Color TrayColour(Level level)
        {
            switch (level)
            {
                case Level.Ok: return Color.FromArgb(0x5D, 0xCA, 0xA5);
                case Level.Amber: return Color.FromArgb(0xFA, 0xC7, 0x75);
                case Level.Red: return Color.FromArgb(0xF0, 0x95, 0x95);
                default: return Color.FromArgb(0xA3, 0xA3, 0xA3);
            }
        }

        private void ReleaseCurrentIcon()
        {
            if (_current == null) return;
            _current.Dispose();
            Native.DestroyIcon(_currentHandle); // Icon.FromHandle does not own the handle
            _current = null;
        }

        private void ToggleAutostart()
        {
            try
            {
                bool on = !Autostart.IsEnabled(Application.ExecutablePath);
                Autostart.Set(on, Application.ExecutablePath);
                _autostart.Checked = on;
            }
            catch (Exception ex)
            {
                Log.Write("Tray: could not change Start with Windows, " + ex.Message);
            }
        }

        private void OpenSettings()
        {
            try
            {
                Process.Start(_settingsPath);
            }
            catch (Exception ex) // no app associated with .json
            {
                Log.Write("Tray: no .json handler (" + ex.Message + "), opening in Notepad");
                try { Process.Start("notepad.exe", "\"" + _settingsPath + "\""); }
                catch (Exception ex2) { Log.Write("Tray: could not open settings, " + ex2.Message); }
            }
        }

        private static void Raise(Action action)
        {
            if (action != null) action();
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            ReleaseCurrentIcon();
        }
    }

    // HKCU Run key is the single source of truth for "Start with Windows" (no admin needed).
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DesktopMonitor";

        // Only counts as enabled when the entry points at this exe, so a moved build shows unchecked.
        public static bool IsEnabled(string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var value = key == null ? null : key.GetValue(ValueName) as string;
                return value != null && string.Equals(value.Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void Set(bool enabled, string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, "\"" + exePath + "\"");
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
```

- [ ] **Step 3: Replace `src\SafeTimer.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace DesktopMonitor
{
    // DispatcherTimer whose action can throw without stopping it. WPF does not re-arm a DispatcherTimer whose Tick
    // threw, even when DispatcherUnhandledException marks the exception handled, which would freeze the card.
    internal sealed class SafeTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly string _name;
        private readonly Action _action;
        private string _lastError;

        public SafeTimer(TimeSpan interval, string name, Action action)
        {
            _name = name;
            _action = action;
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += delegate { RunNow(); };
        }

        public void Start() { _timer.Start(); }

        public void Stop() { _timer.Stop(); }

        public void RunNow()
        {
            try
            {
                _action();
                _lastError = null;
            }
            catch (Exception ex)
            {
                string error = ex.GetType().Name + ": " + ex.Message;
                if (error != _lastError) Log.Write(_name + " failed, " + error); // once per distinct error, not every tick
                _lastError = error;
            }
        }
    }

    // Runs one part of the UI update so that its failure cannot stop the others (card, tray, alerts, log, panel).
    // UI thread only; logs once per distinct error per part.
    internal static class Guard
    {
        private static readonly Dictionary<string, string> LastError = new Dictionary<string, string>();

        public static void Run(string name, Action action)
        {
            try
            {
                action();
                LastError.Remove(name);
            }
            catch (Exception ex)
            {
                string error = ex.GetType().Name + ": " + ex.Message;
                string last;
                if (!LastError.TryGetValue(name, out last) || last != error) Log.Write(name + " failed, " + error);
                LastError[name] = error;
            }
        }
    }
}
```

- [ ] **Step 4: Replace `src\Program.cs`**

Set `PowerSwitchAvailable` to the `Decision:` in `spike\POWER-RESULT.md` (shown as `true`).

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopMonitor
{
    internal static class Program
    {
        // Outcome of the v1 Task 1 spike (spike\RESULT.md): true = owned by the desktop host window, false = bottom-of-stack only.
        internal static readonly bool UseDesktopOwner = true;

        // Outcome of the v2 Task 1 spike (spike\POWER-RESULT.md): true = the panel may switch the power mode.
        internal static readonly bool PowerSwitchAvailable = true;

        [STAThread]
        private static int Main()
        {
            bool firstInstance;
            using (var mutex = new Mutex(true, @"Local\DesktopMonitor", out firstInstance))
            {
                if (!firstInstance) return 0; // already running
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) => { Log.Write("UI error: " + e.Exception); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Fatal: " + e.ExceptionObject);
                using (var controller = new AppController())
                {
                    controller.Start();
                    app.Run();
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }

    // Wires settings, the sampler thread, card, tray, details panel, alerts and CSV log, and owns their lifetimes.
    internal sealed class AppController : IDisposable
    {
        private static readonly TimeSpan HistoryEvery = TimeSpan.FromSeconds(10);
        private const double ReopenGuardMs = 600;

        private readonly string _settingsPath = SettingsStore.DefaultPath;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly History _history = new History(60, HistoryEvery);
        private readonly AlertEngine _alerts = new AlertEngine();
        private readonly CsvLogWriter _log = new CsvLogWriter(CsvLog.DefaultDir);
        private LogWindow _logWindow = new LogWindow();
        private AppSettings _settings;
        private SettingsWatcher _watcher;
        private SamplerThread _sampler;
        private TrayIcon _tray;
        private CardWindow _window;
        private DetailsPanel _panel;
        private Snapshot _last;
        private PanelData _panelData;
        private bool _exiting;

        public void Start()
        {
            _settings = SettingsStore.LoadOrCreate(_settingsPath);
            _tray = new TrayIcon(_settingsPath);
            _tray.UnlockToggled += delegate { _window.SetUnlocked(!_window.Unlocked); };
            _tray.ExitRequested += Exit;
            _tray.DetailsRequested += TogglePanel;
            ShowCard();
            CreatePanel();
            try
            {
                _watcher = new SettingsWatcher(_settingsPath);
                _watcher.Changed += s => _dispatcher.BeginInvoke(new Action(() => ApplySettings(s)));
            }
            catch (Exception ex)
            {
                Log.Write("Settings: live reload unavailable, " + ex.Message);
            }
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _sampler = new SamplerThread(_settings, _dispatcher, OnSnapshot, OnPanelData);
            _sampler.Start();
        }

        private void ShowCard()
        {
            _window = new CardWindow(_settings, Program.UseDesktopOwner);
            _window.PositionCommitted += SavePosition;
            _window.UnlockedChanged += unlocked => _tray.SetUnlocked(unlocked);
            _window.Closed += delegate
            {
                if (_exiting) return;
                Log.Write("Card window closed unexpectedly, recreating");
                _dispatcher.BeginInvoke(new Action(ShowCard));
            };
            _tray.SetUnlocked(false);
            _window.Show();
            if (_last != null) _window.Card.Update(_last);
            _window.Card.SetHistory(_history.ToArray(), _history.Peak, _history.Capacity);
        }

        private void CreatePanel()
        {
            _panel = new DetailsPanel();
            _panel.IsVisibleChanged += delegate { if (_sampler != null) _sampler.SetPanelOpen(_panel.IsVisible); };
            PanelView v = _panel.View;
            v.CopyRequested += Copy;
            v.PowerModeRequested += SetPowerMode;
            v.LogToggleRequested += ToggleLog;
            v.OpenLogFolderRequested += delegate { Open("explorer.exe", "\"" + EnsureDir(_log.Dir) + "\""); };
            v.TaskManagerRequested += delegate { Open("taskmgr.exe", null); };
            v.PowerSettingsRequested += delegate { Open("ms-settings:powersleep", null); };
        }

        private void OnSnapshot(Snapshot s)
        {
            _last = s;
            Guard.Run("Card", delegate
            {
                _window.Card.Update(s);
                if (_history.Offer(s.Time, s.CpuTempC)) _window.Card.SetHistory(_history.ToArray(), _history.Peak, _history.Capacity);
            });
            Guard.Run("Tray", delegate { _tray.Update(s, _settings); });
            Guard.Run("Alerts", delegate
            {
                foreach (Alert a in _alerts.Evaluate(s, _settings))
                {
                    _tray.ShowAlert(a);
                    _log.AppendAlert(a);
                }
            });
            Guard.Run("CSV log", delegate { LogRow(s); });
            if (_panel.IsVisible) Guard.Run("Panel", RefreshPanel);
        }

        private void OnPanelData(PanelData d)
        {
            _panelData = d;
            if (_panel.IsVisible) Guard.Run("Panel", RefreshPanel);
        }

        private void LogRow(Snapshot s)
        {
            if (!_settings.LogEnabled)
            {
                _logWindow = new LogWindow();
                return;
            }
            _logWindow.Add(s);
            TimeSpan every = TimeSpan.FromSeconds(_settings.LogIntervalSeconds);
            if (!_logWindow.Due(s.Time, every)) return;
            _log.Append(s.Time, _logWindow.ToRow(PowerMode.Name(PowerMode.Get())), (int)_settings.LogRetentionDays);
            _logWindow.Next(every);
        }

        private void RefreshPanel()
        {
            _panel.View.Update(_last ?? new Snapshot(), _panelData, _alerts.Recent(DateTime.Now), _settings, Program.PowerSwitchAvailable);
        }

        private void TogglePanel()
        {
            if (_panel.IsVisible)
            {
                _panel.Hide();
                return;
            }
            if ((DateTime.Now - _panel.LastAutoHide).TotalMilliseconds < ReopenGuardMs) return; // that double-click was to close it
            RefreshPanel();
            Box card = _window.Bounds;
            _panel.ShowBeside(card, CardWindow.WorkAreaContaining(card));
        }

        private void Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                _panel.View.ShowCopied(text);
            }
            catch (Exception ex)
            {
                Log.Write("Panel: could not copy to the clipboard, " + ex.Message);
            }
        }

        private void SetPowerMode(PowerModeKind mode)
        {
            if (!PowerMode.Set(mode)) Log.Write("Panel: Windows refused power mode " + mode);
            if (_panelData == null) _panelData = new PanelData();
            _panelData.PowerMode = PowerMode.Get(); // show what Windows actually applied
            RefreshPanel();
        }

        private void ToggleLog()
        {
            AppSettings s = _settings.Clone();
            s.LogEnabled = !s.LogEnabled;
            _settings = s;
            Save(s, "log setting");
            RefreshPanel();
        }

        // Thresholds, opacity, zones, alerts and log settings apply live; x/y are only read at start-up.
        private void ApplySettings(AppSettings s)
        {
            _settings = s;
            _sampler.ApplySettings(s);
            _window.ApplySettings(s);
            if (_panel.IsVisible) RefreshPanel();
        }

        private void SavePosition(double x, double y)
        {
            AppSettings s = _settings.Clone();
            s.X = Math.Round(x);
            s.Y = Math.Round(y);
            _settings = s;
            Save(s, "position");
        }

        private void Save(AppSettings s, string what)
        {
            try
            {
                SettingsStore.Save(_settingsPath, s);
            }
            catch (Exception ex)
            {
                Log.Write("Settings: could not save " + what + ", " + ex.Message);
            }
        }

        private static string EnsureDir(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Open(string target, string args)
        {
            try
            {
                if (args == null) Process.Start(target);
                else Process.Start(target, args);
            }
            catch (Exception ex)
            {
                Log.Write("Panel: could not open " + target + ", " + ex.Message);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) _sampler.RequestReset();
        }

        private void Exit()
        {
            _exiting = true;
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _exiting = true;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (_sampler != null) _sampler.Dispose();
            if (_watcher != null) _watcher.Dispose();
            if (_tray != null) _tray.Dispose();
        }
    }
}
```

- [ ] **Step 5: Build and run the tests**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
Stop-Process -Name DesktopMonitor -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
Start-Process .\bin\DesktopMonitor.exe; Start-Sleep -Seconds 4
powershell -NoProfile -ExecutionPolicy Bypass -File tools\inspect-window.ps1
```
Expected: `301 passed, 0 failed`; `Built bin\DesktopMonitor.exe`; the card window is `visible: True`, owner `Progman`, `click-through: True`.

- [ ] **Step 6: Panel checks with the user**

Ask the user to try each and confirm:
1. Left double-click the tray icon: the panel opens beside the card (left of it at top-right), top-aligned, inside the screen.
2. Sections in order: CPU speed, Top apps (5), Network (Wi-Fi name and signal, IP, gateway; no VMware adapters), COM ports, Alerts, Power mode, Battery, CSV log.
3. Click the IP: it reads "Copied" for about 1.5 s and pasting (Ctrl+V in Notepad) gives the IP.
4. Press Esc: the panel closes. Reopen it from the tray menu **Details** (shown bold); click anywhere else: it closes. Double-click the tray icon twice with it open: it closes and stays closed.
5. Power mode: click Efficiency; Settings → Power & battery shows "Best power efficiency"; click the original mode back (skip if `PowerSwitchAvailable = false`: then "Open power settings ↗" opens Settings).
6. "Open log folder" opens `%LOCALAPPDATA%\DesktopMonitor\logs`; "Task Manager ↗" opens Task Manager.
7. After 10 minutes the card's history graph has filled from the right.

- [ ] **Step 7: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/DetailsPanel.cs src/TrayIcon.cs src/SafeTimer.cs src/Program.cs && git commit -m "Add the details panel, tray Details and notifications, and wire alerts and the CSV log

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: README and final verification

**Files:**
- Modify: `README.md`, `docs\superpowers\specs\2026-10-03-desktop-monitor-v2-design.md` (status line)

- [ ] **Step 1: Add the v2 sections to `README.md`**

Insert before the `## Settings` heading:

````markdown
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

````

In the settings table, add these rows after `zoneCpu, zoneSkin, zoneBattery`:

```markdown
| `logEnabled`, `logIntervalSeconds`, `logRetentionDays` | `true`, `10`, `30` | CSV log on/off, row interval (5–300 s), days kept |
| `alertsEnabled` and `alert...On` | `true` | All alerts, and each one |
| `alertCpuTemp`, `alertCpuClear`, `alertCpuSeconds` | `90`, `85`, `60` | CPU hot: fire at, clear below, seconds held |
| `alertThrottleSeconds` | `120` | Seconds below full speed before the throttled alert |
| `alertDiskFreeGb`, `alertDiskClearGb` | `10`, `12` | Disk low: fire below, clear above (GB free) |
| `alertBatteryPercent` | `20` | Battery low on battery |
| `alertBatteryTemp`, `alertBatteryTempClear`, `alertBatteryTempSeconds` | `45`, `42`, `60` | Battery hot |
```

And in the Build section, add after the `-Dump` line:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Power   # bin\PowerModeTool.exe: read or set the power mode
```

- [ ] **Step 2: CSV log check**

Run:
```powershell
$f = Join-Path $env:LOCALAPPDATA ("DesktopMonitor\logs\" + (Get-Date -Format 'yyyy-MM-dd') + ".csv")
Get-Content $f -TotalCount 1; Start-Sleep -Seconds 25; Get-Content $f -Tail 3
```
Expected: the header line, then rows about 10 s apart with values in every column except those genuinely unavailable. Ask the user to open the file in Excel (columns split correctly), wait 30 s, close Excel, and confirm with `Get-Content $f -Tail 5` that rows kept arriving with no gap.

- [ ] **Step 3: Alerts end to end**

Edit `settings.json`: set `"alertCpuTemp"` and `"alertCpuClear"` below the current CPU temperature (for example 40 and 35) and `"alertCpuSeconds": 5`, save. Expected within about 10 s: exactly one "CPU hot" notification, a "CPU hot" row in the panel's Alerts and in `alerts.csv`, and no second notification while it stays above. Restore `90`, `85`, `60` and save.

- [ ] **Step 4: COM port check (if a USB-serial board is at hand)**

Ask the user to plug in a board. Expected within about 3 s: a "COMn connected · <device name>" notification, the port highlighted with " · new" in the panel for 10 s, and its row in `alerts.csv`. Unplug: the row disappears. If no board is available, record "not run" in the ledger.

- [ ] **Step 5: Resource check**

With the panel closed for 10 minutes:
```powershell
$p = Get-Process DesktopMonitor; $t0 = $p.TotalProcessorTime; Start-Sleep -Seconds 60; $p.Refresh()
"CPU {0:N2} %  working set {1:N0} MB" -f (($p.TotalProcessorTime - $t0).TotalSeconds / 60 / [Environment]::ProcessorCount * 100), ($p.WorkingSet64 / 1MB)
```
Expected: CPU < 1 %, working set < 150 MB. Then ask the user to open the panel and run the same measurement while it stays open: CPU < 2 %. If a target is missed, report the numbers to the user; do not tune blindly.

- [ ] **Step 6: v1 checks still open (ask the user)**

1. Sleep and wake: within 3 s every value is live again; no speed spike in Net ↓ / Net ↑; the CSV log carries on without a burst of rows.
2. Tray → Start with Windows, reboot, sign in: the card and tray icon appear; `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v DesktopMonitor` shows the exe path.

- [ ] **Step 7: Mark the spec implemented**

In `docs\superpowers\specs\2026-10-03-desktop-monitor-v2-design.md` replace `Status: approved 2026-10-03` with `Status: implemented 2026-10-03 (see spike/POWER-RESULT.md for the power switch)`.

- [ ] **Step 8: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add README.md docs/superpowers/specs/2026-10-03-desktop-monitor-v2-design.md && git commit -m "Document v2 and mark its spec implemented

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
