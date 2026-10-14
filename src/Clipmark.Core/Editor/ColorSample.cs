using System.Globalization;
using Clipmark.Core.Settings;

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

    /// <summary>The color as text: #RRGGBB, rgb(r, g, b) or hsl(h, s%, l%) (hue 0-359, integers); alpha is not written.</summary>
    public static string Format(Rgba c, ColorFormat format) => format switch
    {
        ColorFormat.Rgb => string.Create(CultureInfo.InvariantCulture, $"rgb({c.R}, {c.G}, {c.B})"),
        ColorFormat.Hsl => Hsl(c),
        _ => Hex(c),
    };

    private static string Hsl(Rgba c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, l = (max + min) / 2;
        double h = d == 0 ? 0 : max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        int hue = (int)Math.Round((h * 60 + 360) % 360) % 360;
        return string.Create(CultureInfo.InvariantCulture, $"hsl({hue}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)");
    }
}
