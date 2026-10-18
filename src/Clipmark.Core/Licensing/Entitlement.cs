using System.Security.Cryptography;

namespace Clipmark.Core.Licensing;

/// <summary>Everything beyond Snipping Tool. Editable PNGs still open without a license (view and flatten).</summary>
public enum PremiumFeature { Select, AttachedArrows, Text, Fill, Eyedropper, EditablePng, SendToAi, History, AiChat }

public enum LicenseStatus { Licensed, Trial, Free }

/// <summary>What this install may use. <see cref="TrialDaysLeft"/> is only meaningful for <see cref="LicenseStatus.Trial"/>.</summary>
public sealed record Entitlement(LicenseStatus Status, int TrialDaysLeft = 0)
{
    /// <summary>Length of the server trial, for the Premium dialog text.</summary>
    public const int TrialDays = 30;

    /// <summary>A verified entitlement: plan "trial" counts days to exp (rounded down, at least 1 while valid; exp is server time); any other plan is Premium; any failed check is Free.</summary>
    public static Entitlement Evaluate(TokenVerdict verdict, EntitlementClaims? claims, DateTime nowUtc)
    {
        if (verdict != TokenVerdict.Valid || claims is null)
            return new(LicenseStatus.Free);
        if (claims.Plan != "trial")
            return new(LicenseStatus.Licensed);
        long seconds = claims.Exp - new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
        return new(LicenseStatus.Trial, (int)Math.Max(1, seconds / 86400));
    }

    /// <summary>license.json's state: only its rl1 entitlement, verified for this app and device, unlocks Premium; a v1 key does not.</summary>
    public static Entitlement Evaluate(LicenseFile file, IReadOnlyDictionary<string, ECDsa> keys, string app, string dev, DateTime nowUtc,
        out TokenVerdict verdict, out EntitlementClaims? claims)
    {
        if (file.HighWaterUtc is { } mark && nowUtc < mark - ClockTolerance)
        {
            (verdict, claims) = (TokenVerdict.ClockBehind, null);
            return new(LicenseStatus.Free);
        }
        verdict = EntitlementToken.Verify(file.Entitlement, keys, app, dev, new DateTimeOffset(nowUtc).ToUnixTimeSeconds(), out claims);
        return Evaluate(verdict, claims, nowUtc);
    }

    /// <summary>All premium features share one license today; the parameter names the gate at each call site.</summary>
    public bool IsEnabled(PremiumFeature feature) => Status != LicenseStatus.Free;

    /// <summary>
    /// Clock set back (releash integration.md B4): a local now this far below the highest time seen is not licensed. Above the
    /// 300 s server skew and normal time corrections; the mark is saved hourly, so a rollback of up to about two hours is not caught.
    /// </summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(1);

    /// <summary>The file with the high-water mark raised to now; null (nothing to write) while the stored mark is less than an hour older than now.</summary>
    public static LicenseFile? RaiseHighWater(LicenseFile file, DateTime nowUtc) =>
        file.HighWaterUtc is { } mark && nowUtc - mark < ClockTolerance ? null : file with { HighWaterUtc = nowUtc };

    /// <summary>
    /// A token from the server replaces the stored state only if it verifies here (integration.md B3); null keeps the stored state.
    /// It removes a v1 key and a stored rate limit and sets the high-water mark to its iat (server time), so a clock once far ahead recovers.
    /// </summary>
    public static LicenseFile? Accept(LicenseFile file, string token, IReadOnlyDictionary<string, ECDsa> keys, string app, string dev, DateTime nowUtc)
    {
        if (EntitlementToken.Verify(token, keys, app, dev, new DateTimeOffset(nowUtc).ToUnixTimeSeconds(), out var claims) != TokenVerdict.Valid)
            return null;
        return file with { Entitlement = token, Key = null, RetryAfterUtc = null, HighWaterUtc = DateTimeOffset.FromUnixTimeSeconds(claims!.Iat).UtcDateTime };
    }

    /// <summary>The plan feature that IT-BAER's relay (Ask AI) needs.</summary>
    public const string RelayFeature = "ai";

    /// <summary>A verified, non-trial entitlement whose plan includes <see cref="RelayFeature"/>.</summary>
    public static bool AllowsRelay(TokenVerdict verdict, EntitlementClaims? claims) =>
        verdict == TokenVerdict.Valid && claims is { Plan: not "trial" } && claims.Feat.Contains(RelayFeature);
}
