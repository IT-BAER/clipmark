using Clipmark.Core.Capture;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class PixelsTests
{
    [Fact]
    public void OverWhite_blends_straight_alpha_onto_white_and_makes_pixels_opaque()
    {
        byte[] bgra =
        [
            10, 20, 30, 255, // opaque: unchanged
            0, 0, 0, 0, // transparent: white
            0, 0, 200, 128, // half red: halfway to white
        ];
        Pixels.OverWhite(bgra);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 255, 255, 255, 255, 127, 127, 227, 255 }, bgra);
    }

    [Fact]
    public void AddBorder_grows_the_image_by_the_border_on_every_side_and_keeps_the_pixels()
    {
        byte[] bgra =
        [
            1, 2, 3, 255, 4, 5, 6, 0,
            7, 8, 9, 128, 10, 11, 12, 255,
        ];
        var (grown, w, h) = Pixels.AddBorder(bgra, 2, 2, 1, new Rgba(200, 100, 50, 7));
        Assert.Equal((4, 4, 4 * 4 * 4), (w, h, grown.Length));
        byte[] Px(int x, int y) => grown[((y * w + x) * 4)..((y * w + x) * 4 + 4)];
        byte[] border = [50, 100, 200, 255];
        foreach (var (x, y) in new[] { (0, 0), (3, 0), (0, 3), (3, 3), (1, 0), (0, 2), (3, 1), (2, 3) })
            Assert.Equal(border, Px(x, y));
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, Px(1, 1));
        Assert.Equal(new byte[] { 4, 5, 6, 0 }, Px(2, 1)); // transparent stays transparent
        Assert.Equal(new byte[] { 7, 8, 9, 128 }, Px(1, 2));
        Assert.Equal(new byte[] { 10, 11, 12, 255 }, Px(2, 2));
    }

    [Fact]
    public void AddBorder_handles_a_one_pixel_image_and_the_largest_border()
    {
        var (grown, w, h) = Pixels.AddBorder([9, 8, 7, 255], 1, 1, 20, new Rgba(1, 2, 3, 255));
        Assert.Equal((41, 41), (w, h));
        Assert.Equal(new byte[] { 9, 8, 7, 255 }, grown[((20 * 41 + 20) * 4)..((20 * 41 + 20) * 4 + 4)]);
        Assert.Equal(new byte[] { 3, 2, 1, 255 }, grown[..4]);
        Assert.Equal(new byte[] { 3, 2, 1, 255 }, grown[^4..]);
    }
}
