using System.Security.Cryptography;
using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

public class EntitlementTests
{
    private static readonly ECDsa Issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly string ValidKey = LicenseKey.Sign(new LicensePayload(42, new DateOnly(2026, 10, 12)), Issuer);
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly long NowUnix = new DateTimeOffset(Now).ToUnixTimeSeconds();

    private static EntitlementClaims Claims(string plan, long exp, long grace = 0) =>
        new("kid", "clipmark", "", plan, [], 1, "dev", NowUnix - 3600, exp, grace);

    [Fact]
    public void A_valid_premium_token_unlocks_premium()
    {
        var e = Entitlement.Evaluate(TokenVerdict.Valid, Claims("premium", NowUnix + 86400 * 30), Now);

        Assert.Equal(new Entitlement(LicenseStatus.Licensed), e);
        Assert.True(e.IsEnabled(PremiumFeature.Text));
    }

    [Theory]
    [InlineData(86400L * 12, 12)]
    [InlineData(86400L * 12 - 1, 12)]
    [InlineData(86400L * 11 + 1, 12)]
    [InlineData(1L, 1)]
    public void A_valid_trial_token_counts_the_days_left_rounded_up(long secondsLeft, int days)
    {
        var e = Entitlement.Evaluate(TokenVerdict.Valid, Claims("trial", NowUnix + secondsLeft), Now);

        Assert.Equal(new Entitlement(LicenseStatus.Trial, days), e);
        Assert.True(e.IsEnabled(PremiumFeature.EditablePng));
    }

    [Fact]
    public void A_trial_token_still_valid_in_its_grace_shows_one_day()
    {
        Assert.Equal(new Entitlement(LicenseStatus.Trial, 1), Entitlement.Evaluate(TokenVerdict.Valid, Claims("trial", NowUnix - 10, grace: 60), Now));
    }

    [Theory]
    [InlineData(TokenVerdict.Expired)]
    [InlineData(TokenVerdict.IssuedInFuture)]
    [InlineData(TokenVerdict.UnknownKid)]
    [InlineData(TokenVerdict.WrongApp)]
    [InlineData(TokenVerdict.WrongDev)]
    [InlineData(TokenVerdict.BadSignature)]
    [InlineData(TokenVerdict.Malformed)]
    public void Any_failed_check_locks_every_premium_feature(TokenVerdict verdict)
    {
        var e = Entitlement.Evaluate(verdict, Claims("premium", NowUnix + 86400), Now);

        Assert.Equal(LicenseStatus.Free, e.Status);
        Assert.All(Enum.GetValues<PremiumFeature>(), f => Assert.False(e.IsEnabled(f)));
    }

    [Fact]
    public void Valid_without_claims_is_free()
    {
        Assert.Equal(LicenseStatus.Free, Entitlement.Evaluate(TokenVerdict.Valid, null, Now).Status);
    }

    [Fact]
    public void A_valid_v1_key_unlocks_premium_offline()
    {
        Assert.Equal(new Entitlement(LicenseStatus.Licensed), Entitlement.EvaluateLegacy(ValidKey, Issuer));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AAAAA-BBBBB")]
    public void A_missing_or_invalid_v1_key_is_free(string? key)
    {
        Assert.Equal(LicenseStatus.Free, Entitlement.EvaluateLegacy(key, Issuer).Status);
    }
}
