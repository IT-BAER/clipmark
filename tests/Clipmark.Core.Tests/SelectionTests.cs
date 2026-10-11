using Clipmark.Core.Capture;

namespace Clipmark.Core.Tests;

public class SelectionTests
{
    private static readonly PixelRect Desktop = new(-2560, 0, 5120, 1440);

    [Fact]
    public void Drag_up_and_left_gives_the_same_rect_as_drag_down_and_right()
    {
        var down = Selection.FromDrag(new PixelPoint(-100, 50), new PixelPoint(200, 300), Desktop);
        var up = Selection.FromDrag(new PixelPoint(200, 300), new PixelPoint(-100, 50), Desktop);

        Assert.Equal(new PixelRect(-100, 50, 300, 250), down);
        Assert.Equal(down, up);
    }

    [Fact]
    public void Drag_past_the_desktop_edge_is_clipped()
    {
        var rect = Selection.FromDrag(new PixelPoint(-3000, -20), new PixelPoint(-2500, 100), Desktop);
        Assert.Equal(new PixelRect(-2560, 0, 60, 100), rect);
    }

    [Theory]
    [InlineData(10, 10, 10, 10)] // click without drag
    [InlineData(10, 10, 300, 10)] // zero height
    [InlineData(3000, 0, 3100, 100)] // fully outside
    public void Empty_selection_is_null(int x1, int y1, int x2, int y2)
    {
        Assert.Null(Selection.FromDrag(new PixelPoint(x1, y1), new PixelPoint(x2, y2), Desktop));
    }
}
