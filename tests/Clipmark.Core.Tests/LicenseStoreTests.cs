using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

public class LicenseStoreTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void License_file_round_trips()
    {
        var file = new LicenseFile("ABCDE-FGHIJ", new TrialState(Start, Start.AddHours(5.5)));

        Assert.Equal(file, LicenseStore.Parse(LicenseStore.Serialize(file)));
    }

    [Fact]
    public void Empty_file_round_trips()
    {
        Assert.Equal(new LicenseFile(null, null), LicenseStore.Parse(LicenseStore.Serialize(new LicenseFile(null, null))));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    public void Broken_file_gives_an_empty_license(string json)
    {
        Assert.Equal(new LicenseFile(null, null), LicenseStore.Parse(json));
    }

    [Fact]
    public void Trial_with_a_bad_timestamp_is_dropped_but_the_key_is_kept()
    {
        var file = LicenseStore.Parse("""{ "key": "ABCDE", "trialStarted": "yesterday", "trialLastSeen": "2026-10-12T09:00:00Z" }""");

        Assert.Equal(new LicenseFile("ABCDE", null), file);
    }

    [Fact]
    public void Parsed_timestamps_are_utc()
    {
        var file = LicenseStore.Parse("""{ "trialStarted": "2026-10-12T11:00:00+02:00", "trialLastSeen": "2026-10-12T11:00:00+02:00" }""");

        Assert.Equal(new TrialState(Start, Start), file.Trial);
        Assert.Equal(DateTimeKind.Utc, file.Trial!.StartedUtc.Kind);
    }

    [Fact]
    public void Merge_keeps_the_earliest_start_and_the_latest_seen()
    {
        var file = new TrialState(Start.AddDays(3), Start.AddDays(4));
        var registry = new TrialState(Start, Start.AddDays(2));

        Assert.Equal(new TrialState(Start, Start.AddDays(4)), TrialClock.Merge(file, registry));
    }

    [Fact]
    public void Merge_with_one_copy_missing_keeps_the_other()
    {
        var registry = new TrialState(Start, Start.AddDays(2));

        Assert.Equal(registry, TrialClock.Merge(null, registry));
        Assert.Null(TrialClock.Merge(null, null));
    }
}
