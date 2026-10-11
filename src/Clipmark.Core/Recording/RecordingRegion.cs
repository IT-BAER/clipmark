using Clipmark.Core.Capture;

namespace Clipmark.Core.Recording;

/// <summary>What a recording captures: one monitor and the region on it, both in desktop pixels.</summary>
public readonly record struct RecordingArea(PixelRect Monitor, PixelRect Region);

public static class RecordingRegion
{
    /// <summary>Smallest recorded side in pixels; smaller picks grow around their center.</summary>
    public const int MinSize = 64;

    /// <summary>
    /// The monitor with the largest overlap and the selection clipped to it, grown to <see cref="MinSize"/> and with even
    /// width and height (H.264 needs both); null when the selection touches no monitor.
    /// </summary>
    public static RecordingArea? Fit(PixelRect selection, IReadOnlyList<PixelRect> monitors)
    {
        PixelRect? best = null, monitor = null;
        foreach (var m in monitors)
        {
            if (selection.Intersect(m) is { } part && (best is not { } b || (long)part.Width * part.Height > (long)b.Width * b.Height))
                (best, monitor) = (part, m);
        }
        if (best is not { } r || monitor is not { } mon)
            return null;
        int w = Math.Min(Math.Max(r.Width, MinSize), mon.Width) & ~1;
        int h = Math.Min(Math.Max(r.Height, MinSize), mon.Height) & ~1;
        int x = Math.Clamp(r.X + (r.Width - w) / 2, mon.X, mon.Right - w);
        int y = Math.Clamp(r.Y + (r.Height - h) / 2, mon.Y, mon.Bottom - h);
        if (r.Width >= MinSize && r.Width <= mon.Width)
            x = r.X; // evening down keeps the left edge
        if (r.Height >= MinSize && r.Height <= mon.Height)
            y = r.Y;
        return new RecordingArea(mon, new PixelRect(x, y, w, h));
    }
}

public static class VideoBitrate
{
    /// <summary>0.1 bit per pixel and frame, between 1 and 40 Mbit/s.</summary>
    public static uint For(int width, int height, int fps) =>
        (uint)Math.Clamp((long)width * height * fps / 10, 1_000_000, 40_000_000);
}

public static class RecordingTime
{
    /// <summary>"mm:ss", or "h:mm:ss" from one hour on.</summary>
    public static string Format(TimeSpan t) => t.TotalHours >= 1
        ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
        : $"{t.Minutes:00}:{t.Seconds:00}";
}
