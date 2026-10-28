namespace Nyvora.Core.Tasks;

/// <summary>
/// Decides when to nudge the user (e.g. hourly "update your task statuses"). Time is passed in, so it is
/// deterministic to test. Not thread-safe: use it from the UI thread only.
/// </summary>
public sealed class RecurringNudge
{
    private DateTimeOffset _next;

    public RecurringNudge(TimeSpan interval, DateTimeOffset now)
    {
        SetInterval(interval, now);
    }

    public TimeSpan Interval { get; private set; }

    /// <summary>A zero or negative interval turns nudging off.</summary>
    public bool Enabled => Interval > TimeSpan.Zero;

    /// <summary>Changes the interval and restarts the countdown from <paramref name="now"/>.</summary>
    public void SetInterval(TimeSpan interval, DateTimeOffset now)
    {
        Interval = interval > TimeSpan.Zero ? interval : TimeSpan.Zero;
        _next = now + Interval;
    }

    /// <summary>
    /// True when a nudge is due and there is something to nudge about. The next one is always scheduled a full
    /// interval after <paramref name="now"/>, so waking from sleep produces one nudge, not a burst.
    /// </summary>
    public bool TryFire(DateTimeOffset now, int openTasks)
    {
        if (!Enabled || now < _next)
        {
            return false;
        }

        _next = now + Interval;
        return openTasks > 0;
    }
}
