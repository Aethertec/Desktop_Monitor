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

$h = [Win]::FindWindow([NullString]::Value, $Title)   # $null would arrive as "" and match no class
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
