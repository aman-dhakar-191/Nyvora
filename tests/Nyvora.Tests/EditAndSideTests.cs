using Nyvora.Core.Animation;
using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class TaskRenameTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RenameTrimsAndNotifies()
    {
        var list = new TaskList();
        var t = list.Add("old", Now)!;
        var n = 0;
        list.Changed += () => n++;

        Assert.True(list.Rename(t.Id, "  new title  "));
        Assert.Equal("new title", t.Title);
        Assert.Equal(1, n);
    }

    [Fact]
    public void RenameRejectsBlankUnchangedAndUnknown()
    {
        var list = new TaskList();
        var t = list.Add("keep", Now)!;
        var n = 0;
        list.Changed += () => n++;

        Assert.False(list.Rename(t.Id, "   "));
        Assert.False(list.Rename(t.Id, ""));
        Assert.False(list.Rename(t.Id, " keep "));
        Assert.False(list.Rename(Guid.NewGuid(), "x"));
        Assert.Equal("keep", t.Title);
        Assert.Equal(0, n);
    }

    [Fact]
    public void RenameCapsLength()
    {
        var list = new TaskList();
        var t = list.Add("a", Now)!;
        list.Rename(t.Id, new string('z', 500));
        Assert.Equal(TaskList.MaxTitleLength, t.Title.Length);
    }

    [Fact]
    public void RenameKeepsStatusTagsAndReminder()
    {
        var tags = new TagSet();
        var tag = tags.TryAdd("work", out _)!;
        var list = new TaskList(null, null, tags);
        var t = list.Add("a", Now, Now.AddHours(1))!;
        list.ToggleTag(t.Id, tag.Id);
        list.SetStatus(t.Id, "doing");

        list.Rename(t.Id, "b");

        Assert.Equal("doing", t.StatusId);
        Assert.Equal(new[] { tag.Id }, t.TagIds);
        Assert.Equal(Now.AddHours(1), t.RemindAt);
    }
}

public class SideWidthTests
{
    [Theory]
    [InlineData(1500, 420, 380)]   // plenty of room: the default
    [InlineData(700, 420, 280)]    // what is left
    [InlineData(600, 420, 260)]    // not enough: never below the minimum
    [InlineData(300, 420, 260)]
    public void FitsTheDisplay(double screen, double expanded, double expected)
    {
        Assert.Equal(expected, ExpandedSizing.SideWidth(new SizeD(expanded, 400), new SizeD(screen, 900)));
    }
}
