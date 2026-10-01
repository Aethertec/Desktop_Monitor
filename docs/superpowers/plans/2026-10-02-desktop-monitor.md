# Desktop Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A native Windows desktop card (gauge style B) showing live CPU, RAM, GPU, disk, network, battery and temperatures for the HP ProBook 440 G8, pinned to the desktop layer and click-through.

**Architecture:** One small WPF executable built in code (no XAML) with the C# compiler that ships with Windows. A 1 s `DispatcherTimer` asks `MetricsSampler` for a `Snapshot`; `CardView` draws the card from it, `TrayIcon` shows the CPU temperature. `DesktopPin` keeps the borderless transparent `CardWindow` on the desktop layer; `Settings` loads and live-reloads `settings.json`. Pure logic lives in `Rules` and `Settings` and is unit-tested by a framework-free console runner.

**Tech Stack:** C# 5, .NET Framework 4.8 (WPF, WinForms `NotifyIcon`, `System.Diagnostics.PerformanceCounter`, `JavaScriptSerializer`), Win32 P/Invoke, PowerShell 5.1 build script, git.

**Spec:** `docs/superpowers/specs/2026-10-02-desktop-monitor-design.md`

All code in this plan was compiled with the target compiler and the unit tests were run (118 passed) before the plan was written; copy it verbatim.

## Global Constraints

- Project root: `D:\Projects\Desktop_Monitor`. All paths below are relative to it.
- Compiler: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (version 4.8.9221), invoked only through `build.ps1`. **C# 5 only:** no `$"..."` interpolation, no `?.`, no `nameof`, no `=>` members, no auto-property initialisers, no exception filters, no `using static`.
- No NuGet, no installs. References are only the Framework assemblies listed in `build.ps1`.
- 64-bit build (`/platform:x64`); runs as the normal user (`asInvoker`), never needs admin.
- UI built in code; no XAML.
- Non-ASCII characters in C# string literals are written as `\u` escapes (`\u00B0` for °, `\u2193` ↓, `\u2191` ↑, `\u00B7` ·).
- Thresholds (inclusive, compared on the displayed rounded value): usage amber ≥ 70 % red ≥ 85 % (CPU, RAM, GPU); temperature amber ≥ 80 °C red ≥ 85 °C; disk amber ≥ 90 % red ≥ 95 %.
- Colours: card `#141416` at opacity 0.93, text `#ECECEC`, labels `#A3A3A3`, ring track white 13 %, OK `#5DCAA5`, amber `#FAC775`, red `#F09595`.
- Card 250 DIPs wide; default position top-right of the primary work area, 12 px margins.
- Settings `%APPDATA%\DesktopMonitor\settings.json`; log `%APPDATA%\DesktopMonitor\log.txt` (restarted past 100 KB).
- Targets: app CPU < 1 % average, working set < 50 MB.
- Run scripts as `powershell -NoProfile -ExecutionPolicy Bypass -File <script> [args]` from the project root.
- Commit after every task on branch `v1-build`, each message ending with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Actions that disturb the user's session (restarting Explorer, sleep, reboot) happen only after asking the user in chat.

### Refinements to the spec found while verifying this plan

- Thermal zones use the ACPI offset 273.2 K (spec corrected). Measured CPU zone 3592 tenths of K = 86.0 °C; empty zones read 2732.
- GPU: instead of re-enumerating counters every 10 s, the sampler reads the whole `GPU Engine` category once per tick (measured 1–3 ms) and computes utilisation from consecutive raw samples, so new or ended processes are always picked up.
- Test runner split into `tests\TestMain.cs`, `tests\RulesTests.cs`, `tests\SettingsTests.cs`. Extra helpers: `tools\inspect-window.ps1`, `tools\SamplerDump.cs`, and `build.ps1 -Spike` / `-Dump`.
- The "plug glyph" is drawn as a small lightning bolt next to the battery percentage when on AC.

## Review Focus

1. **Value shown as 80° but coloured OK.** A temperature of 79.6 °C displays "80°"; the user expects amber, not teal. Pinned in Task 2 (`Classify_UsesTheDisplayedRounding`).
2. **Hand-edited `settings.json` with a typo, wrong type or half-saved file.** The user expects the other keys to keep working and a broken file to be ignored (last good settings stay). Pinned in Task 3 (`Parse_WrongTypeFallsBackToDefault`, `Parse_MalformedJsonThrowsFormatException`, `LoadOrCreate_MalformedFileGivesDefaultsAndLeavesFileAlone`).
3. **Network totals dropping when Wi-Fi switches or a VPN toggles.** The user expects no negative or absurd speed, just a momentary "--". Pinned in Task 2 (`Rate_RejectsCountersThatGoBackwards`).
4. **Saved position off-screen after unplugging a monitor or changing resolution.** The user expects the card back at top-right. Pinned in Task 2 (`IsMostlyOnScreen_Cases`).
5. **Sensors reporting placeholders.** An empty thermal zone (273.2 K) or unknown battery charge (255 %) must show "--", not "0°C" or "255%". Pinned in Task 2 (`ThermalToCelsius_ConvertsAndRejectsEmptyZones`, `BatteryPercent_RejectsUnknownCharge`).

## File Map

| File | Responsibility | Task |
|---|---|---|
| `build.ps1` | Compile app, tests, spike, dump tool with built-in csc | 1 |
| `.gitignore` | Ignore `bin/` | 1 |
| `src\Native.cs` | Win32 P/Invoke declarations | 1 |
| `src\Log.cs` | Never-throwing append log, data folder path | 1 |
| `src\DesktopPin.cs` | Desktop-layer pinning, Explorer-restart re-attach | 1 |
| `spike\PinSpike.cs`, `spike\RESULT.md` | Throwaway pin experiment and its recorded outcome | 1 |
| `tools\inspect-window.ps1` | Report a window's owner, click-through, z-order | 1 |
| `tests\TestMain.cs` | Framework-free test runner | 2 (edited in 3) |
| `src\Rules.cs`, `tests\RulesTests.cs` | Thresholds, conversions, formatting, placement | 2 |
| `src\Settings.cs`, `tests\SettingsTests.cs` | Settings model, JSON load/save, live watcher | 3 |
| `src\Snapshot.cs` | One sample of all readings | 4 |
| `src\MetricsSampler.cs` | Reads every metric, isolated failures | 4 |
| `tools\SamplerDump.cs` | Console printout of live readings | 4 |
| `app.manifest` | asInvoker, DPI awareness, Windows 10/11 | 5 |
| `src\CardView.cs` | Draws the card | 5 |
| `src\CardWindow.cs` | Transparent window, click-through, drag, placement | 5 |
| `src\TrayIcon.cs` | Tray icon, menu, autostart registry | 5 |
| `src\Program.cs` | Entry point, single instance, wiring | 5 |
| `README.md` | Build, run, settings, troubleshooting | 6 |

---

### Task 1: Build script and desktop-pin spike

The riskiest part goes first: prove on this PC that a transparent WPF window can live on the desktop layer, stay visible after Win+D, survive an Explorer restart and let clicks through.

**Files:**
- Create: `.gitignore`, `build.ps1`, `src\Native.cs`, `src\Log.cs`, `src\DesktopPin.cs`, `spike\PinSpike.cs`, `tools\inspect-window.ps1`, `spike\RESULT.md`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `internal static class Native` — constants `GWL_EXSTYLE`, `GWLP_HWNDPARENT`, `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE` (all `long` for the styles), `WM_WINDOWPOSCHANGING`, `SWP_*`, `SW_SHOWNOACTIVATE`, `HWND_BOTTOM`; structs `WINDOWPOS`, `MEMORYSTATUSEX`; methods `FindWindow`, `FindWindowEx`, `EnumWindows`, `IsWindow`, `IsWindowVisible`, `ShowWindow`, `GetWindowLongPtr`, `SetWindowLongPtr`, `SetWindowPos`, `RegisterWindowMessage`, `DestroyIcon`, `GlobalMemoryStatusEx`.
  - `public static class Log` — `string DataDir { get; }`, `static string FilePath` (settable), `void Write(string message)`.
  - `internal sealed class DesktopPin` — `DesktopPin(IntPtr hwnd, bool useOwner)`, `void Attach()`, `IntPtr Host { get; }`, `static IntPtr FindDesktopHost()`.
  - `spike\RESULT.md` stating `UseDesktopOwner = true` or `false` (consumed by Task 5).

- [ ] **Step 1: Create the working branch**

```bash
cd /d/Projects/Desktop_Monitor && git checkout -b v1-build
```

Expected: `Switched to a new branch 'v1-build'`

- [ ] **Step 2: Create `.gitignore`**

```gitignore
bin/
```

- [ ] **Step 3: Create `build.ps1`**

```powershell
# Builds Desktop Monitor with the C# compiler that ships with Windows (.NET Framework 4.8, C# 5 only).
#   build.ps1          -> bin\DesktopMonitor.exe
#   build.ps1 -Test    -> bin\Tests.exe, then runs it (exit code 1 when any test fails)
#   build.ps1 -Spike   -> bin\PinSpike.exe (desktop-layer experiment)
#   build.ps1 -Dump    -> bin\SamplerDump.exe (prints live readings)
param([switch]$Test, [switch]$Spike, [switch]$Dump)
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
    (Join-Path $fw 'System.Web.Extensions.dll')
) | ForEach-Object { '/r:' + $_ }

function Compile([string]$target, [string]$out, [string[]]$sources, [string[]]$extra = @()) {
    $files = @($sources | ForEach-Object { Join-Path $root $_ })  # @() so a single file is not splatted char by char
    & $csc @common @refs @extra "/target:$target" "/out:$(Join-Path $bin $out)" @files
    if ($LASTEXITCODE -ne 0) { throw "Build of $out failed" }
    Write-Output "Built bin\$out"
}

# Pure files shared by tests and tools; only those that exist yet are included.
$pure = @(@('src\Rules.cs', 'src\Settings.cs', 'src\Log.cs') | Where-Object { Test-Path (Join-Path $root $_) })

if ($Test) {
    Compile 'exe' 'Tests.exe' (@('tests\*.cs') + $pure)
    & (Join-Path $bin 'Tests.exe')
    exit $LASTEXITCODE
}
elseif ($Spike) {
    Compile 'winexe' 'PinSpike.exe' @('spike\PinSpike.cs', 'src\DesktopPin.cs', 'src\Native.cs', 'src\Log.cs')
}
elseif ($Dump) {
    Compile 'exe' 'SamplerDump.exe' (@('tools\SamplerDump.cs', 'src\MetricsSampler.cs', 'src\Snapshot.cs', 'src\Native.cs') + $pure)
}
else {
    Compile 'winexe' 'DesktopMonitor.exe' @('src\*.cs') @("/win32manifest:$(Join-Path $root 'app.manifest')")
}
```

- [ ] **Step 4: Create `src\Native.cs`**

```csharp
using System;
using System.Runtime.InteropServices;

namespace DesktopMonitor
{
    // Win32 declarations used by the desktop pin, card window, sampler and tray icon. 64-bit build only (GetWindowLongPtrW).
    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int GWLP_HWNDPARENT = -8;
        public const long WS_EX_TRANSPARENT = 0x00000020;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_NOACTIVATE = 0x08000000;
        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const int SW_SHOWNOACTIVATE = 4;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    }
}
```

- [ ] **Step 5: Create `src\Log.cs`**

```csharp
using System;
using System.Globalization;
using System.IO;

namespace DesktopMonitor
{
    // Append-only diagnostics log in %APPDATA%\DesktopMonitor\log.txt, restarted once it passes 100 KB. Never throws.
    public static class Log
    {
        private static readonly object Gate = new object();

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopMonitor"); }
        }

        // Tests point this at a temp file.
        public static string FilePath = Path.Combine(DataDir, "log.txt");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 100 * 1024) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Logging must never take the app down.
            }
        }
    }
}
```

- [ ] **Step 6: Create `src\DesktopPin.cs`**

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
        private readonly DispatcherTimer _watchdog;
        private IntPtr _host;

        public DesktopPin(IntPtr hwnd, bool useOwner)
        {
            _hwnd = hwnd;
            _useOwner = useOwner;
            _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _watchdog.Tick += delegate
            {
                // Covers Explorer not being ready at login and a missed TaskbarCreated broadcast.
                if (_useOwner && !Native.IsWindow(_host)) Attach();
            };
            _watchdog.Start();
        }

        public IntPtr Host { get { return _host; } }

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

- [ ] **Step 7: Create `spike\PinSpike.cs`**

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Throwaway experiment (plan Task 1): proves desktop-layer pinning on this PC before the real card exists.
    //   PinSpike.exe              owner mode, click-through
    //   PinSpike.exe --no-owner   bottom-of-stack only, click-through
    //   PinSpike.exe --unlocked   owner mode, not click-through, drag to move
    internal static class PinSpike
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool useOwner = Array.IndexOf(args, "--no-owner") < 0;
            bool unlocked = Array.IndexOf(args, "--unlocked") >= 0;
            var app = new Application();
            var window = new Window
            {
                Title = "PinSpike",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Width = 250,
                Height = 204,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = SystemParameters.WorkArea.Right - 262,
                Top = SystemParameters.WorkArea.Top + 12
            };
            window.Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(237, 0x14, 0x14, 0x16)),
                CornerRadius = new CornerRadius(10),
                Child = new TextBlock
                {
                    Text = "Pin spike\nowner mode: " + useOwner + "\nunlocked: " + unlocked,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    Margin = new Thickness(14)
                }
            };
            window.SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (!unlocked) ex |= Native.WS_EX_TRANSPARENT;
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
                var pin = new DesktopPin(hwnd, useOwner);
                pin.Attach();
                Log.Write("PinSpike: owner mode=" + useOwner + ", unlocked=" + unlocked + ", host=0x" + pin.Host.ToString("X"));
            };
            window.MouseLeftButtonDown += delegate { if (unlocked) window.DragMove(); };
            app.Run(window);
        }
    }
}
```

- [ ] **Step 8: Create `tools\inspect-window.ps1`**

```powershell
# Reports how a window is layered: owner, click-through, and where it sits in the z-order.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\inspect-window.ps1 -Title PinSpike
param([string]$Title = 'Desktop Monitor')

Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
}
'@

$h = [Win]::FindWindow($null, $Title)
if ($h -eq [IntPtr]::Zero) { Write-Output "Window '$Title' not found"; exit 1 }
$owner = [Win]::GetWindow($h, 4)   # GW_OWNER
$ex = [Win]::GetWindowLongPtr($h, -20).ToInt64()
Write-Output ("visible:       " + [Win]::IsWindowVisible($h))
Write-Output ("owner class:   " + $(if ($owner -eq [IntPtr]::Zero) { '(none)' } else { [Win]::ClassOf($owner) }))
Write-Output ("click-through: " + (($ex -band 0x20) -ne 0))
Write-Output ("tool window:   " + (($ex -band 0x80) -ne 0))
Write-Output ("no-activate:   " + (($ex -band 0x08000000) -ne 0))

$visible = @()
$w = [Win]::GetTopWindow([IntPtr]::Zero)
while ($w -ne [IntPtr]::Zero) {
    if ([Win]::IsWindowVisible($w)) { $visible += , @($w, [Win]::ClassOf($w)) }
    $w = [Win]::GetWindow($w, 2)   # GW_HWNDNEXT
}
$i = 0
foreach ($e in $visible) { if ($e[0] -eq $h) { break }; $i++ }
Write-Output "z-position:    $i of $($visible.Count) visible top-level windows (0 = top)"
Write-Output ("below it:      " + (($visible | Select-Object -Skip ($i + 1) | ForEach-Object { $_[1] }) -join ', '))
```

- [ ] **Step 9: Build the spike**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Spike`
Expected: `Built bin\PinSpike.exe`

- [ ] **Step 10: Run owner mode and inspect it**

Run:
```powershell
Start-Process .\bin\PinSpike.exe; Start-Sleep -Seconds 2
powershell -NoProfile -ExecutionPolicy Bypass -File tools\inspect-window.ps1 -Title PinSpike
```
Expected: `visible: True`, `owner class: Progman` (or `WorkerW`), `click-through: True`, `tool window: True`, `no-activate: True`, and `below it:` lists only shell classes such as `Progman` / `WorkerW` (no app windows).

- [ ] **Step 11: Check owner mode on screen**

Ask the user to check these and say which pass. You can also check with a screenshot where the computer-use tools are available:
- A. A dark card reading "Pin spike / owner mode: True" sits at top-right of the desktop.
- B. Opening or maximising any app window covers the card (the card stays behind apps).
- C. Pressing Win+D shows the desktop and the card is still visible.
- D. With the desktop showing, clicking a desktop icon that lies under the card selects the icon. Move an icon under the card first if none is there.

- [ ] **Step 12: Check that the card survives an Explorer restart (ask first)**

Ask the user: "May I restart Explorer? The taskbar will flicker and any open File Explorer windows will close." Only on a yes:
```powershell
Stop-Process -Name explorer -Force; Start-Sleep -Seconds 8
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe; Start-Sleep -Seconds 5 }
Start-Sleep -Seconds 5
powershell -NoProfile -ExecutionPolicy Bypass -File tools\inspect-window.ps1 -Title PinSpike
Get-Content "$env:APPDATA\DesktopMonitor\log.txt" -Tail 5
```
Expected: the window is still found, `visible: True`, `owner class: Progman` (or `WorkerW`). The log shows `DesktopPin: Explorer restarted, re-attaching` or a watchdog re-attach. If the window is gone (`Window 'PinSpike' not found`), record it: Task 5's `AppController` recreates a destroyed card window, so this is survivable but must be written in RESULT.md.

- [ ] **Step 13: Check unlocked dragging**

Run:
```powershell
Stop-Process -Name PinSpike -ErrorAction SilentlyContinue
Start-Process .\bin\PinSpike.exe -ArgumentList '--unlocked'
```
Ask the user to drag the card with the mouse (desktop visible). Expected: it moves and stays where it's dropped. Then `Stop-Process -Name PinSpike`.

- [ ] **Step 14: Fall back if owner mode failed**

If any of A–D failed in Step 11, repeat Steps 10–11 with `Start-Process .\bin\PinSpike.exe -ArgumentList '--no-owner'`. Expected for this fallback: `owner class: (none)` and A, B, D pass; C (Win+D) may fail, which the spec accepts. If A or B fail in **both** modes, stop and report to the user: the spec's desktop-layer design does not work on this PC and needs a decision.

- [ ] **Step 15: Record the outcome in `spike\RESULT.md`**

Fill in the actual observations (Pass/Fail per check and any notes) in this exact structure:

```markdown
# Desktop-pin spike result (2026-10-02)

| Check | Owner mode | No-owner mode |
|---|---|---|
| A. Visible on desktop, top-right | Pass | not run |
| B. Behind app windows | Pass | not run |
| C. Visible after Win+D | Pass | not run |
| D. Click passes through to desktop icon | Pass | not run |
| Explorer restart: card re-attached | Pass | not run |
| Unlocked drag works | Pass | n/a |

Decision: UseDesktopOwner = true

Notes: <anything unexpected, e.g. owner class seen, log lines>
```

- [ ] **Step 16: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add .gitignore build.ps1 src/Native.cs src/Log.cs src/DesktopPin.cs spike tools/inspect-window.ps1 && git commit -m "Add build script and desktop-pin spike

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Rules and test runner

**Files:**
- Create: `tests\TestMain.cs`, `tests\RulesTests.cs`, `src\Rules.cs`

**Interfaces:**
- Consumes: `Log.FilePath` (Task 1).
- Produces:
  - `public enum Level { Unknown, Ok, Amber, Red }`
  - `public struct Box { double X, Y, W, H; Box(double x, double y, double w, double h) }`
  - `public static class Rules`: `double Display(double)`, `Level Classify(double? value, double amber, double red)`, `double? ThermalToCelsius(double raw, bool tenthsOfKelvin)`, `double ClampPercent(double)`, `double? Percent(double part, double whole)`, `double? Rate(long previous, long current, double seconds)`, `double? BatteryPercent(float lifePercent)`, `double TempRingFraction(double? celsius, double min, double max)`, `string FormatNumber(double?)`, `string FormatPercent(double?)`, `string FormatTemp(double? celsius, bool withUnit)`, `string FormatSpeed(double? bytesPerSecond)`, `string Tooltip(double? cpuPercent, double? cpuTempC)`, `bool IsMostlyOnScreen(Box card, IList<Box> workAreas)`, `Box DefaultPlacement(Box workArea, double cardW, double cardH, double margin)`.
  - Test helpers `TestMain.Equal<T>`, `TestMain.Near`, `TestMain.True`, `TestMain.Throws<TException>`.

- [ ] **Step 1: Write the test runner `tests\TestMain.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopMonitor.Tests
{
    // Minimal test runner (no framework available with the built-in compiler). build.ps1 -Test compiles and runs it.
    internal static class TestMain
    {
        private static int _passed, _failed;

        private static int Main()
        {
            Log.FilePath = Path.Combine(Path.GetTempPath(), "DesktopMonitorTests.log");
            RulesTests.Run();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        public static void Equal<T>(T expected, T actual, string name)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual)) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL " + name + ": expected <" + Show(expected) + "> but got <" + Show(actual) + ">");
        }

        public static void Near(double expected, double? actual, string name)
        {
            if (actual.HasValue && Math.Abs(expected - actual.Value) < 1e-6) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL " + name + ": expected <" + expected + "> but got <" + Show(actual) + ">");
        }

        public static void True(bool condition, string name)
        {
            Equal(true, condition, name);
        }

        public static void Throws<TException>(Action action, string name) where TException : Exception
        {
            try
            {
                action();
                _failed++;
                Console.WriteLine("FAIL " + name + ": expected " + typeof(TException).Name + " but nothing was thrown");
            }
            catch (TException)
            {
                _passed++;
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + ": expected " + typeof(TException).Name + " but got " + ex.GetType().Name);
            }
        }

        private static string Show(object o)
        {
            return o == null ? "null" : o.ToString();
        }
    }
}
```

- [ ] **Step 2: Write the failing tests `tests\RulesTests.cs`**

```csharp
using System.Collections.Generic;

namespace DesktopMonitor.Tests
{
    internal static class RulesTests
    {
        public static void Run()
        {
            Classify_ThresholdsAreInclusive();
            Classify_UsesTheDisplayedRounding();
            ThermalToCelsius_ConvertsAndRejectsEmptyZones();
            Percent_And_Clamp();
            Rate_RejectsCountersThatGoBackwards();
            BatteryPercent_RejectsUnknownCharge();
            TempRingFraction_MapsAndClamps();
            Formatting();
            FormatSpeed_SwitchesUnitsAt1000KB();
            Tooltip_Text();
            IsMostlyOnScreen_Cases();
            DefaultPlacement_TopRight();
        }

        private static void Classify_ThresholdsAreInclusive()
        {
            TestMain.Equal(Level.Ok, Rules.Classify(79.0, 80, 85), "temp 79 ok");
            TestMain.Equal(Level.Amber, Rules.Classify(80.0, 80, 85), "temp 80 amber");
            TestMain.Equal(Level.Amber, Rules.Classify(84.0, 80, 85), "temp 84 amber");
            TestMain.Equal(Level.Red, Rules.Classify(85.0, 80, 85), "temp 85 red");
            TestMain.Equal(Level.Ok, Rules.Classify(69.0, 70, 85), "usage 69 ok");
            TestMain.Equal(Level.Amber, Rules.Classify(70.0, 70, 85), "usage 70 amber");
            TestMain.Equal(Level.Ok, Rules.Classify(89.0, 90, 95), "disk 89 ok");
            TestMain.Equal(Level.Amber, Rules.Classify(90.0, 90, 95), "disk 90 amber");
            TestMain.Equal(Level.Red, Rules.Classify(95.0, 90, 95), "disk 95 red");
            TestMain.Equal(Level.Unknown, Rules.Classify(null, 80, 85), "missing value unknown");
        }

        // Review focus 1: a value shown as "80°" must never be drawn in the OK colour.
        private static void Classify_UsesTheDisplayedRounding()
        {
            TestMain.Equal("79\u00B0", Rules.FormatTemp(79.4, false), "79.4 displays 79");
            TestMain.Equal(Level.Ok, Rules.Classify(79.4, 80, 85), "79.4 classified ok");
            TestMain.Equal("80\u00B0", Rules.FormatTemp(79.5, false), "79.5 displays 80");
            TestMain.Equal(Level.Amber, Rules.Classify(79.5, 80, 85), "79.5 classified amber");
            TestMain.Equal("90%", Rules.FormatPercent(89.6), "disk 89.6 displays 90%");
            TestMain.Equal(Level.Amber, Rules.Classify(89.6, 90, 95), "disk 89.6 classified amber");
        }

        // Review focus 5: an empty thermal zone (273.2 K) must show "--", not "0°C".
        private static void ThermalToCelsius_ConvertsAndRejectsEmptyZones()
        {
            TestMain.Near(86.0, Rules.ThermalToCelsius(3592, true), "high precision tenths of K");
            TestMain.Near(85.8, Rules.ThermalToCelsius(359, false), "plain Kelvin");
            TestMain.Equal<double?>(null, Rules.ThermalToCelsius(2732, true), "empty zone 273.2 K");
            TestMain.Equal<double?>(null, Rules.ThermalToCelsius(0, false), "zero Kelvin");
            TestMain.Equal<double?>(null, Rules.ThermalToCelsius(5000, true), "500 K is not a reading");
        }

        private static void Percent_And_Clamp()
        {
            TestMain.Near(25, Rules.Percent(50, 200), "50 of 200");
            TestMain.Equal<double?>(null, Rules.Percent(1, 0), "zero total");
            TestMain.Near(100, Rules.Percent(150, 100), "over 100 clamps");
            TestMain.Near(0, Rules.ClampPercent(-3), "negative clamps to 0");
            TestMain.Near(100, Rules.ClampPercent(130), "CPU utility above 100 clamps");
            TestMain.Near(0, Rules.ClampPercent(double.NaN), "NaN clamps to 0");
        }

        // Review focus 3: Wi-Fi switch or VPN toggle makes the byte totals drop; no negative or huge speed.
        private static void Rate_RejectsCountersThatGoBackwards()
        {
            TestMain.Near(1000, Rules.Rate(1000, 3000, 2.0), "2000 bytes in 2 s");
            TestMain.Equal<double?>(null, Rules.Rate(5000, 1000, 1.0), "total went backwards");
            TestMain.Equal<double?>(null, Rules.Rate(0, 100, 0), "no time elapsed");
        }

        // Review focus 5: unknown charge (255 %) must show "--".
        private static void BatteryPercent_RejectsUnknownCharge()
        {
            TestMain.Near(99, Rules.BatteryPercent(0.99f), "99 %");
            TestMain.Near(0, Rules.BatteryPercent(0f), "empty battery");
            TestMain.Equal<double?>(null, Rules.BatteryPercent(2.55f), "unknown charge");
        }

        private static void TempRingFraction_MapsAndClamps()
        {
            TestMain.Near(0.5, Rules.TempRingFraction(65, 30, 100), "65 is halfway");
            TestMain.Near(0, Rules.TempRingFraction(20, 30, 100), "below min");
            TestMain.Near(1, Rules.TempRingFraction(120, 30, 100), "above max");
            TestMain.Near(0, Rules.TempRingFraction(null, 30, 100), "missing");
            TestMain.Near(0, Rules.TempRingFraction(50, 100, 30), "inverted range");
        }

        private static void Formatting()
        {
            TestMain.Equal("23%", Rules.FormatPercent(23.4), "percent rounds");
            TestMain.Equal("--", Rules.FormatPercent(null), "percent missing");
            TestMain.Equal("77\u00B0C", Rules.FormatTemp(77.2, true), "temp with unit");
            TestMain.Equal("77\u00B0", Rules.FormatTemp(77.2, false), "temp without unit");
            TestMain.Equal("--", Rules.FormatTemp(null, true), "temp missing");
            TestMain.Equal("--", Rules.FormatNumber(null), "number missing");
            TestMain.Equal("100", Rules.FormatNumber(99.5), "number rounds half up");
        }

        private static void FormatSpeed_SwitchesUnitsAt1000KB()
        {
            TestMain.Equal("--", Rules.FormatSpeed(null), "missing");
            TestMain.Equal("0 KB/s", Rules.FormatSpeed(0), "idle");
            TestMain.Equal("1 KB/s", Rules.FormatSpeed(512), "half a KB rounds up");
            TestMain.Equal("999 KB/s", Rules.FormatSpeed(999 * 1024), "999 KB/s");
            TestMain.Equal("1.0 MB/s", Rules.FormatSpeed(999.6 * 1024), "rounds into MB, never 1000 KB/s");
            TestMain.Equal("1.5 MB/s", Rules.FormatSpeed(1.5 * 1048576), "1.5 MB/s");
            TestMain.Equal("12.3 MB/s", Rules.FormatSpeed(12.34 * 1048576), "one decimal");
        }

        private static void Tooltip_Text()
        {
            TestMain.Equal("CPU 23% \u00B7 77\u00B0C", Rules.Tooltip(23.4, 77.2), "tooltip");
            TestMain.Equal("CPU -- \u00B7 --", Rules.Tooltip(null, null), "tooltip missing");
        }

        // Review focus 4: projector unplugged or resolution changed; card must not stay off-screen.
        private static void IsMostlyOnScreen_Cases()
        {
            var laptop = new List<Box> { new Box(0, 0, 1366, 728) };
            TestMain.True(Rules.IsMostlyOnScreen(new Box(1104, 12, 250, 204), laptop), "default spot is on screen");
            TestMain.True(!Rules.IsMostlyOnScreen(new Box(1300, 12, 250, 204), laptop), "mostly past right edge");
            TestMain.True(!Rules.IsMostlyOnScreen(new Box(3000, 100, 250, 204), laptop), "saved on a monitor that is gone");
            var twoScreens = new List<Box> { new Box(0, 0, 1366, 728), new Box(1366, 0, 1920, 1040) };
            TestMain.True(Rules.IsMostlyOnScreen(new Box(1300, 12, 250, 204), twoScreens), "straddling, most on second screen");
            TestMain.True(!Rules.IsMostlyOnScreen(new Box(0, 0, 0, 0), laptop), "zero-size card");
        }

        private static void DefaultPlacement_TopRight()
        {
            Box p = Rules.DefaultPlacement(new Box(0, 0, 1366, 728), 250, 204, 12);
            TestMain.Near(1104, p.X, "x is 12 px from the right edge");
            TestMain.Near(12, p.Y, "y is 12 px from the top");
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0103: The name 'Rules' does not exist in the current context` (and CS0246 for `Level` / `Box`), then `Build of Tests.exe failed`.

- [ ] **Step 4: Implement `src\Rules.cs`**

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

        // Bytes per second from two cumulative counters; null when the total went backwards (adapter reset) or no time passed.
        public static double? Rate(long previous, long current, double seconds)
        {
            if (seconds <= 0 || current < previous) return null;
            return (current - previous) / seconds;
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
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `Built bin\Tests.exe` then `61 passed, 0 failed`, exit code 0.

- [ ] **Step 6: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add tests/TestMain.cs tests/RulesTests.cs src/Rules.cs && git commit -m "Add display rules with unit tests

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Settings load, save and live reload

**Files:**
- Create: `tests\SettingsTests.cs`, `src\Settings.cs`
- Modify: `tests\TestMain.cs` (add the `SettingsTests.Run()` call)

**Interfaces:**
- Consumes: `Log.Write`, `Log.DataDir` (Task 1); `TestMain` helpers (Task 2).
- Produces:
  - `public sealed class AppSettings` with public fields `double? X, Y; double Opacity, UsageAmber, UsageRed, TempAmber, TempRed, DiskAmber, DiskRed, TempRingMin, TempRingMax; string ZoneCpu, ZoneSkin, ZoneBattery;` and `AppSettings Clone()`.
  - `public static class SettingsStore`: `string DefaultPath { get; }`, `AppSettings Parse(string json)` (throws `FormatException`), `string ToJson(AppSettings)`, `AppSettings LoadOrCreate(string path)`, `void Save(string path, AppSettings s)`.
  - `public sealed class SettingsWatcher : IDisposable`: `SettingsWatcher(string path)`, `event Action<AppSettings> Changed` (raised on a thread-pool thread).

- [ ] **Step 1: Write the failing tests `tests\SettingsTests.cs`**

```csharp
using System;
using System.IO;

namespace DesktopMonitor.Tests
{
    internal static class SettingsTests
    {
        public static void Run()
        {
            Parse_EmptyObjectGivesDefaults();
            Parse_ReadsEveryKey();
            Parse_KeysAreCaseInsensitive();
            Parse_WrongTypeFallsBackToDefault();
            Parse_ClampsOutOfRangeNumbers();
            Parse_InvertedRingRangeUsesDefaults();
            Parse_MalformedJsonThrowsFormatException();
            ToJson_RoundTrips();
            LoadOrCreate_WritesDefaultsWhenMissing();
            LoadOrCreate_MalformedFileGivesDefaultsAndLeavesFileAlone();
        }

        private static void Parse_EmptyObjectGivesDefaults()
        {
            AppSettings s = SettingsStore.Parse("{}");
            TestMain.Equal<double?>(null, s.X, "x default null");
            TestMain.Equal<double?>(null, s.Y, "y default null");
            TestMain.Near(0.93, s.Opacity, "opacity default");
            TestMain.Near(70, s.UsageAmber, "usageAmber default");
            TestMain.Near(85, s.UsageRed, "usageRed default");
            TestMain.Near(80, s.TempAmber, "tempAmber default");
            TestMain.Near(85, s.TempRed, "tempRed default");
            TestMain.Near(90, s.DiskAmber, "diskAmber default");
            TestMain.Near(95, s.DiskRed, "diskRed default");
            TestMain.Near(30, s.TempRingMin, "tempRingMin default");
            TestMain.Near(100, s.TempRingMax, "tempRingMax default");
            TestMain.Equal("CPUZ", s.ZoneCpu, "zoneCpu default");
            TestMain.Equal("SK1Z", s.ZoneSkin, "zoneSkin default");
            TestMain.Equal("BATZ", s.ZoneBattery, "zoneBattery default");
        }

        private static void Parse_ReadsEveryKey()
        {
            AppSettings s = SettingsStore.Parse(@"{""x"": 100.5, ""y"": 40, ""opacity"": 0.8,
                ""usageAmber"": 60, ""usageRed"": 90, ""tempAmber"": 75, ""tempRed"": 88,
                ""diskAmber"": 80, ""diskRed"": 97, ""tempRingMin"": 20, ""tempRingMax"": 110,
                ""zoneCpu"": ""LOCZ"", ""zoneSkin"": ""EXTZ"", ""zoneBattery"": ""CHGZ""}");
            TestMain.Near(100.5, s.X, "x");
            TestMain.Near(40, s.Y, "y");
            TestMain.Near(0.8, s.Opacity, "opacity");
            TestMain.Near(60, s.UsageAmber, "usageAmber");
            TestMain.Near(90, s.UsageRed, "usageRed");
            TestMain.Near(75, s.TempAmber, "tempAmber");
            TestMain.Near(88, s.TempRed, "tempRed");
            TestMain.Near(80, s.DiskAmber, "diskAmber");
            TestMain.Near(97, s.DiskRed, "diskRed");
            TestMain.Near(20, s.TempRingMin, "tempRingMin");
            TestMain.Near(110, s.TempRingMax, "tempRingMax");
            TestMain.Equal("LOCZ", s.ZoneCpu, "zoneCpu");
            TestMain.Equal("EXTZ", s.ZoneSkin, "zoneSkin");
            TestMain.Equal("CHGZ", s.ZoneBattery, "zoneBattery");
        }

        private static void Parse_KeysAreCaseInsensitive()
        {
            AppSettings s = SettingsStore.Parse(@"{""TempAmber"": 78, ""ZONECPU"": ""LOCZ""}");
            TestMain.Near(78, s.TempAmber, "TempAmber");
            TestMain.Equal("LOCZ", s.ZoneCpu, "ZONECPU");
        }

        // Review focus 2: a typo in one key must not reset or break the others.
        private static void Parse_WrongTypeFallsBackToDefault()
        {
            AppSettings s = SettingsStore.Parse(@"{""tempAmber"": ""eighty"", ""tempRed"": 90, ""x"": ""left"", ""zoneCpu"": 5, ""zoneSkin"": ""  ""}");
            TestMain.Near(80, s.TempAmber, "string where number expected");
            TestMain.Near(90, s.TempRed, "neighbouring key still read");
            TestMain.Equal<double?>(null, s.X, "non-numeric x");
            TestMain.Equal("CPUZ", s.ZoneCpu, "number where string expected");
            TestMain.Equal("SK1Z", s.ZoneSkin, "blank zone");
        }

        private static void Parse_ClampsOutOfRangeNumbers()
        {
            AppSettings s = SettingsStore.Parse(@"{""opacity"": 1.5, ""usageRed"": 120, ""diskAmber"": -5, ""tempRed"": 400}");
            TestMain.Near(1.0, s.Opacity, "opacity max 1");
            TestMain.Near(100, s.UsageRed, "usage max 100");
            TestMain.Near(0, s.DiskAmber, "disk min 0");
            TestMain.Near(150, s.TempRed, "temp max 150");
            TestMain.Near(0.2, SettingsStore.Parse(@"{""opacity"": 0}").Opacity, "opacity min 0.2 so the card never vanishes");
        }

        private static void Parse_InvertedRingRangeUsesDefaults()
        {
            AppSettings s = SettingsStore.Parse(@"{""tempRingMin"": 90, ""tempRingMax"": 40}");
            TestMain.Near(30, s.TempRingMin, "ring min reset");
            TestMain.Near(100, s.TempRingMax, "ring max reset");
        }

        // Review focus 2: a half-saved or broken file is rejected as a whole so the caller keeps the last good settings.
        private static void Parse_MalformedJsonThrowsFormatException()
        {
            TestMain.Throws<FormatException>(() => SettingsStore.Parse("{ \"tempAmber\": 80,"), "truncated file");
            TestMain.Throws<FormatException>(() => SettingsStore.Parse("tempAmber = 80"), "not JSON");
            TestMain.Throws<FormatException>(() => SettingsStore.Parse("[1, 2]"), "array instead of object");
            TestMain.Throws<FormatException>(() => SettingsStore.Parse(""), "empty file");
        }

        private static void ToJson_RoundTrips()
        {
            var original = new AppSettings();
            original.X = 1104;
            original.Y = 12.5;
            original.Opacity = 0.85;
            original.TempAmber = 78;
            original.ZoneCpu = "A\"B";
            AppSettings back = SettingsStore.Parse(SettingsStore.ToJson(original));
            TestMain.Near(1104, back.X, "x");
            TestMain.Near(12.5, back.Y, "y");
            TestMain.Near(0.85, back.Opacity, "opacity");
            TestMain.Near(78, back.TempAmber, "tempAmber");
            TestMain.Equal("A\"B", back.ZoneCpu, "quote escaped");
            TestMain.True(SettingsStore.ToJson(new AppSettings()).Contains("\"x\": null"), "default x written as null");
        }

        private static void LoadOrCreate_WritesDefaultsWhenMissing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "DesktopMonitorTests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "settings.json");
            try
            {
                AppSettings s = SettingsStore.LoadOrCreate(path);
                TestMain.Near(80, s.TempAmber, "defaults returned");
                TestMain.True(File.Exists(path), "file created");
                TestMain.Near(90, SettingsStore.Parse(File.ReadAllText(path)).DiskAmber, "created file parses");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        private static void LoadOrCreate_MalformedFileGivesDefaultsAndLeavesFileAlone()
        {
            string dir = Path.Combine(Path.GetTempPath(), "DesktopMonitorTests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "settings.json");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "{ broken");
                AppSettings s = SettingsStore.LoadOrCreate(path);
                TestMain.Near(0.93, s.Opacity, "defaults used");
                TestMain.Equal("{ broken", File.ReadAllText(path), "user's file not overwritten");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
```

- [ ] **Step 2: Register the new tests in `tests\TestMain.cs`**

Replace:
```csharp
            RulesTests.Run();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
```
with:
```csharp
            RulesTests.Run();
            SettingsTests.Run();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: build fails with `error CS0246: The type or namespace name 'AppSettings' could not be found` and `error CS0103: The name 'SettingsStore' does not exist`.

- [ ] **Step 4: Implement `src\Settings.cs`**

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

            var map = new Dictionary<string, object>(raw, StringComparer.OrdinalIgnoreCase);
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
            sb.Append("  \"zoneBattery\": ").Append(js.Serialize(s.ZoneBattery)).Append("\r\n");
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
            }
        }

        public void Dispose()
        {
            _fsw.Dispose();
            _debounce.Dispose();
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test`
Expected: `118 passed, 0 failed`, exit code 0.

- [ ] **Step 6: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add tests/SettingsTests.cs tests/TestMain.cs src/Settings.cs && git commit -m "Add settings load, save and live reload

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Metrics sampler

Hardware readers are verified against live data rather than unit tests: the pure parts they use (conversion, clamping, rates) are already tested in Task 2.

**Files:**
- Create: `src\Snapshot.cs`, `src\MetricsSampler.cs`, `tools\SamplerDump.cs`

**Interfaces:**
- Consumes: `Rules.*`, `Level` (Task 2); `AppSettings` (Task 3); `Native.MEMORYSTATUSEX`, `Native.GlobalMemoryStatusEx`, `Log.Write` (Task 1).
- Produces:
  - `public sealed class Snapshot` with public fields `DateTime Time; double? CpuPercent, RamPercent, GpuPercent, DiskPercent, DownBytesPerSec, UpBytesPerSec, BatteryPercent, CpuTempC, SkinTempC, BatteryTempC; bool OnAc;`.
  - `internal sealed class MetricsSampler : IDisposable`: `MetricsSampler(AppSettings)`, `Snapshot Sample()`, `void ApplySettings(AppSettings)`, `void Reset()`.

- [ ] **Step 1: Create `src\Snapshot.cs`**

```csharp
using System;

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
        public double? DownBytesPerSec;
        public double? UpBytesPerSec;
        public double? BatteryPercent;
        public bool OnAc;
        public double? CpuTempC;
        public double? SkinTempC;
        public double? BatteryTempC;
    }
}
```

- [ ] **Step 2: Create `src\MetricsSampler.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopMonitor
{
    // Reads every metric (spec section 4). Each reader is isolated: a failure is logged once, yields null
    // until it recovers, and never escapes Sample().
    internal sealed class MetricsSampler : IDisposable
    {
        private const string ThermalCategory = "Thermal Zone Information";
        private const double TempEvery = 2, BatteryEvery = 10, DiskEvery = 60; // seconds

        private readonly HashSet<string> _failing = new HashSet<string>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private AppSettings _settings;

        private PerformanceCounter _cpu;
        private Dictionary<string, CounterSample> _gpuPrev;
        private long _rxPrev, _txPrev;
        private double _netPrevAt = -1;

        private PerformanceCounter _tCpu, _tSkin, _tBattery;
        private bool _thermalOpen, _thermalTenths;
        private double _tempAt = -1, _batteryAt = -1, _diskAt = -1;
        private double? _cpuTemp, _skinTemp, _batteryTemp, _battery, _disk;
        private bool _onAc;

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
            _gpuPrev = null;
            _netPrevAt = -1;
            CloseThermal();
            _batteryAt = -1;
            _diskAt = -1;
        }

        public Snapshot Sample()
        {
            double now = _clock.Elapsed.TotalSeconds;
            var s = new Snapshot();
            s.CpuPercent = Read("cpu", ReadCpu, CloseCpu);
            s.RamPercent = Read("ram", ReadRam, null);
            s.GpuPercent = Read("gpu", ReadGpu, delegate { _gpuPrev = null; });
            ReadNetwork(s, now);
            if (Due(ref _tempAt, now, TempEvery)) ReadTemps();
            if (Due(ref _batteryAt, now, BatteryEvery)) ReadBattery();
            if (Due(ref _diskAt, now, DiskEvery)) _disk = Read("disk", ReadDisk, null);
            s.CpuTempC = _cpuTemp;
            s.SkinTempC = _skinTemp;
            s.BatteryTempC = _batteryTemp;
            s.BatteryPercent = _battery;
            s.OnAc = _onAc;
            s.DiskPercent = _disk;
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
                long rx = 0, tx = 0;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceStatistics st = ni.GetIPStatistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
                if (_netPrevAt >= 0)
                {
                    s.DownBytesPerSec = Rules.Rate(_rxPrev, rx, now - _netPrevAt);
                    s.UpBytesPerSec = Rules.Rate(_txPrev, tx, now - _netPrevAt);
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
                Ok("battery");
            }
            catch (Exception ex)
            {
                Fail("battery", ex);
                _battery = null;
            }
        }

        private static double? ReadDisk()
        {
            var c = new DriveInfo("C");
            return Rules.Percent(c.TotalSize - c.TotalFreeSpace, c.TotalSize);
        }

        public void Dispose()
        {
            CloseCpu();
            CloseThermal();
        }
    }
}
```

- [ ] **Step 3: Create `tools\SamplerDump.cs`**

```csharp
using System;
using System.Threading;

namespace DesktopMonitor
{
    // Prints live readings once a second for comparison with Task Manager (plan Task 4).
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
                        + "  ram " + Rules.FormatPercent(s.RamPercent)
                        + "  gpu " + Rules.FormatPercent(s.GpuPercent)
                        + "  disk " + Rules.FormatPercent(s.DiskPercent)
                        + "  down " + Rules.FormatSpeed(s.DownBytesPerSec)
                        + "  up " + Rules.FormatSpeed(s.UpBytesPerSec)
                        + "  battery " + Rules.FormatPercent(s.BatteryPercent) + (s.OnAc ? " AC" : "")
                        + "  temps cpu " + Rules.FormatTemp(s.CpuTempC, true)
                        + " skin " + Rules.FormatTemp(s.SkinTempC, true)
                        + " battery " + Rules.FormatTemp(s.BatteryTempC, true));
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
```

- [ ] **Step 4: Build the dump tool and confirm the tests still pass**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Dump
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
```
Expected: `Built bin\SamplerDump.exe`, then `118 passed, 0 failed`.

- [ ] **Step 5: Read live values**

Run: `.\bin\SamplerDump.exe 10`
Expected, shape as below (numbers vary): the first line has `cpu --`, `gpu --`, `down --`, `up --` (rate counters priming); later lines have values. Temperatures are plausible: CPU 40–100 °C, skin 30–70 °C, battery 20–50 °C. Disk is about 87 %.
```
03:16:58  cpu 22%  ram 78%  gpu 1%  disk 87%  down 98 KB/s  up 8 KB/s  battery 100% AC  temps cpu 92°C skin 58°C battery 38°C
```

- [ ] **Step 6: Cross-check against Windows**

Run both while SamplerDump runs for 20 s (`.\bin\SamplerDump.exe 20` in one terminal):
```powershell
(Get-Counter '\Thermal Zone Information(*)\High Precision Temperature').CounterSamples | Where-Object { $_.InstanceName -match 'cpuz|sk1z|batz' } | ForEach-Object { "{0}: {1:N1} C" -f $_.InstanceName, ($_.CookedValue / 10 - 273.2) }
$end = (Get-Date).AddSeconds(10); $jobs = 1..4 | ForEach-Object { Start-Job { while ((Get-Date) -lt $using:end) { } } }; $jobs | Wait-Job | Remove-Job
```
Expected: temperatures match the dump to within 1 °C. During the 10 s load, `cpu` rises well above idle (typically above 50 %). Ask the user to glance at Task Manager → Performance: CPU, Memory, GPU 0 and Wi-Fi/Ethernet values should be in the same range as the dump.

- [ ] **Step 7: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add src/Snapshot.cs src/MetricsSampler.cs tools/SamplerDump.cs && git commit -m "Add metrics sampler and live dump tool

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Card, window, tray and app wiring

**Files:**
- Create: `app.manifest`, `src\CardView.cs`, `src\CardWindow.cs`, `src\TrayIcon.cs`, `src\Program.cs`

**Interfaces:**
- Consumes: everything above. Specifically `DesktopPin(IntPtr, bool)` + `Attach()`; `Rules.Classify/Format*/TempRingFraction/Tooltip/IsMostlyOnScreen/DefaultPlacement`; `AppSettings`, `SettingsStore.DefaultPath/LoadOrCreate/Save`, `SettingsWatcher.Changed`; `MetricsSampler.Sample/ApplySettings/Reset`; `Snapshot`; `Native.*`; the decision in `spike\RESULT.md`.
- Produces: `bin\DesktopMonitor.exe`, plus:
  - `CardView : FrameworkElement` — `const double CardWidth`, `static double CardHeight(bool unlocked)`, `bool Unlocked`, `void Update(Snapshot)`, `void ApplySettings(AppSettings)`.
  - `CardWindow : Window` — `CardWindow(AppSettings, bool useOwner)`, `CardView Card`, `bool Unlocked`, `void SetUnlocked(bool)`, `void ApplySettings(AppSettings)`, events `Action<double,double> PositionCommitted`, `Action<bool> UnlockedChanged`.
  - `TrayIcon : IDisposable` — `TrayIcon(string settingsPath)`, `void Update(Snapshot, AppSettings)`, `void SetUnlocked(bool)`, events `Action UnlockToggled`, `Action ExitRequested`; `static class Autostart { bool IsEnabled(string exePath); void Set(bool enabled, string exePath); }`.

- [ ] **Step 1: Create `app.manifest`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="DesktopMonitor"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 and 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 2: Create `src\CardView.cs`**

Layout (DIPs): padding 14; header row at y 14; rings top at y 38 (64 px, radius 25, stroke 6, centres at x 46 / 125 / 204); ring labels at y 104; grid rows at y 130 / 152 / 174 in two 104 px columns at x 14 and 132; height 204, or 226 when unlocked (hint at y 202).

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Draws the whole gauge card from the latest Snapshot (spec section 3; approved preview style B).
    internal sealed class CardView : FrameworkElement
    {
        public const double CardWidth = 250;
        private const double Pad = 14, RingSize = 64, RingRadius = 25, RingStroke = 6;
        private const double ColGap = 14, RowHeight = 16, RowGap = 6;

        private static readonly Brush TextBrush = Solid(0xEC, 0xEC, 0xEC);
        private static readonly Brush LabelBrush = Solid(0xA3, 0xA3, 0xA3);
        private static readonly Brush OkBrush = Solid(0x5D, 0xCA, 0xA5);
        private static readonly Brush AmberBrush = Solid(0xFA, 0xC7, 0x75);
        private static readonly Brush RedBrush = Solid(0xF0, 0x95, 0x95);
        private static readonly Pen TrackPen = FrozenPen(Color.FromArgb(33, 255, 255, 255), RingStroke); // white at 13 %
        private static readonly Pen OutlinePen = FrozenDashedPen();
        private static readonly Typeface Face = new Typeface("Segoe UI");

        private Snapshot _snap = new Snapshot();
        private AppSettings _settings = new AppSettings();
        private bool _unlocked;
        private double _pixelsPerDip = 1.0;

        public CardView()
        {
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale); // ClearType cannot render on a transparent window
            Loaded += delegate { _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; InvalidateVisual(); };
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

        public void ApplySettings(AppSettings s)
        {
            _settings = s;
            InvalidateVisual();
        }

        public static double CardHeight(bool unlocked)
        {
            return unlocked ? 226 : 204;
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
            DrawLeft(dc, "System", 12, LabelBrush, Pad, y);
            DrawRight(dc, s.Time.ToString("HH:mm", CultureInfo.InvariantCulture), 12, LabelBrush, CardWidth - Pad, y);
            y += RowHeight + 8;

            double cpuFraction = s.CpuPercent.HasValue ? s.CpuPercent.Value / 100 : 0;
            double ramFraction = s.RamPercent.HasValue ? s.RamPercent.Value / 100 : 0;
            DrawRing(dc, Pad + RingSize / 2, y, cpuFraction, Rules.FormatPercent(s.CpuPercent), "CPU", Rules.Classify(s.CpuPercent, st.UsageAmber, st.UsageRed));
            DrawRing(dc, CardWidth / 2, y, ramFraction, Rules.FormatPercent(s.RamPercent), "RAM", Rules.Classify(s.RamPercent, st.UsageAmber, st.UsageRed));
            DrawRing(dc, CardWidth - Pad - RingSize / 2, y, Rules.TempRingFraction(s.CpuTempC, st.TempRingMin, st.TempRingMax),
                Rules.FormatTemp(s.CpuTempC, false), "CPU temp", Rules.Classify(s.CpuTempC, st.TempAmber, st.TempRed));
            y += RingSize + 2 + RowHeight + 10;

            double colW = (CardWidth - 2 * Pad - ColGap) / 2;
            double x1 = Pad, x2 = Pad + colW + ColGap;
            Cell(dc, x1, y, colW, "GPU", Rules.FormatPercent(s.GpuPercent), Colour(Rules.Classify(s.GpuPercent, st.UsageAmber, st.UsageRed), TextBrush));
            Cell(dc, x2, y, colW, "Disk C:", Rules.FormatPercent(s.DiskPercent), Colour(Rules.Classify(s.DiskPercent, st.DiskAmber, st.DiskRed), TextBrush));
            y += RowHeight + RowGap;
            Cell(dc, x1, y, colW, "Net \u2193", Rules.FormatSpeed(s.DownBytesPerSec), s.DownBytesPerSec.HasValue ? TextBrush : LabelBrush);
            Cell(dc, x2, y, colW, "Net \u2191", Rules.FormatSpeed(s.UpBytesPerSec), s.UpBytesPerSec.HasValue ? TextBrush : LabelBrush);
            y += RowHeight + RowGap;
            BatteryCell(dc, x1, y, colW, s, st);
            Cell(dc, x2, y, colW, "Skin", Rules.FormatTemp(s.SkinTempC, true), Colour(Rules.Classify(s.SkinTempC, st.TempAmber, st.TempRed), OkBrush));

            if (_unlocked) DrawCentered(dc, "Drag to move \u00B7 locks when you let go", 11, TextBrush, CardWidth / 2, 202);
        }

        private void DrawRing(DrawingContext dc, double cx, double top, double fraction, string value, string label, Level level)
        {
            var center = new Point(cx, top + RingSize / 2);
            dc.DrawEllipse(null, TrackPen, center, RingRadius, RingRadius);
            if (fraction > 0)
            {
                var pen = new Pen(Colour(level, OkBrush), RingStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (fraction >= 0.999) dc.DrawEllipse(null, pen, center, RingRadius, RingRadius);
                else dc.DrawGeometry(null, pen, Arc(center, RingRadius, fraction));
            }
            FormattedText v = Text(value, 13, TextBrush);
            dc.DrawText(v, new Point(cx - v.Width / 2, center.Y - v.Height / 2));
            DrawCentered(dc, label, 12, LabelBrush, cx, top + RingSize + 2);
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
            DrawLeft(dc, "Battery", 12, LabelBrush, x, y);
            double right = x + w;
            FormattedText temp = Text(Rules.FormatTemp(s.BatteryTempC, false), 12, Colour(Rules.Classify(s.BatteryTempC, st.TempAmber, st.TempRed), OkBrush));
            right -= temp.Width;
            dc.DrawText(temp, new Point(right, y));
            if (s.OnAc)
            {
                right -= 4 + 7;
                dc.DrawGeometry(TextBrush, null, Bolt(right, y + 2.5));
            }
            FormattedText pct = Text(Rules.FormatPercent(s.BatteryPercent), 12, s.BatteryPercent.HasValue ? TextBrush : LabelBrush);
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
            DrawLeft(dc, label, 12, LabelBrush, x, y);
            DrawRight(dc, value, 12, valueBrush, x + w, y);
        }

        private static Brush Colour(Level level, Brush okBrush)
        {
            switch (level)
            {
                case Level.Ok: return okBrush;
                case Level.Amber: return AmberBrush;
                case Level.Red: return RedBrush;
                default: return LabelBrush;
            }
        }

        private FormattedText Text(string s, double size, Brush brush)
        {
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, _pixelsPerDip);
        }

        private void DrawLeft(DrawingContext dc, string s, double size, Brush brush, double x, double y)
        {
            dc.DrawText(Text(s, size, brush), new Point(x, y));
        }

        private void DrawRight(DrawingContext dc, string s, double size, Brush brush, double right, double y)
        {
            FormattedText t = Text(s, size, brush);
            dc.DrawText(t, new Point(right - t.Width, y));
        }

        private void DrawCentered(DrawingContext dc, string s, double size, Brush brush, double cx, double y)
        {
            FormattedText t = Text(s, size, brush);
            dc.DrawText(t, new Point(cx - t.Width / 2, y));
        }

        private static Brush Solid(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Color c, double thickness)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }

        private static Pen FrozenDashedPen()
        {
            var pen = new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            return pen;
        }
    }
}
```

- [ ] **Step 3: Create `src\CardWindow.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopMonitor
{
    // Borderless transparent window hosting the card (spec section 5): click-through while locked,
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
        }

        public CardView Card { get { return _card; } }

        public bool Unlocked { get { return _card.Unlocked; } }

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

        private void Place(AppSettings s)
        {
            double scale = SystemDpiScale();
            var areas = new List<Box>();
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                System.Drawing.Rectangle r = screen.WorkingArea;
                areas.Add(new Box(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale));
            }
            var saved = new Box(s.X ?? 0, s.Y ?? 0, CardView.CardWidth, CardView.CardHeight(false));
            if (s.X.HasValue && s.Y.HasValue && Rules.IsMostlyOnScreen(saved, areas))
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

- [ ] **Step 4: Create `src\TrayIcon.cs`**

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

        public TrayIcon(string settingsPath)
        {
            _settingsPath = settingsPath;
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

        public void Update(Snapshot s, AppSettings st)
        {
            string text = Rules.FormatNumber(s.CpuTempC);
            Level level = Rules.Classify(s.CpuTempC, st.TempAmber, st.TempRed);
            if (text + level != _shownKey) SetIcon(text, level);
            _icon.Text = Rules.Tooltip(s.CpuPercent, s.CpuTempC);
        }

        // 16 x 16 dark tile with the temperature number, so it reads on light and dark taskbars.
        private void SetIcon(string text, Level level)
        {
            _shownKey = text + level;
            using (var bmp = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (var bg = new SolidBrush(Color.FromArgb(0x14, 0x14, 0x16)))
                using (var fg = new SolidBrush(TrayColour(level)))
                using (var font = new Font("Segoe UI", text.Length >= 3 ? 8f : 10f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.FillRectangle(bg, 0, 0, 16, 16);
                    g.DrawString(text, font, fg, new RectangleF(0, 0, 16, 16), fmt);
                }
                IntPtr handle = bmp.GetHicon();
                Icon icon = Icon.FromHandle(handle);
                _icon.Icon = icon;
                ReleaseCurrentIcon();
                _current = icon;
                _currentHandle = handle;
            }
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

- [ ] **Step 5: Create `src\Program.cs`**

Set `UseDesktopOwner` to the value in the `Decision:` line of `spike\RESULT.md` (the code below shows `true`).

```csharp
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopMonitor
{
    internal static class Program
    {
        // Outcome of the Task 1 spike (spike\RESULT.md): true = owned by the desktop host window, false = bottom-of-stack only.
        internal static readonly bool UseDesktopOwner = true;

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

    // Wires settings, sampler, card window and tray icon together and owns their lifetimes.
    internal sealed class AppController : IDisposable
    {
        private readonly string _settingsPath = SettingsStore.DefaultPath;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private AppSettings _settings;
        private SettingsWatcher _watcher;
        private MetricsSampler _sampler;
        private TrayIcon _tray;
        private CardWindow _window;
        private DispatcherTimer _timer;
        private bool _exiting;

        public void Start()
        {
            _settings = SettingsStore.LoadOrCreate(_settingsPath);
            _sampler = new MetricsSampler(_settings);
            _tray = new TrayIcon(_settingsPath);
            _tray.UnlockToggled += delegate { _window.SetUnlocked(!_window.Unlocked); };
            _tray.ExitRequested += Exit;
            ShowCard();
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
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Tick(); };
            _timer.Start();
            Tick();
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
        }

        private void Tick()
        {
            Snapshot s = _sampler.Sample();
            _window.Card.Update(s);
            _tray.Update(s, _settings);
        }

        // Thresholds, opacity and zones apply live; x/y are only read at start-up.
        private void ApplySettings(AppSettings s)
        {
            _settings = s;
            _sampler.ApplySettings(s);
            _window.ApplySettings(s);
        }

        private void SavePosition(double x, double y)
        {
            AppSettings s = _settings.Clone();
            s.X = Math.Round(x);
            s.Y = Math.Round(y);
            _settings = s;
            try
            {
                SettingsStore.Save(_settingsPath, s);
            }
            catch (Exception ex)
            {
                Log.Write("Settings: could not save position, " + ex.Message);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) _dispatcher.BeginInvoke(new Action(() => _sampler.Reset()));
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
            if (_timer != null) _timer.Stop();
            if (_watcher != null) _watcher.Dispose();
            if (_tray != null) _tray.Dispose();
            if (_sampler != null) _sampler.Dispose();
        }
    }
}
```

- [ ] **Step 6: Build the app and re-run the tests**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
```
Expected: `Built bin\DesktopMonitor.exe` (about 42 KB), then `118 passed, 0 failed`.

- [ ] **Step 7: Start it and inspect the window**

Run:
```powershell
Start-Process .\bin\DesktopMonitor.exe; Start-Sleep -Seconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File tools\inspect-window.ps1
Get-Content "$env:APPDATA\DesktopMonitor\settings.json"
```
Expected: inspect output matches the spike's passing mode (`visible: True`, `click-through: True`, `tool window: True`, `no-activate: True`; owner `Progman`/`WorkerW` in owner mode). `settings.json` exists with `"x": null`, `"tempAmber": 80`, `"diskAmber": 90`.

- [ ] **Step 8: Visual check against the approved preview**

Ask the user to compare the desktop card with the approved style B preview, or check it with a screenshot. Expected:
- Top-right, 12 px from the edges.
- Header "System" with the clock.
- Three rings: CPU %, RAM %, CPU temp (coloured red when the CPU zone is ≥ 85 °C, as it has been on this laptop).
- Grid rows: GPU | Disk C:, Net ↓ | Net ↑, Battery % (bolt on AC) and battery temp | Skin.
- Disk C: at 87 % in plain text colour (below the 90 % amber threshold).
- The tray icon shows the CPU temperature number, and hovering it shows `CPU nn% · nn°C`.

- [ ] **Step 9: Interaction checks**

Do each check and confirm the expected result (ask the user for the mouse actions):
1. Right-click the tray icon → **Unlock to move**. The card shows a dashed outline and "Drag to move · locks when you let go". `tools\inspect-window.ps1` reports `click-through: False`.
2. Drag the card somewhere else and release. The outline disappears, `inspect-window.ps1` reports `click-through: True`, and `settings.json` now has numeric `x` and `y`. The tray menu item reads "Unlock to move" again.
3. Live settings: edit `settings.json` and set `"tempAmber": 50`, then save. Within about 1 s the CPU temp ring turns amber or red as appropriate. Set it back to `80` and save.
4. Bad edit: replace the file content with `{ broken` and save. The card keeps working and `log.txt` gains `Settings: ignored invalid edit`. Restore the file with `x`/`y` and `tempAmber: 80` (copy from step 2's content).
5. Tray → **Open settings** opens `settings.json` in an editor.
6. Second instance: `Start-Process .\bin\DesktopMonitor.exe; Start-Sleep 2; (Get-Process DesktopMonitor).Count` gives `1`.
7. Tray → **Exit**. The card and tray icon disappear, and `Get-Process DesktopMonitor -ErrorAction SilentlyContinue` returns nothing.

- [ ] **Step 10: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add app.manifest src/CardView.cs src/CardWindow.cs src/TrayIcon.cs src/Program.cs && git commit -m "Add gauge card window, tray icon and app wiring

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: README and final verification

**Files:**
- Create: `README.md`
- Modify: `docs\superpowers\specs\2026-10-02-desktop-monitor-design.md` (status line)

**Interfaces:**
- Consumes: the finished `bin\DesktopMonitor.exe`, `tools\inspect-window.ps1`, `tools\SamplerDump.cs`.
- Produces: documentation and a verified build.

- [ ] **Step 1: Create `README.md`**

````markdown
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
````

- [ ] **Step 2: Resource check**

Start the app if it isn't running, wait 10 minutes, then run:
```powershell
$p = Get-Process DesktopMonitor; $t0 = $p.TotalProcessorTime; Start-Sleep -Seconds 60; $p.Refresh()
"CPU {0:N2} %  working set {1:N0} MB" -f (($p.TotalProcessorTime - $t0).TotalSeconds / 60 / [Environment]::ProcessorCount * 100), ($p.WorkingSet64 / 1MB)
```
Expected: CPU below 1 %, working set below 50 MB. If either is over, do not tune blindly: report the numbers to the user with the top suspects (WPF baseline memory, GPU category read).

- [ ] **Step 3: Lifecycle checks (ask the user before each disruptive one)**

1. **Off-screen reset:** Exit from the tray, set `"x": 5000, "y": 5000` in `settings.json`, start the app. Expected: card at top-right.
2. **Explorer restart (ask first):** `Stop-Process -Name explorer -Force`. If Explorer doesn't come back by itself within 8 s, run `Start-Process explorer.exe`. Expected: the card is visible again within 5 s and `inspect-window.ps1` finds it.
3. **Sleep and resume (user):** put the laptop to sleep, wake it. Expected: within 2–3 s every value is live again; no `--` lingers.
4. **Start with Windows and reboot (user):** tray → Start with Windows (ticked), reboot, sign in. Expected: the card appears on its own; `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v DesktopMonitor` shows the quoted exe path.
5. **Win+D:** with apps open, press Win+D. Expected: the result matches the spike decision (visible in owner mode).

- [ ] **Step 4: Mark the spec implemented**

In `docs\superpowers\specs\2026-10-02-desktop-monitor-design.md` replace `Status: approved 2026-10-02` with `Status: implemented 2026-10-02 (see spike/RESULT.md for the pinning mode)`.

- [ ] **Step 5: Commit**

```bash
cd /d/Projects/Desktop_Monitor && git add README.md docs/superpowers/specs/2026-10-02-desktop-monitor-design.md && git commit -m "Add README and mark spec implemented

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
