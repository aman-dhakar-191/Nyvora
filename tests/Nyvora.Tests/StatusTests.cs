using Nyvora.Core.Settings;
using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class StatusSetTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DefaultsAreTodoInProgressDone()
    {
        var s = new StatusSet();
        Assert.Equal(new[] { "To do", "In progress", "Done" }, s.Items.Select(i => i.Name));
        Assert.Equal("To do", s.FirstOpen.Name);
        Assert.Equal("Done", s.FirstDone.Name);
    }

    [Fact]
    public void UnusableStoredDataFallsBackToDefaults()
    {
        Assert.Equal(3, new StatusSet(new List<StatusDef>()).Items.Count);
        Assert.Equal(3, new StatusSet(null).Items.Count);
        // all open, no done status
        Assert.Equal(3, new StatusSet(new[] { new StatusDef { Id = "a", Name = "A" } }).Items.Count);
    }

    [Fact]
    public void StoredDataIsCleaned()
    {
        var s = new StatusSet(new[]
        {
            new StatusDef { Id = "a", Name = "  Backlog ", IsDone = false },
            new StatusDef { Id = "a", Name = "Dup id", IsDone = false },
            new StatusDef { Id = "b", Name = "backlog", IsDone = false },
            new StatusDef { Id = "c", Name = "   ", IsDone = false },
            new StatusDef { Id = "", Name = new string('x', 80), IsDone = true },
        });
        Assert.Equal(2, s.Items.Count);
        Assert.Equal("Backlog", s.Items[0].Name);
        Assert.Equal(StatusSet.MaxNameLength, s.Items[1].Name.Length);
        Assert.False(string.IsNullOrEmpty(s.Items[1].Id));
    }

    [Fact]
    public void RenameValidatesAndNotifies()
    {
        var s = new StatusSet();
        var n = 0;
        s.Changed += () => n++;

        Assert.Null(s.Rename("doing", "Working"));
        Assert.Equal("Working", s.Find("doing")!.Name);
        Assert.Equal(1, n);

        Assert.NotNull(s.Rename("doing", "  "));
        Assert.NotNull(s.Rename("doing", "to DO"));
        Assert.NotNull(s.Rename("doing", new string('x', 25)));
        Assert.NotNull(s.Rename("nope", "X"));
        Assert.Null(s.Rename("doing", "Working")); // unchanged: ok, no event
        Assert.Null(s.Rename("doing", "WORKING")); // case-only change of its own name is allowed
        Assert.Equal(2, n);
    }

    [Fact]
    public void AddEnforcesNamesAndLimit()
    {
        var s = new StatusSet();
        Assert.Null(s.Add("Blocked", false));
        Assert.NotNull(s.Add("blocked", false));
        while (s.Items.Count < StatusSet.MaxStatuses)
        {
            Assert.Null(s.Add("S" + s.Items.Count, false));
        }

        Assert.NotNull(s.Add("One more", false));
    }

    [Fact]
    public void CannotRemoveOrFlipTheLastOpenOrDoneStatus()
    {
        var s = new StatusSet(new[]
        {
            new StatusDef { Id = "o", Name = "Open", IsDone = false },
            new StatusDef { Id = "d", Name = "Done", IsDone = true },
        });
        Assert.NotNull(s.Remove("o"));
        Assert.NotNull(s.Remove("d"));
        Assert.NotNull(s.SetDone("o", true));
        Assert.NotNull(s.SetDone("d", false));
        Assert.Equal(2, s.Items.Count);

        Assert.Null(s.Add("Review", false));
        var review = s.Items.Last().Id;
        Assert.Null(s.SetDone(review, false)); // already open: no-op
        Assert.Null(s.Remove("o"));            // another open status remains
        Assert.NotNull(s.Remove(review));      // now the last open one
    }

    [Fact]
    public void MoveReordersAndClampsAtEnds()
    {
        var s = new StatusSet();
        s.Move("done", -1);
        Assert.Equal(new[] { "todo", "done", "doing" }, s.Items.Select(i => i.Id));
        var before = s.Items.Select(i => i.Id).ToList();
        s.Move("todo", -1);
        Assert.Equal(before, s.Items.Select(i => i.Id));
    }

    [Fact]
    public void NextWrapsAround()
    {
        var s = new StatusSet();
        Assert.Equal("doing", s.Next("todo").Id);
        Assert.Equal("todo", s.Next("done").Id);
        Assert.Equal("todo", s.Next("missing").Id); // unknown id restarts from the first status
    }

    [Fact]
    public void ExportIsACopy()
    {
        var s = new StatusSet();
        var copy = s.Export();
        copy[0].Name = "Changed";
        Assert.Equal("To do", s.Items[0].Name);
    }

    [Fact]
    public void SettingsKeepStatuses()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nyvora-st-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var set = new StatusSet();
            set.Rename("doing", "Working");
            set.Add("Blocked", false);
            new AppSettings { Statuses = set.Export() }.Save(path);

            var back = new StatusSet(AppSettings.Load(path).Statuses);
            Assert.Equal(new[] { "To do", "Working", "Done", "Blocked" }, back.Items.Select(i => i.Name));
            Assert.True(new AppSettings().Statuses.Count == 3);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TasksFromBeforeStatusesAreMigrated()
    {
        var open = new TaskItem { Id = Guid.NewGuid(), Title = "a", Created = Now };
        var done = new TaskItem { Id = Guid.NewGuid(), Title = "b", Created = Now, Done = true };
        var list = new TaskList(new[] { open, done });

        Assert.Equal("todo", open.StatusId);
        Assert.Equal("done", done.StatusId);
        Assert.True(done.Done);
    }

    [Fact]
    public void NewTasksStartInFirstOpenStatus()
    {
        var list = new TaskList();
        Assert.Equal("todo", list.Add("x", Now)!.StatusId);
    }

    [Fact]
    public void SetStatusKeepsDoneInSync()
    {
        var list = new TaskList();
        var t = list.Add("x", Now, Now)!;
        Assert.True(list.SetStatus(t.Id, "doing"));
        Assert.False(t.Done);
        Assert.True(list.SetStatus(t.Id, "done"));
        Assert.True(t.Done);
        Assert.Empty(list.TakePendingNotifications(Now.AddHours(1))); // done tasks never remind
        Assert.False(list.SetStatus(t.Id, "nope"));
        Assert.False(list.SetStatus(Guid.NewGuid(), "todo"));
    }

    [Fact]
    public void CycleVisitsEveryStatusInOrder()
    {
        var list = new TaskList();
        var t = list.Add("x", Now)!;
        var seen = new List<string?>();
        for (var i = 0; i < 4; i++)
        {
            list.CycleStatus(t.Id);
            seen.Add(t.StatusId);
        }

        Assert.Equal(new[] { "doing", "done", "todo", "doing" }, seen);
    }

    [Fact]
    public void ToggleFinishesAndReopens()
    {
        var list = new TaskList();
        var t = list.Add("x", Now)!;
        list.SetStatus(t.Id, "doing");
        list.Toggle(t.Id);
        Assert.Equal("done", t.StatusId);
        list.Toggle(t.Id);
        Assert.Equal("todo", t.StatusId);
    }

    [Fact]
    public void RemovingAStatusMovesItsTasks()
    {
        var statuses = new StatusSet();
        var list = new TaskList(null, statuses);
        var a = list.Add("a", Now)!;
        var b = list.Add("b", Now)!;
        list.SetStatus(a.Id, "doing");
        list.SetStatus(b.Id, "done");

        Assert.Null(statuses.Remove("doing"));
        list.ApplyStatuses();
        Assert.Equal("todo", a.StatusId);   // open task falls back to the first open status
        Assert.Equal("done", b.StatusId);

        Assert.NotNull(statuses.Remove("done")); // the only done status cannot be removed
        Assert.Equal(2, statuses.Items.Count);
    }

    [Fact]
    public void RemovingADoneStatusMovesFinishedTasksToAnotherDoneStatus()
    {
        var statuses = new StatusSet();
        statuses.Add("Shipped", true);
        var list = new TaskList(null, statuses);
        var t = list.Add("a", Now)!;
        list.SetStatus(t.Id, "done");

        Assert.Null(statuses.Remove("done"));
        list.ApplyStatuses();

        Assert.True(t.Done);
        Assert.Equal("Shipped", list.StatusOf(t).Name);
    }

    [Fact]
    public void FlippingDoneFlagReclassifiesTasks()
    {
        var statuses = new StatusSet();
        var list = new TaskList(null, statuses);
        var t = list.Add("a", Now, Now)!;
        list.SetStatus(t.Id, "doing");

        statuses.SetDone("doing", true);
        list.ApplyStatuses();

        Assert.True(t.Done);
        Assert.Equal(0, list.OpenCount);
        Assert.Equal(0, list.DueCount(Now.AddHours(1)));
    }
}

public class RecurringNudgeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void DoesNotFireBeforeTheIntervalAndFiresOncePerInterval()
    {
        var n = new RecurringNudge(Hour, T0);
        Assert.False(n.TryFire(T0, 3));
        Assert.False(n.TryFire(T0.AddMinutes(59), 3));
        Assert.True(n.TryFire(T0.AddMinutes(60), 3));
        Assert.False(n.TryFire(T0.AddMinutes(61), 3));
        Assert.True(n.TryFire(T0.AddMinutes(120), 3));
    }

    [Fact]
    public void SkipsWhenNothingIsOpenButStillReschedules()
    {
        var n = new RecurringNudge(Hour, T0);
        Assert.False(n.TryFire(T0.AddHours(1), 0));
        Assert.False(n.TryFire(T0.AddHours(1).AddMinutes(30), 5)); // next is T0+2h, not due yet
        Assert.True(n.TryFire(T0.AddHours(2), 5));
    }

    [Fact]
    public void LongGapGivesOneNudgeNotABurst()
    {
        var n = new RecurringNudge(Hour, T0);
        var wake = T0.AddHours(9); // laptop slept all night
        Assert.True(n.TryFire(wake, 2));
        Assert.False(n.TryFire(wake.AddSeconds(20), 2));
        Assert.False(n.TryFire(wake.AddMinutes(59), 2));
    }

    [Fact]
    public void ZeroIntervalIsOff()
    {
        var n = new RecurringNudge(TimeSpan.Zero, T0);
        Assert.False(n.Enabled);
        Assert.False(n.TryFire(T0.AddDays(3), 4));
    }

    [Fact]
    public void ChangingTheIntervalRestartsTheCountdown()
    {
        var n = new RecurringNudge(Hour, T0);
        n.SetInterval(TimeSpan.FromMinutes(30), T0.AddMinutes(50));
        Assert.False(n.TryFire(T0.AddMinutes(60), 1));
        Assert.True(n.TryFire(T0.AddMinutes(80), 1));
        n.SetInterval(TimeSpan.Zero, T0.AddMinutes(81));
        Assert.False(n.TryFire(T0.AddDays(1), 1));
    }
}
