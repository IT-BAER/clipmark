# Builds the app/MSIX icon assets from assets\logo\*.svg (Inkscape CLI + System.Drawing).
# clipmark-small.svg (heavier strokes) is used at 32 px and below.
param([string]$Inkscape = 'D:\Software\Inkscape\bin\inkscape.exe')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$logo = Join-Path $root 'assets\logo'
$assets = Join-Path $root 'src\Clipmark.App\Assets'
$tmp = Join-Path $env:TEMP 'clipmark-icons'
New-Item -ItemType Directory -Force $tmp | Out-Null

function Render([int]$size) {
    $svg = if ($size -le 32) { 'clipmark-small.svg' } else { 'clipmark.svg' }
    $target = Join-Path $tmp "icon-$size.png"
    & $Inkscape (Join-Path $logo $svg) --export-type=png "--export-width=$size" "--export-filename=$target" 2>$null | Out-Null
    if (-not (Test-Path $target)) { throw "Inkscape did not write $target" }
    $target
}

function Centered([string]$name, [int]$width, [int]$height, [int]$iconSize) {
    $icon = [Drawing.Image]::FromFile((Render $iconSize))
    $canvas = New-Object Drawing.Bitmap $width, $height
    $g = [Drawing.Graphics]::FromImage($canvas)
    $g.DrawImage($icon, [int](($width - $iconSize) / 2), [int](($height - $iconSize) / 2), $iconSize, $iconSize)
    $canvas.Save((Join-Path $assets $name), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $canvas.Dispose(); $icon.Dispose()
}

$square = [ordered]@{
    'Square44x44Logo.scale-200.png' = 88
    'Square44x44Logo.targetsize-24_altform-unplated.png' = 24
    'Square44x44Logo.targetsize-48_altform-lightunplated.png' = 48
    'Square150x150Logo.scale-200.png' = 300
    'LockScreenLogo.scale-200.png' = 48
    'StoreLogo.png' = 50
}
foreach ($name in $square.Keys) { Copy-Item (Render $square[$name]) (Join-Path $assets $name) -Force }
Centered 'Wide310x150Logo.scale-200.png' 620 300 200
Centered 'SplashScreen.scale-200.png' 1240 600 300

# ICO with PNG-compressed entries (supported since Windows Vista).
$sizes = 16, 20, 24, 32, 48, 64, 256
$images = foreach ($s in $sizes) { , [IO.File]::ReadAllBytes((Render $s)) }
$ms = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.ico'), $ms.ToArray())
$w.Dispose()
Get-ChildItem $assets | Select-Object Name, Length
