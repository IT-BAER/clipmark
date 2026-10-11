namespace Clipmark.Core.Capture;

/// <summary>Lasso selection: bounding box plus one alpha byte per pixel (255 inside, 0 outside).</summary>
public sealed class FreeformMask
{
    public PixelRect Bounds { get; }

    /// <summary>Row-major, Bounds.Width * Bounds.Height bytes.</summary>
    public byte[] Alpha { get; }

    private FreeformMask(PixelRect bounds, byte[] alpha) => (Bounds, Alpha) = (bounds, alpha);

    /// <summary>Closed polygon, even-odd fill, sampled at pixel centers, clipped to <paramref name="desktop"/>. Null when no pixel is inside.</summary>
    public static FreeformMask? Create(IReadOnlyList<PixelPoint> path, PixelRect desktop)
    {
        if (path.Count < 3)
            return null;
        var box = PixelRect.FromCorners(path.Min(p => p.X), path.Min(p => p.Y), path.Max(p => p.X), path.Max(p => p.Y))
            .Intersect(desktop);
        if (box is not { } b)
            return null;

        var alpha = new byte[b.Width * b.Height];
        var crossings = new List<double>();
        bool any = false;
        for (int row = 0; row < b.Height; row++)
        {
            double cy = b.Y + row + 0.5;
            crossings.Clear();
            for (int i = 0; i < path.Count; i++)
            {
                var p1 = path[i];
                var p2 = path[(i + 1) % path.Count];
                if ((p1.Y > cy) != (p2.Y > cy))
                    crossings.Add(p1.X + (cy - p1.Y) * (p2.X - p1.X) / (p2.Y - p1.Y));
            }
            crossings.Sort();
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                // Pixel x is inside when its center x + 0.5 lies in [start, end).
                int from = Math.Max(b.X, (int)Math.Ceiling(crossings[i] - 0.5));
                int to = Math.Min(b.Right, (int)Math.Ceiling(crossings[i + 1] - 0.5));
                for (int x = from; x < to; x++)
                {
                    alpha[row * b.Width + x - b.X] = 255;
                    any = true;
                }
            }
        }
        return any ? new FreeformMask(b, alpha) : null;
    }
}
