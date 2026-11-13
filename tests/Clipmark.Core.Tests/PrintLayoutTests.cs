using Clipmark.Core.Layout;

namespace Clipmark.Core.Tests;

public sealed class PrintLayoutTests
{
    [Fact]
    public void Place_keeps_a_small_image_at_full_size_and_centers_it()
    {
        Assert.Equal((300.0, 400.0, 200.0, 100.0), PrintLayout.Place(200, 100, 100, 100, 600, 700));
    }

    [Fact]
    public void Place_shrinks_a_wide_image_to_the_area_width()
    {
        Assert.Equal((48.0, 345.5, 720.0, 405.0), PrintLayout.Place(1920, 1080, 48, 48, 720, 1000));
    }

    [Fact]
    public void Place_shrinks_a_tall_image_to_the_area_height()
    {
        Assert.Equal((350.0, 0.0, 100.0, 1000.0), PrintLayout.Place(200, 2000, 0, 0, 800, 1000));
    }
}
