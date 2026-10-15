namespace Clipmark.Core.Licensing;

/// <summary>When the app calls the licence server. Every attempt, success or not, sets LastRefreshUtc; attempts are at least an hour apart.</summary>
public static class RefreshPolicy
{
    public static readonly TimeSpan Backoff = TimeSpan.FromHours(1);

    public static bool ShouldCall(LicenseFile state, DateTime nowUtc)
    {
        // A last attempt in the future is a clock set back; it must not block calls until the clock catches up.
        if (state.LastRefreshUtc is { } last && last <= nowUtc && nowUtc - last < Backoff)
            return false;
        if (state.Entitlement is { } token)
        {
            if (EntitlementToken.ReadClaims(token) is not { } c)
                return true;
            long now = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
            return now >= c.Iat + (c.Exp - c.Iat) / 2;
        }
        // No token: activate a stored v1 key, else start the trial unless the server already answered for this
        // install (trial refused, or a licence activated and lost).
        return state.Key is not null || (state.InstallId is null && state.ActivatedUtc is null);
    }
}
