using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class ImageFitTests
{
    [Theory]
    [InlineData(2560, 1440, 2560, 1440)]
    [InlineData(2576, 2576, 2576, 2576)]
    [InlineData(7040, 1748, 2576, 640)]
    [InlineData(3840, 2160, 2576, 1449)]
    [InlineData(1000, 5000, 515, 2576)]
    [InlineData(1, 10000, 1, 2576)]
    public void Fit_scales_the_long_edge_down_to_the_limit(int w, int h, int expectedW, int expectedH)
    {
        Assert.Equal((expectedW, expectedH), ImageFit.Fit(w, h));
    }

    [Fact]
    public void Fit_honours_a_custom_limit()
    {
        Assert.Equal((100, 50), ImageFit.Fit(400, 200, maxLongEdge: 100));
    }
}
