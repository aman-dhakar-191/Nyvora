using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class UndoTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void RestorePutsARemovedTaskBackWhereItWas()
    {
        var l = new TaskList();
        var a = l.Add("a", Now)!;
        var b = l.Add("b", Now)!;
        l.Add("c", Now);

        var at = l.IndexOf(b.Id);
        Assert.Equal(1, at);
        l.Remove(b.Id);
        Assert.Equal(-1, l.IndexOf(b.Id));

        Assert.True(l.Restore(b, at));
        Assert.Equal(new[] { "a", "b", "c" }, l.Items.Select(i => i.Title));
        Assert.Equal(0, l.IndexOf(a.Id));
    }

    [Fact]
    public void RestoreRefusesATaskThatIsAlreadyThere()
    {
        var l = new TaskList();
        var a = l.Add("a", Now)!;
        Assert.False(l.Restore(a, 0));
        Assert.Single(l.Items);
    }

    [Fact]
    public void RestoreClampsAnOutOfRangeIndex()
    {
        var l = new TaskList();
        var a = l.Add("a", Now)!;
        l.Remove(a.Id);
        Assert.True(l.Restore(a, 99));
        Assert.Single(l.Items);
    }
}
