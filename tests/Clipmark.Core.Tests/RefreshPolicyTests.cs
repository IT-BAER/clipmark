using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

public sealed class RefreshPolicyTests
{
    private static readonly DateTime Iat = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly long IatUnix = new DateTimeOffset(Iat).ToUnixTimeSeconds();

    // Unchecked token text: the policy only reads iat and exp.
    private static readonly string Token = EntitlementTokenTests.Sign(
        $$"""{"v":1,"kid":"k","app":"clipmark","lic":"1","plan":"premium","feat":[],"seats":3,"dev":"d","iat":{{IatUnix}},"exp":{{IatUnix + 30 * 86400}},"grace":1209600}""",
        EntitlementTokenTests.VectorKey());

    private static readonly LicenseFile Activated = new(null, Token, "inst", "tpm", Iat);

    [Fact]
    public void A_fresh_install_asks_for_a_trial()
    {
        Assert.True(RefreshPolicy.ShouldCall(new LicenseFile(null), Iat));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public void Without_a_token_the_last_attempt_backs_off_one_hour(int minutesAgo, bool expected)
    {
        var state = new LicenseFile(null, LastRefreshUtc: Iat);

        Assert.Equal(expected, RefreshPolicy.ShouldCall(state, Iat.AddMinutes(minutesAgo)));
    }

    [Fact]
    public void A_last_attempt_in_the_future_does_not_block_the_call()
    {
        Assert.True(RefreshPolicy.ShouldCall(new LicenseFile(null, LastRefreshUtc: Iat.AddDays(3)), Iat));
    }

    [Fact]
    public void A_v1_key_without_a_token_is_activated()
    {
        Assert.True(RefreshPolicy.ShouldCall(new LicenseFile("KEY"), Iat));
        Assert.False(RefreshPolicy.ShouldCall(new LicenseFile("KEY", LastRefreshUtc: Iat), Iat.AddMinutes(30)));
    }

    [Fact]
    public void A_refused_trial_is_not_asked_again()
    {
        Assert.False(RefreshPolicy.ShouldCall(new LicenseFile(null, InstallId: "inst"), Iat));
    }

    [Fact]
    public void An_activated_install_whose_token_was_dropped_is_not_called()
    {
        Assert.False(RefreshPolicy.ShouldCall(Activated with { Entitlement = null }, Iat.AddDays(20)));
    }

    [Theory]
    [InlineData(15 * 86400 - 1, false)]
    [InlineData(15 * 86400, true)]
    [InlineData(60 * 86400, true)]
    public void A_token_is_refreshed_once_half_its_lifetime_passed(int secondsAfterIat, bool expected)
    {
        var now = Iat.AddSeconds(secondsAfterIat);

        Assert.Equal(expected, RefreshPolicy.ShouldCall(Activated with { LastRefreshUtc = now.AddHours(-2) }, now));
    }

    [Fact]
    public void A_due_token_still_backs_off_one_hour()
    {
        var now = Iat.AddDays(20);

        Assert.False(RefreshPolicy.ShouldCall(Activated with { LastRefreshUtc = now.AddMinutes(-10) }, now));
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(23, false)]
    public void A_licence_token_is_refreshed_a_day_after_the_last_attempt(int hoursAgo, bool expected)
    {
        var now = Iat.AddDays(5); // half-TTL is 15 days away

        Assert.Equal(expected, RefreshPolicy.ShouldCall(Activated with { LastRefreshUtc = now.AddHours(-hoursAgo) }, now));
    }

    [Fact]
    public void A_trial_token_waits_for_half_its_lifetime()
    {
        string trial = EntitlementTokenTests.Sign(
            $$"""{"v":1,"kid":"k","app":"clipmark","lic":"","plan":"trial","feat":[],"seats":1,"dev":"d","iat":{{IatUnix}},"exp":{{IatUnix + 30 * 86400}},"grace":0}""",
            EntitlementTokenTests.VectorKey());
        var now = Iat.AddDays(5);

        Assert.False(RefreshPolicy.ShouldCall(Activated with { Entitlement = trial, LastRefreshUtc = now.AddHours(-25) }, now));
    }

    [Fact]
    public void A_licence_token_with_a_last_attempt_in_the_future_is_refreshed()
    {
        Assert.True(RefreshPolicy.ShouldCall(Activated with { LastRefreshUtc = Iat.AddDays(30) }, Iat.AddDays(5)));
    }

    [Fact]
    public void A_v1_key_next_to_a_garbage_entitlement_is_activated()
    {
        Assert.Equal(LicenceCall.Activate, RefreshPolicy.NextCall(new LicenseFile("KEY", Entitlement: "garbage")));
    }

    [Fact]
    public void Each_state_makes_its_server_call()
    {
        Assert.Equal(LicenceCall.Activate, RefreshPolicy.NextCall(new LicenseFile("KEY")));
        Assert.Equal(LicenceCall.Refresh, RefreshPolicy.NextCall(Activated));
        Assert.Equal(LicenceCall.Refresh, RefreshPolicy.NextCall(Activated with { Key = "KEY" }));
        Assert.Equal(LicenceCall.Refresh, RefreshPolicy.NextCall(Activated with { Entitlement = "garbage" }));
        Assert.Equal(LicenceCall.Trial, RefreshPolicy.NextCall(new LicenseFile(null)));
    }

    [Fact]
    public void An_unreadable_token_is_refreshed()
    {
        Assert.True(RefreshPolicy.ShouldCall(Activated with { Entitlement = "garbage" }, Iat));
    }
}
