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
