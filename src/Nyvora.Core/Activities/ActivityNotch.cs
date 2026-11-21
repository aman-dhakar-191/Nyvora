using Nyvora.Core.Tasks;

namespace Nyvora.Core.Activities;

/// <summary>What the resting pill says about the live activities. Pure, so it can be tested without a window.</summary>
public static class ActivityNotch
{
    /// <summary>
    /// Priority: something waiting for the user's answer, then running work, then the most recent finished one.
    /// Null when there is nothing to show (the pill shows its usual counters).
    /// </summary>
    public static NotchMessage? For(IReadOnlyList<Activity> activities, DateTimeOffset now)
    {
        var live = activities.Where(a => a.IsActive(now)).OrderByDescending(a => a.UpdatedAt).ToList();
        if (live.Count == 0)
        {
            return null;
        }

        var waiting = live.Where(a => a.State == ActivityState.WaitingApproval && a.Decision == ApprovalDecision.None).ToList();
        if (waiting.Count > 0)
        {
            return Make(waiting.Count == 1 ? $"Approve: {waiting[0].Title}" : $"{waiting.Count} need your approval", NotchTone.Warning, waiting[0]);
        }

        // An answered question keeps the activity open until the agent moves on, so it counts as still running.
        var running = live.Where(a => !a.IsFinished).ToList();
        if (running.Count == 1)
        {
            var a = running[0];
            var progress = a.Step is { } s && a.Steps is { } n ? $" {s}/{n}" : "";
            return Make(a.Title + progress, NotchTone.Info, a);
        }

        if (running.Count > 1)
        {
            return Make($"{running.Count} activities running", NotchTone.Info, running[0]);
        }

        var last = live[0];
        return last.State == ActivityState.Failed
            ? Make($"{last.Title}: failed", NotchTone.Error, last)
            : Make($"{last.Title}: done", NotchTone.Success, last);
    }

    private static NotchMessage Make(string text, NotchTone tone, Activity from) => new()
    {
        Text = text.Length <= NotchMessage.MaxLength ? text : text[..(NotchMessage.MaxLength - 1)] + "…",
        Tone = tone,
        ExpiresAt = from.ExpiresAt,
    };
}
