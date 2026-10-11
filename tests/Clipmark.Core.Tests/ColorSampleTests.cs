using Clipmark.Core.Editor;

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
}
