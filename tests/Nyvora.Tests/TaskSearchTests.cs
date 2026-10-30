using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public sealed class TaskSearchTests
{
    private static readonly string[] NoTags = Array.Empty<string>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQueryMatchesEverything(string? q)
    {
        Assert.False(TaskSearch.IsActive(q));
        Assert.True(TaskSearch.Matches(q, "anything", "To do", NoTags));
    }

    [Fact]
    public void MatchesTitleCaseInsensitivelyAsASubstring()
    {
        Assert.True(TaskSearch.Matches("MILK", "Buy milk today", "To do", NoTags));
        Assert.False(TaskSearch.Matches("bread", "Buy milk today", "To do", NoTags));
    }

    [Fact]
    public void EveryWordMustMatchSomewhere()
    {
        Assert.True(TaskSearch.Matches("buy today", "Buy milk today", "To do", NoTags));
        Assert.False(TaskSearch.Matches("buy tomorrow", "Buy milk today", "To do", NoTags));
    }

    [Fact]
    public void MatchesStatusAndTagNames()
    {
        Assert.True(TaskSearch.Matches("progress", "x", "In progress", NoTags));
        Assert.True(TaskSearch.Matches("work", "x", "To do", new[] { "Work" }));
        Assert.True(TaskSearch.Matches("progress work", "x", "In progress", new[] { "Work" }));
    }

    [Fact]
    public void HashWordsMatchTagNamesOnly()
    {
        Assert.True(TaskSearch.Matches("#work", "x", "To do", new[] { "Work" }));
        Assert.False(TaskSearch.Matches("#work", "Finish work report", "To do", NoTags));
        Assert.True(TaskSearch.Matches("#wor report", "Report", "To do", new[] { "Work" }));
    }

    [Fact]
    public void ALoneHashDoesNotHideEverything()
    {
        Assert.True(TaskSearch.Matches("#", "x", "To do", NoTags));
        Assert.True(TaskSearch.Matches("milk #", "milk", "To do", NoTags));
    }

    [Fact]
    public void TaskListMatchesUsesStatusAndTagNames()
    {
        var l = new TaskList();
        var t = l.Add("Write the report", new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))!;
        l.CycleStatus(t.Id); // To do -> In progress

        Assert.True(l.Matches(t, "report progress"));
        Assert.False(l.Matches(t, "report done"));
    }
}

public sealed class PriorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewTasksHaveNoPriorityAndOldFilesReadAsNone()
    {
        var l = new TaskList();
        Assert.Equal(TaskPriority.None, l.Add("x", Now)!.Priority);

        var old = System.Text.Json.JsonSerializer.Deserialize<List<TaskItem>>(
            """[{"Id":"11111111-1111-1111-1111-111111111111","Title":"old","Done":false,"Created":"2026-01-01T00:00:00+00:00"}]""")!;
        Assert.Equal(TaskPriority.None, old[0].Priority);
    }

    [Fact]
    public void CycleGoesNoneHighMediumLowNone()
    {
        var l = new TaskList();
        var t = l.Add("x", Now)!;
        var seen = new List<TaskPriority>();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(l.CyclePriority(t.Id));
            seen.Add(t.Priority);
        }

        Assert.Equal(new[] { TaskPriority.High, TaskPriority.Medium, TaskPriority.Low, TaskPriority.None, TaskPriority.High }, seen);
    }

    [Fact]
    public void SetPriorityRaisesChangedOnlyWhenSomethingChanged()
    {
        var l = new TaskList();
        var t = l.Add("x", Now)!;
        var raised = 0;
        l.Changed += () => raised++;

        Assert.True(l.SetPriority(t.Id, TaskPriority.Medium));
        Assert.False(l.SetPriority(t.Id, TaskPriority.Medium));
        Assert.False(l.SetPriority(Guid.NewGuid(), TaskPriority.High));
        Assert.False(l.SetPriority(t.Id, (TaskPriority)42));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void OrderedPutsMoreImportantFirstWithinTheSameStatus()
    {
        var l = new TaskList();
        var none = l.Add("none", Now)!;
        var low = l.Add("low", Now)!;
        var high = l.Add("high", Now)!;
        var medium = l.Add("medium", Now)!;
        l.SetPriority(low.Id, TaskPriority.Low);
        l.SetPriority(high.Id, TaskPriority.High);
        l.SetPriority(medium.Id, TaskPriority.Medium);

        Assert.Equal(new[] { high.Id, medium.Id, low.Id, none.Id }, l.Ordered().Select(i => i.Id));
    }

    [Fact]
    public void StatusStillOutranksPriority()
    {
        var l = new TaskList();
        var urgentTodo = l.Add("urgent", Now)!;
        var doing = l.Add("doing", Now)!;
        l.SetPriority(urgentTodo.Id, TaskPriority.High);
        l.CycleStatus(doing.Id); // To do -> In progress

        Assert.Equal(new[] { doing.Id, urgentTodo.Id }, l.Ordered().Select(i => i.Id));
    }

    [Fact]
    public void OutOfRangeStoredValueIsRepaired()
    {
        var items = new List<TaskItem> { new() { Id = Guid.NewGuid(), Title = "x", Created = Now, Priority = (TaskPriority)99 } };
        var l = new TaskList(items);
        Assert.Equal(TaskPriority.None, l.Items[0].Priority);
    }

    [Theory]
    [InlineData("high", TaskPriority.High, true)]
    [InlineData("HIGH", TaskPriority.High, true)]
    [InlineData("med", TaskPriority.Medium, true)]
    [InlineData("Low", TaskPriority.Low, true)]
    [InlineData("none", TaskPriority.None, true)]
    [InlineData("urgent", TaskPriority.None, false)]
    public void ParsePriorityNames(string text, TaskPriority expected, bool ok)
    {
        Assert.Equal(ok, TaskPriorities.TryParse(text, out var p));
        Assert.Equal(expected, p);
    }

    [Fact]
    public void BangWordsSearchThePriorityOnly()
    {
        Assert.True(TaskSearch.Matches("!high", "x", "To do", Array.Empty<string>(), "High"));
        Assert.True(TaskSearch.Matches("!hi report", "Report", "To do", Array.Empty<string>(), "High"));
        Assert.False(TaskSearch.Matches("!high", "high tide", "To do", Array.Empty<string>(), "Low"));
        Assert.True(TaskSearch.Matches("!", "x", "To do", Array.Empty<string>(), "None"));
    }
}
