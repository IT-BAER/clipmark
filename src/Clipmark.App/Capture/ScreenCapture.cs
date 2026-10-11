using System.Runtime.InteropServices;
using Clipmark.App.Interop;
using Clipmark.Core.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Clipmark.App.Capture;

/// <summary>Frozen BGRA copy of the whole desktop plus the monitor layout it was taken with.</summary>
internal sealed class DesktopFrame(VirtualScreen screen, IReadOnlyList<PixelRect> monitors, byte[] pixels)
{
    public VirtualScreen Screen { get; } = screen;
    public IReadOnlyList<PixelRect> Monitors { get; } = monitors;
    public byte[] Pixels { get; } = pixels;

    /// <summary>Copies one desktop rectangle out of the frame (BGRA, top-down).</summary>
    public byte[] Crop(PixelRect desktopRect)
    {
        var crop = Screen.CropFor(desktopRect);
        int stride = Screen.Bounds.Width * 4, rowBytes = crop.Width * 4;
        var result = new byte[rowBytes * crop.Height];
        for (int row = 0; row < crop.Height; row++)
            System.Buffer.BlockCopy(Pixels, (crop.Y + row) * stride + crop.X * 4, result, row * rowBytes, rowBytes);
        return result;
    }

    public async Task SavePngAsync(PixelRect desktopRect, string path)
    {
        var bgra = Crop(desktopRect);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)desktopRect.Width, (uint)desktopRect.Height, 96, 96, bgra);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        using (var reader = new DataReader(stream))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
        }
        await File.WriteAllBytesAsync(path, bytes);
    }
}

internal static class ScreenCapture
{
    /// <summary>GDI copy of every monitor in one BitBlt. SDR only; HDR needs Windows.Graphics.Capture.</summary>
    public static unsafe DesktopFrame CaptureDesktop()
    {
        var monitors = EnumerateMonitors();
        var screen = VirtualScreen.FromMonitors(monitors);
        var b = screen.Bounds;
        var pixels = new byte[b.Width * b.Height * 4];

        nint screenDc = Native.GetDC(0);
        nint memDc = Native.CreateCompatibleDC(screenDc);
        nint bitmap = Native.CreateCompatibleBitmap(screenDc, b.Width, b.Height);
        nint old = Native.SelectObject(memDc, bitmap);
        try
        {
            if (!Native.BitBlt(memDc, 0, 0, b.Width, b.Height, screenDc, b.X, b.Y, Native.SRCCOPY | Native.CAPTUREBLT))
                throw new InvalidOperationException("BitBlt failed.");
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = sizeof(Native.BITMAPINFOHEADER), biWidth = b.Width,
                biHeight = -b.Height, // negative height = top-down rows
                biPlanes = 1, biBitCount = 32,
            };
            Native.SelectObject(memDc, old);
            fixed (byte* p = pixels)
            {
                if (Native.GetDIBits(memDc, bitmap, 0, (uint)b.Height, p, &header, 0) != b.Height)
                    throw new InvalidOperationException("GetDIBits returned a partial frame.");
            }
        }
        finally
        {
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(0, screenDc);
        }
        return new DesktopFrame(screen, monitors, pixels);
    }

    public static unsafe List<PixelRect> EnumerateMonitors()
    {
        var list = new List<PixelRect>();
        var handle = GCHandle.Alloc(list);
        try
        {
            Native.EnumDisplayMonitors(0, 0, &OnMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return list;
    }

    [UnmanagedCallersOnly]
    private static unsafe int OnMonitor(nint monitor, nint hdc, Native.RECT* rect, nint data)
    {
        var list = (List<PixelRect>)GCHandle.FromIntPtr(data).Target!;
        var info = new Native.MONITORINFO { cbSize = sizeof(Native.MONITORINFO) };
        if (Native.GetMonitorInfo(monitor, ref info))
        {
            var r = info.rcMonitor;
            list.Add(new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
        }
        return 1;
    }
}
