# Renders every SVG in a folder with Inkscape and builds a light/dark contact sheet (200 px + 32 px).
param([Parameter(Mandatory)][string]$Folder, [string]$Inkscape = 'D:\Software\Inkscape\bin\inkscape.exe')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$png = Join-Path $Folder 'png'
New-Item -ItemType Directory -Force $png | Out-Null
Get-ChildItem $Folder -Filter *.svg | ForEach-Object {
    $target = Join-Path $png "$($_.BaseName).png"
    & $Inkscape $_.FullName --export-type=png --export-width=256 "--export-filename=$target" 2>$null | Out-Null
}
$files = Get-ChildItem $png -Filter *.png | Where-Object Name -ne 'contact-sheet.png' | Sort-Object Name
$cell = 300; $cols = 5; $rows = [math]::Ceiling($files.Count / $cols)
$sheet = New-Object Drawing.Bitmap ($cols * $cell), ($rows * $cell * 2)
$g = [Drawing.Graphics]::FromImage($sheet)
$g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAlias'
$font = New-Object Drawing.Font 'Segoe UI', 11
for ($i = 0; $i -lt $files.Count; $i++) {
    $img = [Drawing.Image]::FromFile($files[$i].FullName)
    $x = ($i % $cols) * $cell; $top = [math]::Floor($i / $cols) * $cell * 2
    foreach ($dark in $false, $true) {
        $y = $top + [int]$dark * $cell
        $bg = if ($dark) { [Drawing.Color]::FromArgb(32, 32, 32) } else { [Drawing.Color]::FromArgb(243, 243, 243) }
        $g.FillRectangle((New-Object Drawing.SolidBrush $bg), $x, $y, $cell, $cell)
        $g.DrawImage($img, $x + 22, $y + 12, 200, 200)
        $g.DrawImage($img, $x + 240, $y + 80, 32, 32)
        $fg = if ($dark) { [Drawing.Brushes]::White } else { [Drawing.Brushes]::Black }
        $g.DrawString($files[$i].BaseName, $font, $fg, $x + 22, $y + 250)
    }
    $img.Dispose()
}
$out = Join-Path $png 'contact-sheet.png'
$sheet.Save($out); $g.Dispose(); $sheet.Dispose()
$out
