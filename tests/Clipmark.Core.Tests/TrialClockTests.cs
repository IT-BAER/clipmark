using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

public class TrialClockTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void First_run_starts_a_30_day_trial()
    {
        var (daysLeft, next) = TrialClock.Check(null, Start);

        Assert.Equal(30, daysLeft);
        Assert.Equal(new TrialState(Start, Start), next);
    }

    [Theory]
    [InlineData(1, 29)]
    [InlineData(29.5, 1)]
    [InlineData(30, 0)]
    [InlineData(400, 0)]
    public void Days_left_count_down_and_round_up(double daysUsed, int expected)
    {
        var now = Start.AddDays(daysUsed);

        var (daysLeft, next) = TrialClock.Check(new TrialState(Start, Start), now);

        Assert.Equal(expected, daysLeft);
        Assert.Equal(new TrialState(Start, now), next);
    }

    [Fact]
    public void Turning_the_clock_back_does_not_return_trial_days()
    {
        var lastSeen = Start.AddDays(20);

        var (daysLeft, next) = TrialClock.Check(new TrialState(Start, lastSeen), Start.AddDays(1));

        Assert.Equal(10, daysLeft);
        Assert.Equal(lastSeen, next.LastSeenUtc);
    }

    [Fact]
    public void Edited_far_future_start_does_not_crash()
    {
        var farFuture = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var (daysLeft, _) = TrialClock.Check(new TrialState(farFuture, farFuture), Start);

        Assert.Equal(30, daysLeft);
    }

    [Fact]
    public void Clock_before_the_trial_start_gives_no_extra_days()
    {
        var (daysLeft, _) = TrialClock.Check(new TrialState(Start, Start), Start.AddDays(-100));

        Assert.Equal(30, daysLeft);
    }
}
