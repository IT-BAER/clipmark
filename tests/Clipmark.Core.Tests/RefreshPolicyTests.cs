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

    private static readonly LicenseFile Activated = new(null, null, null, Token, "inst", "tpm", Iat);

    [Fact]
    public void A_fresh_install_asks_for_a_trial()
    {
        Assert.True(RefreshPolicy.ShouldCall(new LicenseFile(null, null), Iat));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public void Without_a_token_the_last_attempt_backs_off_one_hour(int minutesAgo, bool expected)
    {
        var state = new LicenseFile(null, null, LastRefreshUtc: Iat);

        Assert.Equal(expected, RefreshPolicy.ShouldCall(state, Iat.AddMinutes(minutesAgo)));
    }

    [Fact]
    public void A_last_attempt_in_the_future_does_not_block_the_call()
    {
        Assert.True(RefreshPolicy.ShouldCall(new LicenseFile(null, null, LastRefreshUtc: Iat.AddDays(3)), Iat));
    }

    [Fact]
    public void A_v1_key_without_a_token_is_activated()
    {
        Assert.True(RefreshPolicy.ShouldCall(new LicenseFile("KEY", null), Iat));
        Assert.False(RefreshPolicy.ShouldCall(new LicenseFile("KEY", null, LastRefreshUtc: Iat), Iat.AddMinutes(30)));
    }

    [Fact]
    public void A_refused_trial_is_not_asked_again()
    {
        Assert.False(RefreshPolicy.ShouldCall(new LicenseFile(null, null, InstallId: "inst"), Iat));
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
        Assert.Equal(expected, RefreshPolicy.ShouldCall(Activated, Iat.AddSeconds(secondsAfterIat)));
    }

    [Fact]
    public void A_due_token_still_backs_off_one_hour()
    {
        var now = Iat.AddDays(20);

        Assert.False(RefreshPolicy.ShouldCall(Activated with { LastRefreshUtc = now.AddMinutes(-10) }, now));
    }

    [Fact]
    public void An_unreadable_token_is_refreshed()
    {
        Assert.True(RefreshPolicy.ShouldCall(Activated with { Entitlement = "garbage" }, Iat));
    }
}
