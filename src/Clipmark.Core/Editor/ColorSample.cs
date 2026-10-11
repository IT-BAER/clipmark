namespace Clipmark.Core.Editor;

/// <summary>Eyedropper lookups in a straight-alpha BGRA composite.</summary>
public static class ColorSample
{
    /// <summary>Pixel at (x, y), or null outside the buffer or where fully transparent (nothing painted).</summary>
    public static Rgba? At(ReadOnlySpan<byte> bgra, int width, int height, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return null;
        int i = (y * width + x) * 4;
        return bgra[i + 3] == 0 ? null : new Rgba(bgra[i + 2], bgra[i + 1], bgra[i], bgra[i + 3]);
    }

    public static string Hex(Rgba c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
