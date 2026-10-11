using Clipmark.Core.Capture;

namespace Clipmark.Core.Tests;

public class VirtualScreenTests
{
    [Fact]
    public void Monitor_left_of_primary_maps_to_the_start_of_the_bitmap()
    {
        // Left monitor at x = -2560, primary at 0: the virtual screen starts at -2560.
        var left = new PixelRect(-2560, 0, 2560, 1440);
        var primary = new PixelRect(0, 0, 2560, 1440);
        var screen = VirtualScreen.FromMonitors([primary, left]);

        Assert.Equal(new PixelRect(-2560, 0, 5120, 1440), screen.Bounds);
        Assert.Equal(new PixelRect(0, 0, 2560, 1440), screen.CropFor(left));
        Assert.Equal(new PixelRect(2560, 0, 2560, 1440), screen.CropFor(primary));
    }

    [Fact]
    public void Monitors_with_different_sizes_and_vertical_offset_cover_their_union()
    {
        var primary = new PixelRect(0, 0, 3840, 2160);
        var above = new PixelRect(500, -1080, 1920, 1080);
        var screen = VirtualScreen.FromMonitors([primary, above]);

        Assert.Equal(new PixelRect(0, -1080, 3840, 3240), screen.Bounds);
        Assert.Equal(new PixelRect(500, 0, 1920, 1080), screen.CropFor(above));
        Assert.Equal(new PixelRect(0, 1080, 3840, 2160), screen.CropFor(primary));
    }

    [Fact]
    public void Crop_outside_the_virtual_screen_is_rejected()
    {
        var screen = VirtualScreen.FromMonitors([new PixelRect(0, 0, 1920, 1080)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.CropFor(new PixelRect(1900, 0, 100, 100)));
    }
}
