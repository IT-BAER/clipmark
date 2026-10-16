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
        verdict = EntitlementToken.Verify(file.Entitlement, keys, app, dev, new DateTimeOffset(nowUtc).ToUnixTimeSeconds(), out claims);
        return Evaluate(verdict, claims, nowUtc);
    }

    /// <summary>All premium features share one license today; the parameter names the gate at each call site.</summary>
    public bool IsEnabled(PremiumFeature feature) => Status != LicenseStatus.Free;
}
