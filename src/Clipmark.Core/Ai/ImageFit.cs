namespace Clipmark.Core.Ai;

public static class ImageFit
{
    /// <summary>Scales down so the long edge is at most <paramref name="maxLongEdge"/>, keeping the aspect ratio; never upscales.</summary>
    public static (int Width, int Height) Fit(int width, int height, int maxLongEdge = 2576)
    {
        int longEdge = Math.Max(width, height);
        if (longEdge <= maxLongEdge)
            return (width, height);
        int Scale(int v) => Math.Max(1, (int)Math.Round((double)v * maxLongEdge / longEdge, MidpointRounding.AwayFromZero));
        return (Scale(width), Scale(height));
    }
}
