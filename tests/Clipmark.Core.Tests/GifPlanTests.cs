using Clipmark.Core.Recording;

namespace Clipmark.Core.Tests;

public class GifPlanTests
{
    [Theory]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(1080, 1920, 720, 1280)]
    [InlineData(800, 600, 800, 600)]
    [InlineData(101, 51, 100, 50)]
    [InlineData(1916, 1128, 1280, 754)]
    public void Size_caps_the_long_edge_keeps_the_aspect_and_never_upscales(int w, int h, int ew, int eh) =>
        Assert.Equal((ew, eh), GifPlan.Size(w, h, GifPlan.MaxEdge));

    [Fact]
    public void Low_is_12_fps_and_high_is_24_fps()
    {
        Assert.Equal(12, GifPlan.Fps(GifQuality.Low));
        Assert.Equal(24, GifPlan.Fps(GifQuality.High));
    }

    [Theory]
    [InlineData(10.0, 12, 120)]
    [InlineData(10.0, 24, 240)]
    [InlineData(10.05, 12, 121)]
    [InlineData(45.0, 12, 360)]
    [InlineData(0.01, 24, 1)]
    public void Frame_count_covers_the_duration_up_to_30_seconds(double seconds, int fps, int expected) =>
        Assert.Equal(expected, GifPlan.FrameCount(TimeSpan.FromSeconds(seconds), fps));

    [Theory]
    [InlineData(12, 120, 1000)]
    [InlineData(24, 240, 1000)]
    [InlineData(24, 720, 3000)]
    public void Delays_add_up_to_the_frame_time(int fps, int frames, int totalCs)
    {
        var delays = Enumerable.Range(0, frames).Select(i => GifPlan.DelayCs(i, fps)).ToList();
        Assert.Equal(totalCs, delays.Sum());
        Assert.All(delays, d => Assert.InRange(d, 100 / fps, 100 / fps + 1));
    }

    [Fact]
    public void Frame_times_are_evenly_spaced() =>
        Assert.Equal([0, 833_333, 1_666_666], Enumerable.Range(0, 3).Select(i => GifPlan.FrameTime(i, 12).Ticks));

    [Fact]
    public void A_low_gif_over_the_budget_gets_a_smaller_edge_until_it_fits()
    {
        Assert.Null(GifPlan.SmallerEdge(GifPlan.LowBudgetBytes, 1280));
        Assert.Equal(576, GifPlan.SmallerEdge(GifPlan.LowBudgetBytes * 4, 1280));
        Assert.Equal(GifPlan.MinEdge, GifPlan.SmallerEdge(GifPlan.LowBudgetBytes * 100, 400));
        Assert.Null(GifPlan.SmallerEdge(GifPlan.LowBudgetBytes * 2, GifPlan.MinEdge));
    }
}
