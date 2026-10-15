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
    public void Activation_fields_round_trip()
    {
        var file = new LicenseFile(null, null, null, "rl1.a.b", "install-1", "tpm", Start, Start.AddHours(1));

        Assert.Equal(file, LicenseStore.Parse(LicenseStore.Serialize(file)));
    }

    [Fact]
    public void Absent_activation_fields_serialize_as_null()
    {
        string json = LicenseStore.Serialize(new LicenseFile("K", null));

        foreach (string name in new[] { "entitlement", "installId", "keyStore", "activatedUtc", "lastRefreshUtc" })
            Assert.Contains($"\"{name}\": null", json);
    }

    [Fact]
    public void An_old_file_with_key_and_trial_only_still_parses()
    {
        var file = LicenseStore.Parse("""{ "key": "ABCDE", "trialStarted": "2026-10-12T09:00:00Z", "trialLastSeen": "2026-10-12T09:00:00Z", "trialTag": "t" }""");

        Assert.Equal(new LicenseFile("ABCDE", new TrialState(Start, Start), "t"), file);
    }

    [Fact]
    public void Trial_fields_are_not_written_once_an_entitlement_is_stored()
    {
        var file = new LicenseFile(null, new TrialState(Start, Start), "t", "rl1.a.b", "install-1");

        Assert.Equal(file with { Trial = null, TrialTag = null }, LicenseStore.Parse(LicenseStore.Serialize(file)));
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
