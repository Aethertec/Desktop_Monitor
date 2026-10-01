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
