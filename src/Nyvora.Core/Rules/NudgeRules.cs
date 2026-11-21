using Nyvora.Core.Tasks;

namespace Nyvora.Core.Rules;

/// <summary>The built-in rules. Each looks only at the task list and the clock.</summary>
public static class NudgeRules
{
    public static IReadOnlyList<INudgeRule> All { get; } = new INudgeRule[]
    {
        new OverdueBacklog(),
        new EndOfDay(),
        new TodaysPlan(),
        new StaleHighPriority(),
    };

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    /// <summary>Three or more open tasks whose reminders have passed.</summary>
    private sealed class OverdueBacklog : INudgeRule
    {
        public const int Threshold = 3;

        public string Id => "overdue-backlog";

        public string Name => "Overdue backlog";

        public string Description => $"{Threshold} or more tasks are overdue (their reminders have passed).";

        public TimeSpan Cooldown => TimeSpan.FromHours(4);

        public Nudge? Evaluate(NudgeContext c)
        {
            var n = c.Tasks.Count(t => TaskList.IsDue(t, c.Now));
            return n >= Threshold ? new Nudge(Id, Name, $"{n} tasks are overdue", NotchTone.Warning) : null;
        }
    }

    /// <summary>Weekdays 17:30 to 20:00, once a day: high-priority tasks still open.</summary>
    private sealed class EndOfDay : INudgeRule
    {
        public string Id => "end-of-day";

        public string Name => "End-of-day check";

        public string Description => "On weekdays after 17:30, high-priority tasks are still open.";

        public TimeSpan Cooldown => TimeSpan.FromHours(20);

        public Nudge? Evaluate(NudgeContext c)
        {
            var local = c.Now.TimeOfDay;
            if (c.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || local < TimeSpan.FromHours(17.5))
            {
                return null;
            }

            var n = c.Tasks.Count(t => !t.Done && t.Priority == TaskPriority.High);
            return n > 0 ? new Nudge(Id, Name, $"{Count(n, "high-priority task", "high-priority tasks")} still open", NotchTone.Info) : null;
        }
    }

    /// <summary>Weekdays 09:00 to 12:00, once a day: what has a reminder today, and what is high priority.</summary>
    private sealed class TodaysPlan : INudgeRule
    {
        public string Id => "todays-plan";

        public string Name => "Today's plan";

        public string Description => "On weekday mornings, a short summary of reminders today and high-priority tasks.";

        public TimeSpan Cooldown => TimeSpan.FromHours(20);

        public Nudge? Evaluate(NudgeContext c)
        {
            var local = c.Now.TimeOfDay;
            if (c.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || local < TimeSpan.FromHours(9) || local >= TimeSpan.FromHours(12))
            {
                return null;
            }

            var today = c.Tasks.Count(t => TaskList.IsDue(t, c.Now) || TaskList.IsLaterToday(t, c.Now));
            var high = c.Tasks.Count(t => !t.Done && t.Priority == TaskPriority.High);
            if (today == 0 && high == 0)
            {
                return null;
            }

            var parts = new List<string>();
            if (today > 0)
            {
                parts.Add(Count(today, "reminder", "reminders"));
            }

            if (high > 0)
            {
                parts.Add($"{high} high priority");
            }

            return new Nudge(Id, Name, "Today: " + string.Join(", ", parts), NotchTone.Info);
        }
    }

    /// <summary>An open high-priority task, 3 or more days old, with no reminder: it may be forgotten.</summary>
    private sealed class StaleHighPriority : INudgeRule
    {
        public static readonly TimeSpan MinAge = TimeSpan.FromDays(3);

        public string Id => "stale-high-priority";

        public string Name => "Forgotten high priority";

        public string Description => "A high-priority task with no reminder has been open for 3 days or more.";

        public TimeSpan Cooldown => TimeSpan.FromHours(24);

        public Nudge? Evaluate(NudgeContext c)
        {
            var stale = c.Tasks
                .Where(t => !t.Done && t.Priority == TaskPriority.High && t.RemindAt is null && c.Now - t.Created >= MinAge)
                .OrderBy(t => t.Created)
                .ToList();
            if (stale.Count == 0)
            {
                return null;
            }

            var days = (int)(c.Now - stale[0].Created).TotalDays;
            var title = stale[0].Title.Length <= 22 ? stale[0].Title : stale[0].Title[..21] + "…";
            return stale.Count == 1
                ? new Nudge(Id, Name, $"“{title}” is {days} days old", NotchTone.Warning)
                : new Nudge(Id, Name, $"{stale.Count} high-priority tasks are 3+ days old", NotchTone.Warning);
        }
    }
}
