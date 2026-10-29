using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class TaskListTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void AddTrimsAndRejectsBlank()
    {
        var l = new TaskList();
        Assert.Null(l.Add("   ", Now));
        Assert.Equal("buy milk", l.Add("  buy milk ", Now)!.Title);
        Assert.Single(l.Items);
    }

    [Fact]
    public void AddCapsTitleLength()
    {
        var item = new TaskList().Add(new string('x', 500), Now)!;
        Assert.Equal(TaskList.MaxTitleLength, item.Title.Length);
    }

    [Fact]
    public void ToggleAndRemove()
    {
        var l = new TaskList();
        var t = l.Add("a", Now)!;
        Assert.True(l.Toggle(t.Id));
        Assert.True(t.Done);
        Assert.Equal(0, l.OpenCount);
        Assert.True(l.Remove(t.Id));
        Assert.False(l.Remove(t.Id));
        Assert.False(l.Toggle(Guid.NewGuid()));
    }

    [Fact]
    public void ReminderFiresOnceThenAgainAfterSnooze()
    {
        var l = new TaskList();
        var t = l.Add("call", Now, Now.AddMinutes(5))!;

        Assert.Empty(l.TakePendingNotifications(Now));
        var due = Now.AddMinutes(5);
        Assert.Single(l.TakePendingNotifications(due));
        Assert.Empty(l.TakePendingNotifications(due.AddMinutes(1)));
        Assert.Equal(1, l.DueCount(due.AddMinutes(1))); // still due until completed or snoozed

        Assert.True(l.Snooze(t.Id, due, Reminders.SnoozeDuration));
        Assert.Equal(0, l.DueCount(due.AddMinutes(1)));
        Assert.Single(l.TakePendingNotifications(due.AddMinutes(10)));
    }

    [Fact]
    public void DoneTasksNeverNotify()
    {
        var l = new TaskList();
        var t = l.Add("x", Now, Now)!;
        l.Toggle(t.Id);
        Assert.Empty(l.TakePendingNotifications(Now.AddHours(1)));
        Assert.Equal(0, l.DueCount(Now.AddHours(1)));
    }

    [Fact]
    public void SnoozeRejectsNonPositive()
    {
        var l = new TaskList();
        var t = l.Add("x", Now, Now)!;
        Assert.False(l.Snooze(t.Id, Now, TimeSpan.Zero));
    }

    [Fact]
    public void OrderedPutsOpenBySoonestReminderAndDoneLast()
    {
        var l = new TaskList();
        var none = l.Add("none", Now)!;
        var late = l.Add("late", Now, Now.AddHours(3))!;
        var soon = l.Add("soon", Now, Now.AddHours(1))!;
        var done = l.Add("done", Now, Now.AddMinutes(1))!;
        l.Toggle(done.Id);

        Assert.Equal(new[] { soon.Id, late.Id, none.Id, done.Id }, l.Ordered().Select(i => i.Id));
    }

    [Fact]
    public void OrderedPutsInProgressAboveToDo()
    {
        var l = new TaskList();
        var todo = l.Add("todo", Now, Now.AddHours(1))!;
        var doing = l.Add("doing", Now)!;
        l.CycleStatus(doing.Id); // To do -> In progress

        Assert.Equal(new[] { doing.Id, todo.Id }, l.Ordered().Select(i => i.Id));
    }

    [Fact]
    public void LaterTodayCountsOnlyUpcomingRemindersOnTheSameDay()
    {
        var l = new TaskList();
        var noon = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        l.Add("past", noon, noon.AddHours(-1));
        l.Add("later", noon, noon.AddHours(3));
        l.Add("tomorrow", noon, noon.AddHours(20));
        l.Add("none", noon);

        Assert.Equal(1, l.LaterTodayCount(noon));
    }

    [Fact]
    public void ChangedIsRaisedForMutations()
    {
        var l = new TaskList();
        var n = 0;
        l.Changed += () => n++;
        var t = l.Add("a", Now, Now)!;
        l.Toggle(t.Id);
        l.Toggle(t.Id);
        l.TakePendingNotifications(Now);
        l.Remove(t.Id);
        Assert.Equal(5, n);
    }
}

public class ReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 16, 45, 0, TimeSpan.FromHours(2));

    [Fact]
    public void PresetsResolve()
    {
        Assert.Null(Reminders.Resolve(ReminderPreset.None, Now));
        Assert.Equal(Now.AddMinutes(10), Reminders.Resolve(ReminderPreset.InTenMinutes, Now));
        Assert.Equal(Now.AddHours(1), Reminders.Resolve(ReminderPreset.InOneHour, Now));
        Assert.Equal(
            new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.FromHours(2)),
            Reminders.Resolve(ReminderPreset.TomorrowMorning, Now));
    }

    [Fact]
    public void FormatDueUsesLocalClockOfNow()
    {
        Assert.Equal("17:00", Reminders.FormatDue(Now.AddMinutes(15), Now));
        Assert.Equal("Tomorrow 09:00", Reminders.FormatDue(new DateTimeOffset(2026, 3, 11, 9, 0, 0, Now.Offset), Now));
        // Same instant expressed in UTC still renders in the offset of "now".
        Assert.Equal("17:00", Reminders.FormatDue(Now.AddMinutes(15).ToUniversalTime(), Now));
    }
}

public class JsonTaskStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nyvora-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void MissingFileLoadsEmpty()
    {
        Assert.Empty(new JsonTaskStore(Path.Combine(_dir, "tasks.json")).Load());
    }

    [Fact]
    public void RoundTrips()
    {
        var store = new JsonTaskStore(Path.Combine(_dir, "sub", "tasks.json"));
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var list = new TaskList();
        var t = list.Add("write tests", now, now.AddHours(1))!;
        t.Notified = true;

        store.Save(list.Items);
        var loaded = store.Load().Single();

        Assert.Equal(t.Id, loaded.Id);
        Assert.Equal("write tests", loaded.Title);
        Assert.Equal(now.AddHours(1), loaded.RemindAt);
        Assert.True(loaded.Notified);
        Assert.False(File.Exists(Path.Combine(_dir, "sub", "tasks.json.tmp")));
    }

    [Fact]
    public void CorruptFileIsSetAsideNotLost()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "tasks.json");
        File.WriteAllText(path, "{ not json");
        var store = new JsonTaskStore(path);

        Assert.Empty(store.Load());
        Assert.True(store.RecoveredFromCorruption);
        Assert.True(File.Exists(path + ".corrupt"));
    }
}
