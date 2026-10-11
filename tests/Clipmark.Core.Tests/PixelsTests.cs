using Clipmark.Core.Capture;

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
}
