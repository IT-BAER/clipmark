# Measures size, start time and idle RAM for the M0 variants and for Snipping Tool.
# Start time = launch until the process has a main window (same method for every app),
# plus Clipmark's own first-rendered-frame marker. 5 runs, median. RAM after 5 s idle.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$m0 = Join-Path $root 'artifacts\m0'
$runs = 5

function Get-FolderSize($path) {
    $files = Get-ChildItem $path -Recurse -File -Force
    [pscustomobject]@{ MB = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1); Files = $files.Count }
}

function Median($values) { $s = $values | Sort-Object; $s[[int][math]::Floor($s.Count / 2)] }

function Wait-MainWindow($processName, $timeoutMs = 15000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $p = Get-Process $processName -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
        if ($p) { return $p }
        Start-Sleep -Milliseconds 10
    }
    throw "$processName showed no window within $timeoutMs ms"
}

function Measure-Idle($process) {
    Start-Sleep -Seconds 5
    $process.Refresh()
    [pscustomobject]@{ WorkingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 1); PrivateMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 1) }
}

$results = @()
foreach ($dir in Get-ChildItem $m0 -Directory | Where-Object Name -notlike 'B-*') {
    $exe = Join-Path $dir.FullName 'Clipmark.App.exe'
    if (-not (Test-Path $exe)) { "skip $($dir.Name): no exe"; continue }
    $window = @(); $frame = @()
    for ($i = 0; $i -lt $runs; $i++) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process $exe -PassThru
        Wait-MainWindow 'Clipmark.App' | Out-Null
        $window += $sw.ElapsedMilliseconds
        $p.Kill(); $p.WaitForExit()

        $marker = Join-Path $env:TEMP "clipmark-start-$i.txt"
        Remove-Item $marker -ErrorAction SilentlyContinue
        $p = Start-Process $exe -ArgumentList '--spike-startup', $marker -PassThru
        if (-not $p.WaitForExit(15000)) { $p.Kill(); throw "startup marker run hung: $($dir.Name)" }
        $frame += [int](([long](Get-Content $marker) - $p.StartTime.ToUniversalTime().Ticks) / 10000)
    }
    $p = Start-Process $exe -PassThru
    Wait-MainWindow 'Clipmark.App' | Out-Null
    $idle = Measure-Idle $p
    $p.Kill(); $p.WaitForExit()

    $capDir = Join-Path $env:TEMP "clipmark-spike\$($dir.Name)"
    Remove-Item $capDir -Recurse -Force -ErrorAction SilentlyContinue
    $p = Start-Process $exe -ArgumentList '--spike-capture', $capDir -PassThru
    $null = $p.WaitForExit(30000)
    $size = Get-FolderSize $dir.FullName
    $results += [pscustomobject]@{
        App = $dir.Name; SizeMB = $size.MB; Files = $size.Files
        WindowMsMedian = Median $window; FirstFrameMsMedian = Median $frame; FirstRunWindowMs = $window[0]
        IdleWorkingSetMB = $idle.WorkingSetMB; IdlePrivateMB = $idle.PrivateMB
        Capture = (Get-Content (Join-Path $capDir 'report.txt') -ErrorAction SilentlyContinue)
    }
}

# Snipping Tool: same window-based start timing and idle RAM.
$pkg = Get-AppxPackage Microsoft.ScreenSketch
Get-Process SnippingTool -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$window = @()
for ($i = 0; $i -lt $runs; $i++) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Start-Process 'ms-screensketch:'
    Wait-MainWindow 'SnippingTool' | Out-Null
    $window += $sw.ElapsedMilliseconds
    Get-Process SnippingTool | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}
Start-Process 'ms-screensketch:'
$p = Wait-MainWindow 'SnippingTool'
$idle = Measure-Idle $p
Get-Process SnippingTool | Stop-Process -Force
$size = Get-FolderSize $pkg.InstallLocation
$results += [pscustomobject]@{
    App = "SnippingTool $($pkg.Version)"; SizeMB = $size.MB; Files = $size.Files
    WindowMsMedian = Median $window; FirstFrameMsMedian = $null; FirstRunWindowMs = $window[0]
    IdleWorkingSetMB = $idle.WorkingSetMB; IdlePrivateMB = $idle.PrivateMB; Capture = $null
}

$msix = Get-ChildItem (Join-Path $m0 'B-aot-msix') -Recurse -Filter 'Clipmark.App_*.msix' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($msix) { $results += [pscustomobject]@{ App = "B-aot-msix ($($msix.Name))"; SizeMB = [math]::Round($msix.Length / 1MB, 1); Files = 1 } }

$results | Format-List
$results | ConvertTo-Json | Set-Content (Join-Path $m0 'results.json')
