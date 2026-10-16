namespace Clipmark.Core.Recording;

public enum GifQuality { Low, High }

/// <summary>GIF export from a recording, as in Snipping Tool: at most 30 s, Low 12 fps, High 24 fps, long edge capped.</summary>
public static class GifPlan
{
    public const int MaxSeconds = 30, MaxEdge = 1280, MinEdge = 160;

    /// <summary>
    /// Low always fits a Claude upload: 10 MB per image on claude.ai, 10 MB base64 on the API, so 7.5 MB of raw bytes.
    /// </summary>
    public const long LowBudgetBytes = 7_500_000;

    public static int Fps(GifQuality quality) => quality == GifQuality.High ? 24 : 12;

    /// <summary>Output size with the long edge at most <paramref name="maxEdge"/>, never upscaled, both sides even.</summary>
    public static (int Width, int Height) Size(int width, int height, int maxEdge)
    {
        double scale = Math.Min(1.0, (double)maxEdge / Math.Max(width, height));
        return (Math.Max(2, (int)Math.Round(width * scale) & ~1), Math.Max(2, (int)Math.Round(height * scale) & ~1));
    }

    public static int FrameCount(TimeSpan duration, int fps)
    {
        double seconds = Math.Min(MaxSeconds, duration.TotalSeconds);
        return Math.Max(1, (int)Math.Ceiling(seconds * fps - 1e-9));
    }

    public static TimeSpan FrameTime(int index, int fps) => TimeSpan.FromTicks(index * TimeSpan.TicksPerSecond / fps);

    /// <summary>Frame delay in hundredths of a second; rounding is carried so the delays add up to the frame time.</summary>
    public static int DelayCs(int index, int fps) =>
        (int)(Math.Round((index + 1) * 100.0 / fps, MidpointRounding.AwayFromZero) - Math.Round(index * 100.0 / fps, MidpointRounding.AwayFromZero));

    /// <summary>The long edge for another Low attempt when <paramref name="bytes"/> exceed the budget, else null.</summary>
    public static int? SmallerEdge(long bytes, int edge, long budget = LowBudgetBytes)
    {
        if (bytes <= budget || edge <= MinEdge) return null;
        // GIF size grows about with the pixel area; 0.9 leaves room for the estimate's error.
        int next = (int)(edge * Math.Sqrt((double)budget / bytes) * 0.9) & ~1;
        return Math.Max(MinEdge, next);
    }
}
