using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Clipmark.Core.Licensing;

/// <summary>
/// Binds each stored trial copy to this device: HMAC-SHA256 over the timestamps, keyed by a device secret the caller supplies.
/// Shows edited dates and copies from another PC (--trial-status); the source is open, so it is no defense against a patched build.
/// </summary>
public static class TrialSeal
{
    public static string Sign(TrialState state, byte[] deviceSecret) =>
        Convert.ToBase64String(HMACSHA256.HashData(deviceSecret, Message(state)));

    public static bool Verify(TrialState? state, string? tag, byte[] deviceSecret)
    {
        if (state is null || string.IsNullOrEmpty(tag))
            return false;
        Span<byte> given = stackalloc byte[HMACSHA256.HashSizeInBytes];
        return Convert.TryFromBase64String(tag, given, out int length) && length == given.Length
            && CryptographicOperations.FixedTimeEquals(given, HMACSHA256.HashData(deviceSecret, Message(state)));
    }

    /// <summary>
    /// Days left and the state to store (re-signed) in every copy. All copies merge to the earliest start and latest
    /// moment seen, sealed or not: merging can only shorten the trial, and a device change must not expire it.
    /// </summary>
    public static (int DaysLeft, TrialState Next) Check(IEnumerable<LicenseFile> copies, DateTime nowUtc)
    {
        TrialState? merged = null;
        foreach (var copy in copies)
            merged = TrialClock.Merge(merged, copy.Trial);
        return TrialClock.Check(merged, nowUtc);
    }

    private static byte[] Message(TrialState state) => Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
        $"clipmark-trial-v1|{state.StartedUtc.ToUniversalTime().Ticks}|{state.LastSeenUtc.ToUniversalTime().Ticks}"));
}
