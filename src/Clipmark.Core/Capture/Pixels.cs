namespace Clipmark.Core.Capture;

public static class Pixels
{
    /// <summary>Blends BGRA pixels with straight alpha onto white in place and makes them opaque (for formats without alpha).</summary>
    public static void OverWhite(byte[] bgra)
    {
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            for (int c = 0; c < 3; c++)
                bgra[i + c] = (byte)((bgra[i + c] * a + 255 * (255 - a)) / 255);
            bgra[i + 3] = 255;
        }
    }

    /// <summary>Averages factor x factor blocks of a BGRA buffer; blocks cut by the right / bottom edge average what they hold.</summary>
    public static (byte[] Bgra, int Width, int Height) Shrink(byte[] bgra, int width, int height, int factor)
    {
        int w = (width + factor - 1) / factor, h = (height + factor - 1) / factor;
        var small = new byte[w * h * 4];
        var sum = new int[4];
        for (int by = 0; by < h; by++)
            for (int bx = 0; bx < w; bx++)
            {
                Array.Clear(sum);
                int n = 0;
                for (int y = by * factor; y < Math.Min(height, (by + 1) * factor); y++)
                    for (int x = bx * factor; x < Math.Min(width, (bx + 1) * factor); x++, n++)
                        for (int c = 0; c < 4; c++)
                            sum[c] += bgra[(y * width + x) * 4 + c];
                for (int c = 0; c < 4; c++)
                    small[(by * w + bx) * 4 + c] = (byte)((sum[c] + n / 2) / n);
            }
        return (small, w, h);
    }

    /// <summary>Adds an opaque border of the color outside the image (nothing is covered); the pixels are copied unchanged.</summary>
    public static (byte[] Bgra, int Width, int Height) AddBorder(byte[] bgra, int width, int height, int border, Editor.Rgba color)
    {
        int w = width + 2 * border, h = height + 2 * border;
        var grown = new byte[w * h * 4];
        Fill(grown, w, new PixelRect(0, 0, w, h), color with { A = 255 });
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(bgra, y * width * 4, grown, ((y + border) * w + border) * 4, width * 4);
        return (grown, w, h);
    }

    /// <summary>Paints the rect, clipped to the image, with one color in a BGRA buffer of the given width.</summary>
    public static void Fill(byte[] bgra, int width, PixelRect rect, Editor.Rgba color)
    {
        int height = bgra.Length / 4 / width;
        if (rect.Intersect(new PixelRect(0, 0, width, height)) is not { } r) return;
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                int i = (y * width + x) * 4;
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (color.B, color.G, color.R, color.A);
            }
    }
}
