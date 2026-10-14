using Clipmark.Core.Editor;
using Clipmark.Core.Settings;

namespace Clipmark.Core.Tests;

public class ColorSampleTests
{
    // 2 x 2 straight-alpha BGRA: (0,0) red, (1,0) green, (0,1) blue at half alpha, (1,1) transparent.
    private static readonly byte[] Pixels =
    [
        0, 0, 255, 255, 0, 255, 0, 255,
        255, 0, 0, 128, 0, 0, 0, 0,
    ];

    [Fact]
    public void At_reads_rgb_with_alpha_and_returns_null_outside_or_where_transparent()
    {
        Assert.Equal(new Rgba(255, 0, 0, 255), ColorSample.At(Pixels, 2, 2, 0, 0));
        Assert.Equal(new Rgba(0, 255, 0, 255), ColorSample.At(Pixels, 2, 2, 1, 0));
        Assert.Equal(new Rgba(0, 0, 255, 128), ColorSample.At(Pixels, 2, 2, 0, 1));
        Assert.Null(ColorSample.At(Pixels, 2, 2, 1, 1));
        Assert.Null(ColorSample.At(Pixels, 2, 2, 2, 0));
        Assert.Null(ColorSample.At(Pixels, 2, 2, 0, -1));
    }

    [Fact]
    public void Hex_is_uppercase_rrggbb_without_alpha()
    {
        Assert.Equal("#0A7BFF", ColorSample.Hex(new Rgba(10, 123, 255, 40)));
    }

    [Theory]
    [InlineData(0, 0, 0, "#000000", "rgb(0, 0, 0)", "hsl(0, 0%, 0%)")]
    [InlineData(255, 255, 255, "#FFFFFF", "rgb(255, 255, 255)", "hsl(0, 0%, 100%)")]
    [InlineData(128, 128, 128, "#808080", "rgb(128, 128, 128)", "hsl(0, 0%, 50%)")]
    [InlineData(255, 0, 0, "#FF0000", "rgb(255, 0, 0)", "hsl(0, 100%, 50%)")]
    [InlineData(0, 255, 0, "#00FF00", "rgb(0, 255, 0)", "hsl(120, 100%, 50%)")]
    [InlineData(0, 0, 255, "#0000FF", "rgb(0, 0, 255)", "hsl(240, 100%, 50%)")]
    [InlineData(255, 59, 48, "#FF3B30", "rgb(255, 59, 48)", "hsl(3, 100%, 59%)")]
    [InlineData(10, 123, 255, "#0A7BFF", "rgb(10, 123, 255)", "hsl(212, 100%, 52%)")]
    [InlineData(255, 0, 1, "#FF0001", "rgb(255, 0, 1)", "hsl(0, 100%, 50%)")] // hue 359.76 rounds to 360 = 0
    public void Format_writes_hex_rgb_and_hsl(int r, int g, int b, string hex, string rgb, string hsl)
    {
        var c = new Rgba((byte)r, (byte)g, (byte)b, 200);
        Assert.Equal(hex, ColorSample.Format(c, ColorFormat.Hex));
        Assert.Equal(rgb, ColorSample.Format(c, ColorFormat.Rgb));
        Assert.Equal(hsl, ColorSample.Format(c, ColorFormat.Hsl));
    }
}
