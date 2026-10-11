using Clipmark.Core.Capture;
using Clipmark.Core.Recording;

namespace Clipmark.Core.Tests;

public class RecordingTests
{
    private static readonly PixelRect Left = new(-2560, 0, 2560, 1440), Primary = new(0, 0, 2560, 1440);
    private static readonly PixelRect[] Monitors = [Primary, Left];

    [Fact]
    public void Region_inside_one_monitor_keeps_its_place()
    {
        var area = RecordingRegion.Fit(new PixelRect(100, 200, 640, 480), Monitors);
        Assert.Equal(new RecordingArea(Primary, new PixelRect(100, 200, 640, 480)), area);
    }

    [Fact]
    public void Odd_sizes_are_evened_down_for_h264()
    {
        var area = RecordingRegion.Fit(new PixelRect(10, 10, 641, 479), Monitors);
        Assert.Equal(new PixelRect(10, 10, 640, 478), area!.Value.Region);
    }

    [Fact]
    public void Region_across_monitors_goes_to_the_larger_overlap_and_is_clipped()
    {
        var area = RecordingRegion.Fit(new PixelRect(-300, 100, 400, 300), Monitors);
        Assert.Equal(new RecordingArea(Left, new PixelRect(-300, 100, 300, 300)), area);
    }

    [Fact]
    public void Tiny_region_grows_to_the_minimum_inside_its_monitor()
    {
        var area = RecordingRegion.Fit(new PixelRect(2550, 1430, 10, 10), Monitors);
        Assert.Equal(new PixelRect(2560 - RecordingRegion.MinSize, 1440 - RecordingRegion.MinSize, RecordingRegion.MinSize, RecordingRegion.MinSize),
            area!.Value.Region);
    }

    [Fact]
    public void Region_outside_every_monitor_cannot_be_recorded()
    {
        Assert.Null(RecordingRegion.Fit(new PixelRect(5000, 0, 100, 100), Monitors));
    }

    [Theory]
    [InlineData(1920, 1080, 30, 6_220_800)]
    [InlineData(64, 64, 30, 1_000_000)]
    [InlineData(7680, 4320, 60, 40_000_000)]
    public void Bitrate_is_a_tenth_of_a_bit_per_pixel_within_limits(int w, int h, int fps, int expected)
    {
        Assert.Equal((uint)expected, VideoBitrate.For(w, h, fps));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(65.9, "01:05")]
    [InlineData(3723, "1:02:03")]
    public void Timer_text(double seconds, string expected)
    {
        Assert.Equal(expected, RecordingTime.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Recording_file_name_uses_the_mp4_extension()
    {
        var taken = new HashSet<string> { "Clipmark 2026-10-11 160509.mp4" };
        Assert.Equal("Clipmark 2026-10-11 160509 (2).mp4", CaptureFileName.Next(new DateTime(2026, 10, 11, 16, 5, 9), taken.Contains, ".mp4"));
    }
}
