using System.Runtime.InteropServices;
using Clipmark.Core.Capture;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Capture;

/// <summary>Captured pixels, BGRA top-down. Alpha is 255 except outside a freeform path.</summary>
internal sealed record CapturedImage(int Width, int Height, byte[] Bgra, bool HasTransparency);

/// <summary>Frozen copy of the whole desktop in a GDI DIB section, plus the monitor and window layout at that moment.</summary>
internal sealed unsafe class DesktopFrame(VirtualScreen screen, IReadOnlyList<PixelRect> monitors,
    IReadOnlyList<PixelRect> windows, nint bitmap, byte* bits) : IDisposable
{
    public VirtualScreen Screen { get; } = screen;
    public IReadOnlyList<PixelRect> Monitors { get; } = monitors;

    /// <summary>Top-level window frames in z-order, topmost first.</summary>
    public IReadOnlyList<PixelRect> Windows { get; } = windows;

    /// <summary>DIB section, top-down BGRA, Screen.Bounds sized. Select into a memory DC to blit from it.</summary>
    public nint Bitmap { get; private set; } = bitmap;
    public byte* Bits { get; } = bits;
    public int Stride => Screen.Bounds.Width * 4;

    public CapturedImage Cut(PixelRect desktopRect) => Cut(desktopRect, null);

    public CapturedImage Cut(FreeformMask mask) => Cut(mask.Bounds, mask.Alpha);

    private CapturedImage Cut(PixelRect desktopRect, byte[]? alpha)
    {
        var crop = Screen.CropFor(desktopRect);
        int rowBytes = crop.Width * 4;
        var result = new byte[rowBytes * crop.Height];
        fixed (byte* dst = result)
        {
            for (int row = 0; row < crop.Height; row++)
            {
                byte* src = Bits + (long)(crop.Y + row) * Stride + crop.X * 4;
                byte* line = dst + row * rowBytes;
                Buffer.MemoryCopy(src, line, rowBytes, rowBytes);
                for (int x = 0; x < crop.Width; x++)
                    line[x * 4 + 3] = alpha?[row * crop.Width + x] ?? 255; // GDI leaves alpha undefined
            }
        }
        return new CapturedImage(crop.Width, crop.Height, result, alpha is not null);
    }

    public void Dispose()
    {
        if (Bitmap != 0)
            DeleteObject(Bitmap);
        Bitmap = 0;
    }
}

internal static unsafe class ScreenCapture
{
    /// <summary>GDI copy of every monitor in one BitBlt. SDR only; HDR needs Windows.Graphics.Capture.</summary>
    public static DesktopFrame CaptureDesktop()
    {
        var monitors = EnumerateMonitors();
        var screen = VirtualScreen.FromMonitors(monitors);
        var windows = EnumerateWindows();
        var b = screen.Bounds;

        nint screenDc = GetDC(0);
        nint memDc = CreateCompatibleDC(screenDc);
        var header = new BITMAPINFOHEADER
        {
            biSize = sizeof(BITMAPINFOHEADER), biWidth = b.Width,
            biHeight = -b.Height, // negative height = top-down rows
            biPlanes = 1, biBitCount = 32,
        };
        void* bits;
        nint bitmap = CreateDIBSection(screenDc, &header, 0, &bits, 0, 0);
        try
        {
            if (bitmap == 0)
                throw new InvalidOperationException($"CreateDIBSection failed for {b.Width}x{b.Height}.");
            nint old = SelectObject(memDc, bitmap);
            bool ok = BitBlt(memDc, 0, 0, b.Width, b.Height, screenDc, b.X, b.Y, SRCCOPY | CAPTUREBLT);
            SelectObject(memDc, old);
            if (!ok)
                throw new InvalidOperationException("BitBlt failed.");
#if DEBUG
            // Store screenshots: a raw top-down BGRA file of exactly the virtual-screen size replaces the captured desktop.
            if (Environment.GetEnvironmentVariable("CLIPMARK_TEST_FRAME") is { Length: > 0 } testFrame)
            {
                var raw = File.ReadAllBytes(testFrame);
                if (raw.Length != b.Width * b.Height * 4)
                    throw new InvalidOperationException($"CLIPMARK_TEST_FRAME must be {b.Width}x{b.Height} BGRA.");
                Marshal.Copy(raw, 0, (nint)bits, raw.Length);
            }
#endif
        }
        catch
        {
            if (bitmap != 0)
                DeleteObject(bitmap);
            throw;
        }
        finally
        {
            DeleteDC(memDc);
            ReleaseDC(0, screenDc);
        }
        return new DesktopFrame(screen, monitors, windows, bitmap, (byte*)bits);
    }

    public static List<PixelRect> EnumerateMonitors()
    {
        var list = new List<PixelRect>();
        var handle = GCHandle.Alloc(list);
        try
        {
            EnumDisplayMonitors(0, 0, &OnMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return list;
    }

    [UnmanagedCallersOnly]
    private static int OnMonitor(nint monitor, nint hdc, RECT* rect, nint data)
    {
        var list = (List<PixelRect>)GCHandle.FromIntPtr(data).Target!;
        var info = new MONITORINFO { cbSize = sizeof(MONITORINFO) };
        if (GetMonitorInfo(monitor, ref info))
            list.Add(ToPixelRect(info.rcMonitor));
        return 1;
    }

    /// <summary>Visible, uncloaked top-level windows of other processes, topmost first.</summary>
    public static List<PixelRect> EnumerateWindows()
    {
        var list = new List<PixelRect>();
        var handle = GCHandle.Alloc(list);
        try
        {
            EnumWindows(&OnWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return list;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(nint hwnd, nint data)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
            return 1;
        var exStyle = (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((exStyle & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)) != 0)
            return 1;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId)
            return 1;
        int cloaked = 0;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0)
            return 1;
        char* cls = stackalloc char[32];
        var name = new string(cls, 0, GetClassName(hwnd, cls, 32));
        if (name is "Progman" or "WorkerW")
            return 1; // the desktop itself; window mode falls back to the monitor

        RECT r;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(RECT)) != 0)
            return 1;
        PixelRect? bounds = ToPixelRect(r);
        if (IsZoomed(hwnd))
        {
            // Maximized frames overhang their monitor; keep the window on its own monitor.
            var info = new MONITORINFO { cbSize = sizeof(MONITORINFO) };
            if (GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info))
                bounds = bounds.Value.Intersect(ToPixelRect(info.rcMonitor));
        }
        if (bounds is { Width: > 0, Height: > 0 } ok)
            ((List<PixelRect>)GCHandle.FromIntPtr(data).Target!).Add(ok);
        return 1;
    }

    private static PixelRect ToPixelRect(RECT r) => PixelRect.FromCorners(r.Left, r.Top, r.Right, r.Bottom);
}
