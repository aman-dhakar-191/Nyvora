using Nyvora.Core.Rules;
using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class RuleEngineTests
{
    // Tuesday 2026-11-24 (a weekday), local time = the offset's own clock.
    private static DateTimeOffset At(int h, int m = 0, int day = 24) => new(2026, 11, day, h, m, 0, TimeSpan.FromHours(5.5));

    private static readonly DateTimeOffset Start = At(8);

    private static TaskItem T(string title, TaskPriority p = TaskPriority.None, DateTimeOffset? remind = null, DateTimeOffset? created = null, bool done = false) =>
        new() { Id = Guid.NewGuid(), Title = title, Priority = p, RemindAt = remind, Created = created ?? At(8, 0, 20), Done = done };

    private static NudgeContext Ctx(DateTimeOffset now, params TaskItem[] tasks) => new(now, tasks);

    private static readonly ISet<string> None = new HashSet<string>();

    private sealed class Always : INudgeRule
    {
        public Always(string id, TimeSpan? cooldown = null) { Id = id; Cooldown = cooldown ?? TimeSpan.FromHours(1); }
        public string Id { get; }
        public string Name => Id;
        public string Description => "";
        public TimeSpan Cooldown { get; }
        public Nudge? Evaluate(NudgeContext c) => new(Id, Id, "hi " + Id, NotchTone.Info);
    }

    private static RuleEngine Engine(params INudgeRule[] rules) => new(rules, Start) { StartupGrace = TimeSpan.FromMinutes(10), MinGap = TimeSpan.FromMinutes(30), MaxPerDay = 3 };

    [Fact]
    public void NeverFiresRightAfterStartup()
    {
        var e = Engine(new Always("a"));
        Assert.Null(e.Evaluate(Ctx(At(8, 5)), None));
        Assert.NotNull(e.Evaluate(Ctx(At(8, 11)), None));
    }

    [Fact]
    public void StaysQuietOutsideActiveHours()
    {
        var e = Engine(new Always("a"));
        Assert.Null(e.Evaluate(Ctx(At(7, 59)), None));
        Assert.Null(e.Evaluate(Ctx(At(20, 0)), None));
        Assert.NotNull(e.Evaluate(Ctx(At(19, 59)), None));
    }

    [Fact]
    public void GlobalGapAndPerRuleCooldownApply()
    {
        var e = Engine(new Always("a", TimeSpan.FromHours(2)), new Always("b", TimeSpan.FromHours(2)));
        Assert.Equal("a", e.Evaluate(Ctx(At(9)), None)!.RuleId);
        Assert.Null(e.Evaluate(Ctx(At(9, 20)), None));                    // inside the 30 min gap
        Assert.Equal("b", e.Evaluate(Ctx(At(9, 31)), None)!.RuleId);      // a is cooling down, b goes
        Assert.Null(e.Evaluate(Ctx(At(10, 5)), None));                    // both cooling down
        Assert.Equal("a", e.Evaluate(Ctx(At(11, 1)), None)!.RuleId);
    }

    [Fact]
    public void AtMostMaxPerDayAndTheCountResetsTomorrow()
    {
        var e = Engine(new Always("a", TimeSpan.Zero));
        Assert.NotNull(e.Evaluate(Ctx(At(9)), None));
        Assert.NotNull(e.Evaluate(Ctx(At(10)), None));
        Assert.NotNull(e.Evaluate(Ctx(At(11)), None));
        Assert.Null(e.Evaluate(Ctx(At(12)), None));
        Assert.NotNull(e.Evaluate(Ctx(At(9, 0, 25)), None));
    }

    [Fact]
    public void AMutedRuleNeverRuns()
    {
        var e = Engine(new Always("a"), new Always("b"));
        Assert.Equal("b", e.Evaluate(Ctx(At(9)), new HashSet<string> { "a" })!.RuleId);
        Assert.Null(Engine(new Always("a")).Evaluate(Ctx(At(9)), new HashSet<string> { "a" }));
    }

    // ----- the built-in rules -----

    private static Nudge? Run(string ruleId, NudgeContext c) => NudgeRules.All.Single(r => r.Id == ruleId).Evaluate(c);

    [Fact]
    public void OverdueBacklogNeedsThreeDueTasks()
    {
        var now = At(10);
        var due = new[] { T("a", remind: now.AddHours(-1)), T("b", remind: now.AddMinutes(-5)), T("c", remind: now.AddDays(-1)) };
        Assert.Null(Run("overdue-backlog", Ctx(now, due[0], due[1])));
        Assert.Equal("3 tasks are overdue", Run("overdue-backlog", Ctx(now, due))!.Message);
        Assert.Null(Run("overdue-backlog", Ctx(now, T("a", remind: now.AddHours(-1), done: true), due[1], due[2])));
    }

    [Fact]
    public void EndOfDayOnlyOnWeekdayEveningsWithHighPriorityOpen()
    {
        var high = T("ship", TaskPriority.High);
        Assert.Equal("1 high-priority task still open", Run("end-of-day", Ctx(At(18), high))!.Message);
        Assert.Null(Run("end-of-day", Ctx(At(17, 0), high)));                       // too early
        Assert.Null(Run("end-of-day", Ctx(At(18, 0, 28), high)));                   // Saturday
        Assert.Null(Run("end-of-day", Ctx(At(18), T("low", TaskPriority.Low))));
        Assert.Equal("2 high-priority tasks still open", Run("end-of-day", Ctx(At(18), high, T("x", TaskPriority.High)))!.Message);
    }

    [Fact]
    public void TodaysPlanSummarisesMorningsOnly()
    {
        var now = At(9, 30);
        var tasks = new[] { T("a", remind: now.AddHours(2)), T("b", remind: now.AddMinutes(-10)), T("c", TaskPriority.High) };
        Assert.Equal("Today: 2 reminders, 1 high priority", Run("todays-plan", Ctx(now, tasks))!.Message);
        Assert.Null(Run("todays-plan", Ctx(At(8, 30), tasks)));
        Assert.Null(Run("todays-plan", Ctx(At(12, 0), tasks)));
        Assert.Null(Run("todays-plan", Ctx(At(9, 30, 28), tasks)));                 // weekend
        Assert.Null(Run("todays-plan", Ctx(now, T("plain"))));                      // nothing to say
    }

    [Fact]
    public void StaleHighPriorityNeedsAgeAndNoReminder()
    {
        var now = At(10);
        var old = T("Renew the certificate for production", TaskPriority.High, created: now.AddDays(-4));
        var n = Run("stale-high-priority", Ctx(now, old))!;
        Assert.Contains("4 days old", n.Message);
        Assert.True(n.Message.Length <= NotchMessage.MaxLength + 12); // the title is shortened
        Assert.Null(Run("stale-high-priority", Ctx(now, T("new", TaskPriority.High, created: now.AddDays(-1)))));
        Assert.Null(Run("stale-high-priority", Ctx(now, T("has reminder", TaskPriority.High, remind: now.AddDays(1), created: now.AddDays(-9)))));
        Assert.Null(Run("stale-high-priority", Ctx(now, T("not high", TaskPriority.Medium, created: now.AddDays(-9)))));
        Assert.Contains("2 high-priority tasks", Run("stale-high-priority", Ctx(now, old, T("another", TaskPriority.High, created: now.AddDays(-5))))!.Message);
    }

    [Fact]
    public void EveryBuiltInRuleHasAnIdNameAndDescriptionAndIdsAreUnique()
    {
        Assert.All(NudgeRules.All, r => Assert.False(string.IsNullOrWhiteSpace(r.Id + r.Name + r.Description)));
        Assert.Equal(NudgeRules.All.Count, NudgeRules.All.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void RealRulesThroughTheEngineRespectMuteAndOrder()
    {
        var now = At(18);
        var tasks = new[] { T("a", remind: now.AddHours(-1)), T("b", remind: now.AddHours(-2)), T("c", remind: now.AddHours(-3)), T("hi", TaskPriority.High, created: now.AddDays(-5)) };
        var e = new RuleEngine(NudgeRules.All, Start);
        Assert.Equal("overdue-backlog", e.Evaluate(Ctx(now, tasks), None)!.RuleId);
        var e2 = new RuleEngine(NudgeRules.All, Start);
        Assert.Equal("end-of-day", e2.Evaluate(Ctx(now, tasks), new HashSet<string> { "overdue-backlog" })!.RuleId);
    }
}
