using System.Security.Cryptography;
using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

public class EntitlementTests
{
    private static readonly ECDsa Issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly string ValidKey = LicenseKey.Sign(new LicensePayload(42, new DateOnly(2026, 10, 12)), Issuer);

    [Fact]
    public void Valid_key_unlocks_premium_even_after_the_trial()
    {
        var e = Entitlement.Evaluate(ValidKey, Issuer, trialDaysLeft: 0);

        Assert.Equal(LicenseStatus.Licensed, e.Status);
        Assert.True(e.IsEnabled(PremiumFeature.Text));
    }

    [Fact]
    public void Running_trial_unlocks_premium()
    {
        var e = Entitlement.Evaluate(null, Issuer, trialDaysLeft: 12);

        Assert.Equal(new Entitlement(LicenseStatus.Trial, 12), e);
        Assert.True(e.IsEnabled(PremiumFeature.EditablePng));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AAAAA-BBBBB")]
    public void Expired_trial_without_a_valid_key_locks_every_premium_feature(string? key)
    {
        var e = Entitlement.Evaluate(key, Issuer, trialDaysLeft: 0);

        Assert.Equal(LicenseStatus.Free, e.Status);
        Assert.All(Enum.GetValues<PremiumFeature>(), f => Assert.False(e.IsEnabled(f)));
    }
}
