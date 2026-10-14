namespace Clipmark.Core.Recording;

/// <summary>
/// Media time of a recording that can pause: a wall-clock timestamp (any tick unit, e.g. QPC) minus the start and the
/// paused intervals before it. Thread-safe.
/// </summary>
public sealed class RecordingClock
{
    private readonly object _lock = new();
    private readonly List<(long Start, long End)> _pauses = []; // End is long.MaxValue while paused
    private long _start;

    public void Start(long at)
    {
        lock (_lock)
        {
            _start = at;
            _pauses.Clear();
        }
    }

    public bool IsPaused
    {
        get { lock (_lock) return Paused; }
    }

    private bool Paused => _pauses.Count > 0 && _pauses[^1].End == long.MaxValue;

    /// <summary>Ignored while paused.</summary>
    public void Pause(long at)
    {
        lock (_lock)
            if (!Paused)
                _pauses.Add((at, long.MaxValue));
    }

    /// <summary>Ignored while running.</summary>
    public void Resume(long at)
    {
        lock (_lock)
            if (Paused)
                _pauses[^1] = (_pauses[^1].Start, Math.Max(at, _pauses[^1].Start));
    }

    /// <summary>Media time at <paramref name="at"/>; inside a pause it stays at the pause start, so it never decreases.</summary>
    public long MediaAt(long at)
    {
        lock (_lock)
            return Map(at).Media;
    }

    /// <summary>False for a timestamp inside a pause (audio captured while paused is dropped).</summary>
    public bool TryMap(long at, out long media)
    {
        lock (_lock)
        {
            (media, bool inside) = Map(at);
            return !inside;
        }
    }

    private (long Media, bool Inside) Map(long at)
    {
        long paused = 0;
        foreach (var (start, end) in _pauses)
        {
            if (at < start)
                break;
            if (at < end)
                return (start - _start - paused, true);
            paused += end - start;
        }
        return (at - _start - paused, false);
    }
}
