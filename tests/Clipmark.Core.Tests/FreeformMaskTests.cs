using Clipmark.Core.Capture;

namespace Clipmark.Core.Tests;

public class FreeformMaskTests
{
    private static readonly PixelRect Desktop = new(-2560, 0, 5120, 1440);

    private static PixelPoint[] Path(params int[] xy) =>
        Enumerable.Range(0, xy.Length / 2).Select(i => new PixelPoint(xy[2 * i], xy[2 * i + 1])).ToArray();

    private static int Inside(FreeformMask m) => m.Alpha.Count(a => a == 255);

    [Fact]
    public void Square_path_covers_its_whole_box()
    {
        var m = FreeformMask.Create(Path(-10, 5, -6, 5, -6, 9, -10, 9), Desktop)!;
        Assert.Equal(new PixelRect(-10, 5, 4, 4), m.Bounds);
        Assert.Equal(16, Inside(m));
    }

    [Fact]
    public void Triangle_keeps_only_pixels_whose_center_is_inside()
    {
        // Centers (x+0.5, y+0.5) are inside when x + y < 3: 1 + 2 + 3 pixels.
        var m = FreeformMask.Create(Path(0, 0, 4, 0, 0, 4), Desktop)!;
        Assert.Equal(6, Inside(m));
        Assert.Equal(255, m.Alpha[0]);
        Assert.Equal(0, m.Alpha[3 * 4 + 3]);
    }

    [Fact]
    public void Concave_l_shape_leaves_the_notch_transparent()
    {
        var m = FreeformMask.Create(Path(0, 0, 4, 0, 4, 2, 2, 2, 2, 4, 0, 4), Desktop)!;
        Assert.Equal(12, Inside(m));
        Assert.Equal(0, m.Alpha[3 * 4 + 3]);
        Assert.Equal(255, m.Alpha[3 * 4 + 1]);
    }

    [Fact]
    public void Path_leaving_the_desktop_is_clipped()
    {
        var m = FreeformMask.Create(Path(-2600, 10, -2550, 10, -2550, 20, -2600, 20), Desktop)!;
        Assert.Equal(new PixelRect(-2560, 10, 10, 10), m.Bounds);
        Assert.Equal(100, Inside(m));
    }

    [Fact]
    public void Path_without_area_gives_null()
    {
        Assert.Null(FreeformMask.Create(Path(0, 0, 10, 10), Desktop));
        Assert.Null(FreeformMask.Create(Path(0, 0, 10, 0, 20, 0), Desktop));
    }
}
