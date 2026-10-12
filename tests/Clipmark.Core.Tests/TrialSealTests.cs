using System.Text;
using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

public class TrialSealTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Device = Encoding.UTF8.GetBytes("device-a");
    private static readonly byte[] OtherDevice = Encoding.UTF8.GetBytes("device-b");
    private static readonly LicenseFile Missing = new(null, null);

    private static LicenseFile Sealed(TrialState state, byte[]? device = null) =>
        new(null, state, TrialSeal.Sign(state, device ?? Device));

    [Fact]
    public void Signed_state_verifies()
    {
        var state = new TrialState(Start, Start.AddDays(2));

        Assert.True(TrialSeal.Verify(state, TrialSeal.Sign(state, Device), Device));
    }

    [Fact]
    public void Edited_dates_do_not_verify()
    {
        var state = new TrialState(Start, Start.AddDays(2));
        string tag = TrialSeal.Sign(state, Device);

        Assert.False(TrialSeal.Verify(state with { StartedUtc = Start.AddDays(10) }, tag, Device));
        Assert.False(TrialSeal.Verify(state with { LastSeenUtc = Start }, tag, Device));
    }

    [Fact]
    public void Tag_from_another_device_does_not_verify()
    {
        var state = new TrialState(Start, Start);

        Assert.False(TrialSeal.Verify(state, TrialSeal.Sign(state, OtherDevice), Device));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    public void Missing_or_garbled_tag_does_not_verify(string? tag)
    {
        Assert.False(TrialSeal.Verify(new TrialState(Start, Start), tag, Device));
    }

    [Fact]
    public void No_copies_start_a_new_trial()
    {
        var (daysLeft, next) = TrialSeal.Check([Missing, Missing, Missing], Start);

        Assert.Equal(30, daysLeft);
        Assert.Equal(new TrialState(Start, Start), next);
    }

    [Fact]
    public void One_surviving_copy_keeps_the_trial_running()
    {
        var vault = Sealed(new TrialState(Start, Start.AddDays(5)));

        var (daysLeft, next) = TrialSeal.Check([Missing, Missing, vault], Start.AddDays(10));

        Assert.Equal(20, daysLeft);
        Assert.Equal(new TrialState(Start, Start.AddDays(10)), next);
    }

    [Fact]
    public void Valid_copies_merge_to_the_earliest_start_and_latest_seen()
    {
        var file = Sealed(new TrialState(Start.AddDays(3), Start.AddDays(4)));
        var registry = Sealed(new TrialState(Start, Start.AddDays(2)));
        var vault = Sealed(new TrialState(Start.AddDays(1), Start.AddDays(25)));

        var (daysLeft, next) = TrialSeal.Check([file, registry, vault], Start.AddDays(6));

        Assert.Equal(5, daysLeft);
        Assert.Equal(new TrialState(Start, Start.AddDays(25)), next);
    }

    [Fact]
    public void An_edited_copy_cannot_extend_the_trial()
    {
        var valid = Sealed(new TrialState(Start, Start.AddDays(1)));
        var edited = valid with { Trial = new TrialState(Start.AddDays(1), Start.AddDays(1)) };

        var (daysLeft, next) = TrialSeal.Check([edited, valid, Missing], Start.AddDays(2));

        Assert.Equal(28, daysLeft);
        Assert.Equal(new TrialState(Start, Start.AddDays(2)), next);
    }

    [Fact]
    public void An_invalid_copy_still_shortens_the_trial()
    {
        var valid = Sealed(new TrialState(Start.AddDays(5), Start.AddDays(5)));
        var edited = valid with { Trial = new TrialState(Start, Start.AddDays(5)) };

        var (daysLeft, next) = TrialSeal.Check([valid, edited, Missing], Start.AddDays(5));

        Assert.Equal(25, daysLeft);
        Assert.Equal(new TrialState(Start, Start.AddDays(5)), next);
    }

    [Fact]
    public void Copies_sealed_on_another_device_keep_their_days()
    {
        var copy = Sealed(new TrialState(Start, Start.AddDays(5)), OtherDevice);

        var (daysLeft, next) = TrialSeal.Check([copy, copy, copy], Start.AddDays(10));

        Assert.Equal(20, daysLeft);
        Assert.Equal(new TrialState(Start, Start.AddDays(10)), next);
    }

    [Fact]
    public void An_untagged_trial_keeps_its_days()
    {
        var (daysLeft, next) = TrialSeal.Check([new LicenseFile(null, new TrialState(Start, Start)), Missing], Start.AddDays(1));

        Assert.Equal(29, daysLeft);
        Assert.Equal(new TrialState(Start, Start.AddDays(1)), next);
    }

    [Fact]
    public void A_tag_whose_dates_were_broken_counts_as_missing()
    {
        var broken = new LicenseFile(null, null, TrialSeal.Sign(new TrialState(Start, Start), Device));

        var (daysLeft, next) = TrialSeal.Check([broken, Missing], Start);

        Assert.Equal(30, daysLeft);
        Assert.Equal(new TrialState(Start, Start), next);
    }

    [Fact]
    public void Tag_round_trips_through_the_license_file()
    {
        var file = Sealed(new TrialState(Start, Start.AddHours(5.5))) with { Key = "ABCDE" };

        var parsed = LicenseStore.Parse(LicenseStore.Serialize(file));

        Assert.Equal(file, parsed);
        Assert.True(TrialSeal.Verify(parsed.Trial, parsed.TrialTag, Device));
    }

    [Fact]
    public void Tag_is_kept_when_the_dates_are_broken()
    {
        var file = LicenseStore.Parse("""{ "trialStarted": "yesterday", "trialLastSeen": "2026-10-12T09:00:00Z", "trialTag": "abc=" }""");

        Assert.Equal(new LicenseFile(null, null, "abc="), file);
    }
}
