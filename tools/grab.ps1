param([string]$Out, [string]$ProcessName = '')
# Saves the window of $ProcessName (or the full virtual screen) as PNG.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[W]::SetProcessDPIAware() | Out-Null
if ($ProcessName) {
    $p = Get-Process $ProcessName -ErrorAction Stop | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
    if (-not $p) { throw "no window for $ProcessName" }
    [W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 600
    $r = New-Object W+R
    [W]::DwmGetWindowAttribute($p.MainWindowHandle, 9, [ref]$r, 16) | Out-Null
    $rect = [Drawing.Rectangle]::FromLTRB($r.L, $r.T, $r.Ri, $r.B)
} else {
    $rect = [Windows.Forms.SystemInformation]::VirtualScreen
}
$bmp = New-Object Drawing.Bitmap $rect.Width, $rect.Height
$g = [Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Location, [Drawing.Point]::Empty, $rect.Size)
$bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"$Out $($rect.Width)x$($rect.Height)"
