using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class ZoomMathTests
{
    [Fact]
    public void Fit_uses_the_smaller_ratio() => Assert.Equal(0.5, ZoomMath.Fit(500, 800, 1000, 1000), 6);

    [Fact]
    public void Fit_clamps_to_the_zoom_range()
    {
        Assert.Equal(8, ZoomMath.Fit(10000, 10000, 10, 10));
        Assert.Equal(0.1, ZoomMath.Fit(10, 10, 10000, 10000), 6);
    }

    [Fact]
    public void Fit_of_an_empty_content_keeps_100_percent() => Assert.Equal(1, ZoomMath.Fit(500, 500, 0, 100));

    [Fact]
    public void In_and_Out_step_by_1_25()
    {
        Assert.Equal(1.25, ZoomMath.In(1), 6);
        Assert.Equal(0.8, ZoomMath.Out(1), 6);
    }

    [Fact]
    public void In_and_Out_stop_at_the_limits()
    {
        Assert.Equal(8, ZoomMath.In(7));
        Assert.Equal(0.1, ZoomMath.Out(0.11), 6);
    }
}
