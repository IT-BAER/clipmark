using Clipmark.Core.Recording;

namespace Clipmark.Core.Tests;

public class RecordingClockTests
{
    private static RecordingClock Started(long at = 1000)
    {
        var clock = new RecordingClock();
        clock.Start(at);
        return clock;
    }

    [Fact]
    public void Without_pause_media_time_is_the_time_since_start()
    {
        var clock = Started();
        Assert.False(clock.IsPaused);
        Assert.Equal(0, clock.MediaAt(1000));
        Assert.Equal(500, clock.MediaAt(1500));
        Assert.True(clock.TryMap(1500, out long media));
        Assert.Equal(500, media);
    }

    [Fact]
    public void One_pause_is_cut_out()
    {
        var clock = Started();
        clock.Pause(1200);
        Assert.True(clock.IsPaused);
        clock.Resume(1500);
        Assert.False(clock.IsPaused);
        Assert.Equal(100, clock.MediaAt(1100)); // before
        Assert.Equal(200, clock.MediaAt(1300)); // inside: the pause start
        Assert.Equal(200, clock.MediaAt(1500)); // resume point
        Assert.Equal(300, clock.MediaAt(1600)); // after
        Assert.True(clock.TryMap(1100, out long before));
        Assert.Equal(100, before);
        Assert.False(clock.TryMap(1300, out _));
        Assert.True(clock.TryMap(1600, out long after));
        Assert.Equal(300, after);
    }

    [Fact]
    public void Two_pauses_are_both_cut_out()
    {
        var clock = Started();
        clock.Pause(1100);
        clock.Resume(1300);
        clock.Pause(1500);
        clock.Resume(2000);
        Assert.Equal(50, clock.MediaAt(1050));
        Assert.Equal(100, clock.MediaAt(1200));
        Assert.Equal(150, clock.MediaAt(1350));
        Assert.Equal(300, clock.MediaAt(1700));
        Assert.Equal(400, clock.MediaAt(2100));
        Assert.False(clock.TryMap(1200, out _));
        Assert.False(clock.TryMap(1700, out _));
        Assert.True(clock.TryMap(1350, out long between));
        Assert.Equal(150, between);
        Assert.True(clock.TryMap(2100, out long after));
        Assert.Equal(400, after);
    }

    [Fact]
    public void Media_time_never_decreases()
    {
        var clock = Started();
        clock.Pause(1100);
        clock.Resume(1300);
        clock.Pause(1500);
        clock.Resume(2000);
        clock.Pause(2200);
        long last = long.MinValue;
        for (long t = 1000; t <= 3000; t += 7)
        {
            long media = clock.MediaAt(t);
            Assert.True(media >= last, $"MediaAt({t}) = {media} < {last}");
            last = media;
        }
    }

    [Fact]
    public void A_running_pause_holds_media_time_at_its_start()
    {
        var clock = Started();
        clock.Pause(1400);
        Assert.Equal(400, clock.MediaAt(1400));
        Assert.Equal(400, clock.MediaAt(5000)); // stop while paused
        Assert.False(clock.TryMap(5000, out _));
    }

    [Fact]
    public void Pause_while_paused_and_resume_while_running_are_ignored()
    {
        var clock = Started();
        clock.Resume(1100);
        Assert.Equal(200, clock.MediaAt(1200));
        clock.Pause(1200);
        clock.Pause(1300);
        clock.Resume(1400);
        clock.Resume(1450);
        Assert.Equal(300, clock.MediaAt(1500));
    }
}
