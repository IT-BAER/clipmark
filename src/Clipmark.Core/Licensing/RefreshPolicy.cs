using Clipmark.Core.Relay;

namespace Clipmark.Core.Licensing;

/// <summary>Which licence server call a due sync makes.</summary>
public enum LicenceCall { Refresh, Activate, Trial }

/// <summary>When the app calls the licence server. Every attempt, success or not, sets LastRefreshUtc; attempts are at least an hour apart and none is made before RetryAfterUtc.</summary>
public static class RefreshPolicy
{
    public static readonly TimeSpan Backoff = TimeSpan.FromHours(1);

    /// <summary>A licence (non-trial) entitlement is re-checked this often, so a removed or revoked seat locks within about a day.</summary>
    public static readonly TimeSpan Daily = TimeSpan.FromHours(24);

    public static bool ShouldCall(LicenseFile state, DateTime nowUtc)
    {
        if (state.RetryAfterUtc is { } until && nowUtc < until)
            return false;
        // A last attempt in the future is a clock set back; it must not block calls until the clock catches up.
        if (state.LastRefreshUtc is { } last && last <= nowUtc && nowUtc - last < Backoff)
            return false;
        if (state.Entitlement is { } token)
        {
            // Clock behind the high-water mark: only a fresh token (Entitlement.Accept) can lower the mark after a clock was ahead.
            if (state.HighWaterUtc is { } mark && nowUtc < mark - Entitlement.ClockTolerance)
                return true;
            if (EntitlementToken.ReadClaims(token) is not { } c)
                return true;
            long now = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
            if (now >= c.Iat + (c.Exp - c.Iat) / 2)
                return true;
            var since = state.LastRefreshUtc ?? DateTimeOffset.FromUnixTimeSeconds(c.Iat).UtcDateTime;
            return c.Plan != "trial" && (since > nowUtc || nowUtc - since >= Daily);
        }
        // No token: activate a stored v1 key, else start the trial unless the server already answered for this
        // install (trial refused, or a licence activated and lost).
        return state.Key is not null || (state.InstallId is null && state.ActivatedUtc is null);
    }

    /// <summary>Longest stored wait; bounds a wrong clock or a broken retry_after.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromDays(31);

    /// <summary>
    /// The end of a 30-day cap (integration.md B7: rate_limited with retry_after over an hour on trial or activate), stored so no
    /// background call is made before it; null for shorter limits, which the hourly backoff covers.
    /// </summary>
    public static DateTime? WaitUntil(RelayFailure failure, DateTime nowUtc) =>
        failure is { Status: 429, Code: "rate_limited", RetryAfter: { } seconds } && seconds > 3600
            ? nowUtc + TimeSpan.FromSeconds(Math.Min(seconds, MaxWait.TotalSeconds))
            : null;

    /// <summary>A v1 key is activated while no readable entitlement exists; a readable non-trial one is refreshed; else the trial call.</summary>
    public static LicenceCall NextCall(LicenseFile state)
    {
        var claims = EntitlementToken.ReadClaims(state.Entitlement);
        if (state.Key is not null && claims is null)
            return LicenceCall.Activate;
        return state.Entitlement is not null && claims?.Plan != "trial" ? LicenceCall.Refresh : LicenceCall.Trial;
    }
}
