using Clipmark.Core.Capture;

namespace Clipmark.Core.Tests;

public class ScreenPickTests
{
    private static readonly PixelRect Left = new(-2560, 0, 2560, 1440);
    private static readonly PixelRect Primary = new(0, 0, 2560, 1440);
    private static readonly PixelRect Desktop = new(-2560, 0, 5120, 1440);

    [Theory]
    [InlineData(-1, 0, true)]
    [InlineData(-2560, 1439, true)]
    [InlineData(0, 0, false)]
    [InlineData(2559, 1439, false)]
    public void Monitor_at_point_handles_the_negative_origin_monitor(int x, int y, bool expectLeft)
    {
        var picked = ScreenPick.MonitorAt([Primary, Left], new PixelPoint(x, y));
        Assert.Equal(expectLeft ? Left : Primary, picked);
    }

    [Fact]
    public void Point_outside_every_monitor_picks_nothing()
    {
        Assert.Null(ScreenPick.MonitorAt([Primary, Left], new PixelPoint(0, 1440)));
    }

    [Fact]
    public void Window_at_point_is_the_topmost_one_containing_it()
    {
        var top = new PixelRect(100, 100, 400, 300);
        var below = new PixelRect(0, 0, 1000, 800);
        PixelRect[] topFirst = [top, below];

        Assert.Equal(top, ScreenPick.WindowAt(topFirst, new PixelPoint(150, 150), Desktop));
        Assert.Equal(below, ScreenPick.WindowAt(topFirst, new PixelPoint(900, 700), Desktop));
        Assert.Null(ScreenPick.WindowAt(topFirst, new PixelPoint(1500, 700), Desktop));
    }

    [Fact]
    public void Window_partly_off_the_desktop_is_clipped()
    {
        var overhang = new PixelRect(-2600, -50, 500, 300);
        Assert.Equal(new PixelRect(-2560, 0, 460, 250), ScreenPick.WindowAt([overhang], new PixelPoint(-2500, 10), Desktop));
    }
}
