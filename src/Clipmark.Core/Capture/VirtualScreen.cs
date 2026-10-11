namespace Clipmark.Core.Capture;

/// <summary>Rectangle in physical pixels, desktop coordinates (may be negative).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(PixelRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
}

/// <summary>Union of all monitors; one bitmap of this size holds the whole desktop.</summary>
public sealed class VirtualScreen
{
    public PixelRect Bounds { get; }

    private VirtualScreen(PixelRect bounds) => Bounds = bounds;

    public static VirtualScreen FromMonitors(IReadOnlyCollection<PixelRect> monitors)
    {
        if (monitors.Count == 0)
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        int left = monitors.Min(m => m.X), top = monitors.Min(m => m.Y);
        int right = monitors.Max(m => m.Right), bottom = monitors.Max(m => m.Bottom);
        return new VirtualScreen(new PixelRect(left, top, right - left, bottom - top));
    }

    /// <summary>Maps a desktop rectangle to its position inside the virtual-screen bitmap.</summary>
    public PixelRect CropFor(PixelRect desktopRect)
    {
        if (!Bounds.Contains(desktopRect))
            throw new ArgumentOutOfRangeException(nameof(desktopRect), desktopRect, "Outside the virtual screen.");
        return desktopRect with { X = desktopRect.X - Bounds.X, Y = desktopRect.Y - Bounds.Y };
    }
}
