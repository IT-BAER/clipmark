using System.Security.Cryptography;

namespace Clipmark.Core.Licensing;

/// <summary>Everything beyond Snipping Tool. Editable PNGs still open without a license (view and flatten).</summary>
public enum PremiumFeature { Select, AttachedArrows, Text, Fill, Eyedropper, EditablePng, SendToAi, History, AiChat }

public enum LicenseStatus { Licensed, Trial, Free }

/// <summary>What this install may use. <see cref="TrialDaysLeft"/> is only meaningful for <see cref="LicenseStatus.Trial"/>.</summary>
public sealed record Entitlement(LicenseStatus Status, int TrialDaysLeft = 0)
{
    /// <summary>A verified entitlement: plan "trial" counts days to exp (rounded up, at least 1 while valid); any other plan is Premium; any failed check is Free.</summary>
    public static Entitlement Evaluate(TokenVerdict verdict, EntitlementClaims? claims, DateTime nowUtc)
    {
        if (verdict != TokenVerdict.Valid || claims is null)
            return new(LicenseStatus.Free);
        if (claims.Plan != "trial")
            return new(LicenseStatus.Licensed);
        double days = (claims.Exp - new DateTimeOffset(nowUtc).ToUnixTimeSeconds()) / 86400.0;
        return new(LicenseStatus.Trial, Math.Max(1, (int)Math.Ceiling(days)));
    }

    /// <summary>A v1 offline key, honoured only until it is activated on the server.</summary>
    public static Entitlement EvaluateLegacy(string? key, ECDsa publicKey) =>
        LicenseKey.TryVerify(key, publicKey, out _) ? new(LicenseStatus.Licensed) : new(LicenseStatus.Free);

    /// <summary>All premium features share one license today; the parameter names the gate at each call site.</summary>
    public bool IsEnabled(PremiumFeature feature) => Status != LicenseStatus.Free;
}
