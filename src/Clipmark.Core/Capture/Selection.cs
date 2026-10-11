namespace Clipmark.Core.Capture;

public static class Selection
{
    /// <summary>Rect spanned by a drag in any direction, clipped to <paramref name="bounds"/>; null when empty.</summary>
    public static PixelRect? FromDrag(PixelPoint start, PixelPoint end, PixelRect bounds) =>
        PixelRect.FromCorners(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)).Intersect(bounds);
}
