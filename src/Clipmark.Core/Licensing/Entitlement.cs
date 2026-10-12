using System.Security.Cryptography;

namespace Clipmark.Core.Licensing;

/// <summary>Everything beyond Snipping Tool. Editable PNGs still open without a license (view and flatten).</summary>
public enum PremiumFeature { Select, AttachedArrows, Text, Fill, Eyedropper, EditablePng }

public enum LicenseStatus { Licensed, Trial, Free }

/// <summary>What this install may use. <see cref="TrialDaysLeft"/> is only meaningful for <see cref="LicenseStatus.Trial"/>.</summary>
public sealed record Entitlement(LicenseStatus Status, int TrialDaysLeft = 0)
{
    /// <summary>A valid key wins over the trial; without one the trial decides.</summary>
    public static Entitlement Evaluate(string? key, ECDsa publicKey, int trialDaysLeft) =>
        LicenseKey.TryVerify(key, publicKey, out _) ? new(LicenseStatus.Licensed)
        : trialDaysLeft > 0 ? new(LicenseStatus.Trial, trialDaysLeft)
        : new(LicenseStatus.Free);

    /// <summary>All premium features share one license today; the parameter names the gate at each call site.</summary>
    public bool IsEnabled(PremiumFeature feature) => Status != LicenseStatus.Free;
}
