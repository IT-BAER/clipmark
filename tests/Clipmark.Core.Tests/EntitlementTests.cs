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
    [InlineData(86400L * 30 + 1, 30)]
    [InlineData(86400L * 12, 12)]
    [InlineData(86400L * 12 - 1, 11)]
    [InlineData(86400L * 11 + 1, 11)]
    [InlineData(1L, 1)]
    public void A_valid_trial_token_counts_the_days_left_rounded_down(long secondsLeft, int days)
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

    private static readonly ECDsa Vector = EntitlementTokenTests.VectorKey();

    private static string Token(string plan) => EntitlementTokenTests.Sign(
        $$"""{"v":1,"kid":"dGVzdGtpZDE","app":"clipmark","lic":"1","plan":"{{plan}}","feat":[],"seats":1,"dev":"dev","iat":{{NowUnix - 3600}},"exp":{{NowUnix + 30 * 86400}},"grace":0}""",
        Vector);

    private static Entitlement ForFile(LicenseFile file) =>
        Entitlement.Evaluate(file, EntitlementTokenTests.KeysFor(Vector), "clipmark", "dev", Now, out _, out _);

    [Fact]
    public void A_verified_entitlement_in_the_file_unlocks_premium()
    {
        Assert.Equal(new Entitlement(LicenseStatus.Licensed), ForFile(new LicenseFile(null, Token("premium"))));
    }

    [Fact]
    public void Forged_local_trial_fields_without_an_install_id_are_free()
    {
        var file = LicenseStore.Parse("""{ "trialStarted": "2026-10-13T12:00:00Z", "trialLastSeen": "2026-10-13T12:00:00Z", "trialTag": "forged" }""");

        Assert.Equal(new Entitlement(LicenseStatus.Free), ForFile(file));
    }

    [Fact]
    public void A_valid_v1_key_alone_is_free()
    {
        Assert.Equal(new Entitlement(LicenseStatus.Free), ForFile(new LicenseFile(ValidKey)));
    }

    [Fact]
    public void A_garbage_entitlement_next_to_a_valid_v1_key_is_free()
    {
        Assert.Equal(new Entitlement(LicenseStatus.Free), ForFile(new LicenseFile(ValidKey, "garbage")));
    }

    [Fact]
    public void A_clock_set_back_past_the_high_water_mark_locks_premium()
    {
        var file = new LicenseFile(null, Token("premium"), HighWaterUtc: Now.AddHours(1).AddSeconds(1));

        var e = Entitlement.Evaluate(file, EntitlementTokenTests.KeysFor(Vector), "clipmark", "dev", Now, out var verdict, out var claims);

        Assert.Equal(new Entitlement(LicenseStatus.Free), e);
        Assert.Equal(TokenVerdict.ClockBehind, verdict);
        Assert.Null(claims);
    }

    [Fact]
    public void A_clock_within_the_tolerance_of_the_high_water_mark_stays_licensed()
    {
        Assert.Equal(new Entitlement(LicenseStatus.Licensed), ForFile(new LicenseFile(null, Token("premium"), HighWaterUtc: Now.AddHours(1))));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(-61, true)]
    [InlineData(-59, false)]
    [InlineData(30, false)]
    public void The_high_water_mark_rises_to_now_once_it_is_an_hour_old(int? markMinutesFromNow, bool raised)
    {
        var file = new LicenseFile(null, HighWaterUtc: markMinutesFromNow is { } m ? Now.AddMinutes(m) : null);

        var next = Entitlement.RaiseHighWater(file, Now);

        if (raised)
            Assert.Equal(file with { HighWaterUtc = Now }, next);
        else
            Assert.Null(next);
    }

    [Fact]
    public void A_verified_token_is_stored_and_resets_the_high_water_mark_to_its_iat()
    {
        var old = new LicenseFile(ValidKey, "old", "inst", HighWaterUtc: Now.AddDays(400), RetryAfterUtc: Now.AddDays(20));
        string token = Token("premium");

        var next = Entitlement.Accept(old, token, EntitlementTokenTests.KeysFor(Vector), "clipmark", "dev", Now, out _);

        Assert.Equal(old with { Key = null, Entitlement = token, HighWaterUtc = Now.AddHours(-1), RetryAfterUtc = null }, next);
    }

    [Fact]
    public void A_token_that_fails_verification_is_not_stored()
    {
        var old = new LicenseFile(ValidKey, Token("premium"), "inst");

        Assert.Null(Entitlement.Accept(old, Token("premium"), EntitlementTokenTests.KeysFor(Vector), "clipmark", "other-dev", Now, out _));
        Assert.Null(Entitlement.Accept(old, "garbage", EntitlementTokenTests.KeysFor(Vector), "clipmark", "dev", Now, out _));
    }

    private const string ClockText = "The clock of this PC is off. Check the Windows date and time, then try again.";
    private const string KeyText = "This PC's key was not accepted. Try again; if it keeps failing, contact support.";
    private const string UpdateText = "This license needs a newer version of Clipmark. Update Clipmark, then activate it again.";

    [Theory]
    [InlineData("dev", -2, ClockText)]
    [InlineData("dev", 31 * 24, ClockText)]
    [InlineData("other-dev", 0, KeyText)]
    [InlineData("", 0, KeyText)]
    public void A_rejected_server_token_names_the_reason(string dev, int nowHours, string text)
    {
        var next = Entitlement.Accept(new LicenseFile(null), Token("premium"), EntitlementTokenTests.KeysFor(Vector), "clipmark", dev, Now.AddHours(nowHours), out var verdict);

        Assert.Null(next);
        Assert.Equal(text, Entitlement.RejectedText(verdict));
    }

    [Fact]
    public void A_server_token_this_build_cannot_read_asks_for_an_update()
    {
        Assert.Null(Entitlement.Accept(new LicenseFile(null), "garbage", EntitlementTokenTests.KeysFor(Vector), "clipmark", "dev", Now, out var verdict));
        Assert.Equal(UpdateText, Entitlement.RejectedText(verdict));
        Assert.Equal(UpdateText, Entitlement.RejectedText(TokenVerdict.UnknownKid));
    }

    private static EntitlementClaims WithFeat(string plan, params string[] feat) =>
        new("kid", "clipmark", "1", plan, feat, 1, "dev", NowUnix - 3600, NowUnix + 86400, 0);

    [Fact]
    public void The_relay_needs_a_verified_licence_with_the_ai_feature()
    {
        Assert.True(Entitlement.AllowsRelay(TokenVerdict.Valid, WithFeat("premium", "ai")));
        Assert.False(Entitlement.AllowsRelay(TokenVerdict.Valid, WithFeat("premium")));
        Assert.False(Entitlement.AllowsRelay(TokenVerdict.Valid, WithFeat("trial", "ai")));
        Assert.False(Entitlement.AllowsRelay(TokenVerdict.ClockBehind, WithFeat("premium", "ai")));
        Assert.False(Entitlement.AllowsRelay(TokenVerdict.Valid, null));
    }
}
