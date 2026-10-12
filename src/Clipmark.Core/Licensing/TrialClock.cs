namespace Clipmark.Core.Licensing;

/// <summary>Stored trial timestamps (UTC). LastSeenUtc never moves backwards.</summary>
public sealed record TrialState(DateTime StartedUtc, DateTime LastSeenUtc);

/// <summary>
/// Trial countdown. Turning the clock back does not return trial days: time counts from the latest moment ever seen.
/// Honest-user gate only; the source is open.
/// </summary>
public static class TrialClock
{
    public const int TrialDays = 30;

    /// <summary>Days left (0 = expired, partial days round up) and the state to store.</summary>
    public static (int DaysLeft, TrialState Next) Check(TrialState? stored, DateTime nowUtc)
    {
        var state = stored ?? new TrialState(nowUtc, nowUtc);
        var seen = nowUtc > state.LastSeenUtc ? nowUtc : state.LastSeenUtc;
        var left = TimeSpan.FromDays(TrialDays) - (seen - state.StartedUtc); // no AddDays: overflows near DateTime.MaxValue
        int daysLeft = left <= TimeSpan.Zero ? 0 : Math.Min(TrialDays, (int)Math.Ceiling(left.TotalDays));
        return (daysLeft, state with { LastSeenUtc = seen });
    }

    /// <summary>Combines the file copy and the registry mirror: earliest start, latest moment seen.</summary>
    public static TrialState? Merge(TrialState? a, TrialState? b) =>
        a is null ? b
        : b is null ? a
        : new TrialState(a.StartedUtc < b.StartedUtc ? a.StartedUtc : b.StartedUtc,
                         a.LastSeenUtc > b.LastSeenUtc ? a.LastSeenUtc : b.LastSeenUtc);
}
