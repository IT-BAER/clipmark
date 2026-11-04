# Release gate: publishes a Release + Native AOT build with TestDriver compiled in (ClipmarkSmoke, never shipped) and drives
# it cloaked through the TestDriver command protocol (see TestDriver.cs). No real input, no focus change, no server calls.
# Prints GREEN/RED per case; exit 1 on any RED or crash. On a clean tree it writes artifacts\smoke\pass-<commit>.txt,
# which publish-release.ps1 and publish-store.ps1 require.
# Why: Debug builds (JIT and Debug AOT) hide Release-AOT-only failures; 1.2.0 shipped a tool ring that crashed on open.
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts\smoke'
$app = Join-Path $out 'app'
$exe = Join-Path $app 'Clipmark.App.exe'
$commit = (& git -C $root rev-parse HEAD).Trim()
$clean = -not $NoBuild -and -not (& git -C $root status --porcelain --untracked-files=no)
Get-ChildItem $out -Filter 'pass-*.txt' -ErrorAction SilentlyContinue | Remove-Item

if (-not $NoBuild) {
    Get-Process Clipmark.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
    if (Test-Path $app) { Remove-Item $app -Recurse -Force }
    & dotnet publish (Join-Path $root 'src\Clipmark.App\Clipmark.App.csproj') -c Release -r win-x64 -p:Platform=x64 -nologo -v q `
        -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishAot=true -p:SelfContained=true -p:ClipmarkSmoke=true -o $app
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Smoke -Name W -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(System.IntPtr h, int a, out int v, int s);
public static System.IntPtr Main(uint pid) {
    System.IntPtr found = System.IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); var s = new System.Text.StringBuilder(64); GetWindowText(h, s, 64);
        if (p == pid && s.ToString() == "Clipmark" && IsWindowVisible(h)) { found = h; return false; } return true; }, System.IntPtr.Zero);
    return found;
}
public static bool Cloaked(System.IntPtr h) { int v; DwmGetWindowAttribute(h, 14, out v, 4); return v != 0; }
'@

$work = Join-Path $env:TEMP 'clipmark-smoke'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null
$fixture = Join-Path $work 'fixture.png'
$bmp = New-Object System.Drawing.Bitmap 600, 400
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::White)
$g.DrawString('Clipmark smoke', (New-Object System.Drawing.Font 'Segoe UI', 36, ([System.Drawing.GraphicsUnit]::Pixel)), [System.Drawing.Brushes]::Black, 40, 160)
$g.Dispose(); $bmp.Save($fixture, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

$script:red = 0
$started = Get-Date

function Send($dir, [string[]]$lines, $timeoutMs = 30000) {
    Remove-Item -LiteralPath "$dir\result.txt" -ErrorAction SilentlyContinue
    Set-Content "$dir\cmd.tmp" $lines
    Move-Item "$dir\cmd.tmp" "$dir\cmd.txt" -Force
    for ($t = 0; $t -lt $timeoutMs -and -not (Test-Path "$dir\result.txt"); $t += 100) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path "$dir\result.txt")) { throw 'no result (app gone or hung)' }
    $r = (Get-Content "$dir\result.txt" -Raw).Trim()
    if ($r -ne 'ok') { throw $r }
}

function Start-Smoke($name) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory $dir | Out-Null
    # No entitlement and a pending retry: Free, and the licence check makes no server call.
    Set-Content "$dir\license.json" "{ `"retryAfterUtc`": `"$([DateTime]::UtcNow.AddDays(1).ToString('O'))`" }"
    $env:CLIPMARK_TEST_DIR = $dir
    try { $p = Start-Process $exe -ArgumentList '--open', "`"$fixture`"" -PassThru } finally { Remove-Item Env:CLIPMARK_TEST_DIR }
    for ($t = 0; $t -lt 20000 -and ($h = [Smoke.W]::Main([uint32]$p.Id)) -eq [IntPtr]::Zero; $t += 100) { Start-Sleep -Milliseconds 100 }
    if ($h -eq [IntPtr]::Zero -or -not [Smoke.W]::Cloaked($h)) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; throw 'window missing or not cloaked' }
    Send $dir @('ready') 20000
    @{ Dir = $dir; Proc = $p }
}

function Case($s, $label, [string[]]$lines) {
    try {
        if ($s.Proc.HasExited) { throw "app exited earlier (0x$('{0:X8}' -f $s.Proc.ExitCode))" }
        Send $s.Dir $lines
        "GREEN $label"
    } catch { "RED   $label :: $($_.Exception.Message.Trim())"; $script:red++ }
}

$s = $null
try {
    $s = Start-Smoke 'main'
    $d = $s.Dir
    Case $s 'tool ring: open at the point, keyboard, Esc' @('ringmotion on', 'rightclick 300,200', 'assert ring 300,200', 'assert focus Pen', 'ringkey Right', 'assert focus Highlighter', 'ringkey Escape', 'assert ring closed', 'wait 300', 'assert ring gone')
    Case $s 'tool ring: pick, animation off, beside the image' @('rightclick 300,200', 'ringpick Highlighter', 'assert tool Highlighter', 'ringmotion off', 'rightclick -100,200 0', 'assert ring open', 'ringkey Escape 0', 'assert ring gone', 'ringmotion on')
    Case $s 'tool ring: Premium pick opens the Premium dialog' @('rightclick 300,200', 'ringpick Text', 'wait 600', 'assert has Buy Premium', 'dialogclick Cancel', 'wait 400')
    # Free licence state: Select, Text and other Premium tools are locked; Pen, Highlighter and Eraser are not.
    Case $s 'view: touch pinch zooms, draws nothing' @('click Pen', 'assert count 0', 'touch down 1 200,200', 'touch down 2 400,200', 'touch move 2 480,220', 'touch move 1 150,180', 'touch up 2', 'touch up 1', 'wait 300', 'assert count 0')
    Case $s 'draw: pen stroke, undo, redo' @('key Number0 ctrl', 'wait 300', 'click Pen', 'drag 100,100 200,150 300,120', 'assert count 1', 'key Z ctrl', 'assert count 0', 'key Y ctrl', 'assert count 1')
    Case $s 'draw: eraser removes the stroke' @('click Eraser', 'drag 150,110 150,118 150,125 150,132 150,140', 'assert count 0', 'key Z ctrl', 'assert count 1', 'click Pen')
    Case $s 'view: wheel zoom in and out' @('zoomwheel 120 300,200', 'wait 800', 'zoomwheel -120 300,200', 'wait 800', "viewat 0,0")
    Case $s 'view: middle-button pan' @('key Number0 ctrl', 'wait 300', 'pan down 300,200', 'pan move 260,170', 'pan move 220,140', 'pan up', 'bar h 100', 'bar v 100')
    Case $s 'crop: open and cancel' @('click Crop', 'assert ring closed', 'key Escape')
    Case $s 'text actions: OCR runs' @('click Text actions', 'textwait', 'closepopups')
    Case $s 'theme: dark and light render' @('settheme dark', "shot $d\dark.png", 'settheme light', "shot $d\light.png")
    Case $s 'export: PNG and JPEG' @('drag 50,50 150,80', "save $d\out.png", "saveas $d\out.jpg")
    if ($script:red -eq 0 -and -not ((Test-Path "$d\out.png") -and (Test-Path "$d\out.jpg"))) { 'RED   export files missing'; $script:red++ }
} catch { "RED   start :: $($_.Exception.Message.Trim())"; $script:red++ }
finally { if ($s -and -not $s.Proc.HasExited) { Stop-Process -Id $s.Proc.Id -Force } }

# Recording path (Windows.Graphics.Capture + WASAPI) as the release flag runs it.
$rec = Join-Path $work 'rec.mp4'
$p = Start-Process $exe -ArgumentList '--record-test', "`"$rec`"" -PassThru
if (-not $p.WaitForExit(60000)) { Stop-Process -Id $p.Id -Force }
$r = Get-Content "$rec.txt" -ErrorAction SilentlyContinue
if ($r -like 'ok*' -and (Test-Path $rec) -and (Get-Item $rec).Length -gt 10000) { "GREEN recording: --record-test ($r)" } else { "RED   recording: --record-test :: $r"; $script:red++ }

$crashes = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000; StartTime = $started } -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match [regex]::Escape($exe) })
if ($crashes.Count) { "RED   $($crashes.Count) Application Error event(s) for $exe"; $script:red++ } else { 'GREEN no Application Error events' }

if ($script:red) { "SMOKE RED: $script:red failure(s)"; exit 1 }
if ($clean) {
    Set-Content (Join-Path $out "pass-$commit.txt") "$((Get-Date).ToString('O')) $commit" -NoNewline
    "SMOKE GREEN: pass-$commit.txt"
} else { 'SMOKE GREEN, but -NoBuild or changed tracked files: no pass marker written' }
