namespace Clipmark.Core.Capture;

public static class ScreenPick
{
    public static PixelRect? MonitorAt(IEnumerable<PixelRect> monitors, PixelPoint p)
    {
        foreach (var m in monitors)
            if (m.Contains(p))
                return m;
        return null;
    }

    /// <summary>Topmost window under <paramref name="p"/>, clipped to the desktop. <paramref name="topFirst"/> is in z-order.</summary>
    public static PixelRect? WindowAt(IEnumerable<PixelRect> topFirst, PixelPoint p, PixelRect desktop)
    {
        foreach (var w in topFirst)
            if (w.Contains(p))
                return w.Intersect(desktop);
        return null;
    }
}
