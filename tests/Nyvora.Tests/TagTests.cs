using System.Text.Json;
using Nyvora.Core.Settings;
using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class TagSetTests
{
    [Fact]
    public void AddTrimsStripsHashAndAssignsRotatingColors()
    {
        var s = new TagSet();
        Assert.Equal("work", s.TryAdd("  #work ", out var e1)!.Name);
        Assert.Null(e1);
        for (var i = 0; i < TagSet.PaletteSize; i++)
        {
            s.TryAdd("t" + i, out _);
        }

        Assert.Equal(0, s.Items[0].Color);
        Assert.Equal(1, s.Items[1].Color);
        Assert.All(s.Items, t => Assert.InRange(t.Color, 0, TagSet.PaletteSize - 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("## ")]
    public void RejectsBlankNames(string name)
    {
        var s = new TagSet();
        Assert.Null(s.TryAdd(name, out var error));
        Assert.NotNull(error);
        Assert.Empty(s.Items);
    }

    [Fact]
    public void RejectsDuplicatesIgnoringCaseAndHash()
    {
        var s = new TagSet();
        s.TryAdd("Work", out _);
        Assert.Null(s.TryAdd("#WORK", out var error));
        Assert.NotNull(error);
        Assert.Single(s.Items);
    }

    [Fact]
    public void RejectsTooLongNamesAndTooManyTags()
    {
        var s = new TagSet();
        Assert.Null(s.TryAdd(new string('x', TagSet.MaxNameLength + 1), out var e));
        Assert.NotNull(e);
        for (var i = 0; i < TagSet.MaxTags; i++)
        {
            Assert.NotNull(s.TryAdd("t" + i, out _));
        }

        Assert.Null(s.TryAdd("one more", out var full));
        Assert.NotNull(full);
    }

    [Fact]
    public void RenameSetColorRemove()
    {
        var s = new TagSet();
        var a = s.TryAdd("a", out _)!;
        var b = s.TryAdd("b", out _)!;
        var n = 0;
        s.Changed += () => n++;

        Assert.Null(s.Rename(a.Id, "alpha"));
        Assert.NotNull(s.Rename(a.Id, "B"));        // taken by b
        Assert.Null(s.Rename(a.Id, "alpha"));        // unchanged: no event
        Assert.Null(s.SetColor(b.Id, 9));            // wraps into the palette
        Assert.Equal(9 % TagSet.PaletteSize, b.Color);
        Assert.Null(s.SetColor(b.Id, b.Color));      // unchanged: no event
        Assert.Null(s.Remove(a.Id));
        Assert.NotNull(s.Remove(a.Id));
        Assert.Equal(3, n);
        Assert.Single(s.Items);
    }

    [Fact]
    public void StoredDataIsCleaned()
    {
        var s = new TagSet(new[]
        {
            new TagDef { Id = "1", Name = " #Home ", Color = -1 },
            new TagDef { Id = "1", Name = "dup id", Color = 0 },
            new TagDef { Id = "2", Name = "home", Color = 0 },
            new TagDef { Id = "", Name = "Errands", Color = 99 },
            new TagDef { Id = "3", Name = " ", Color = 0 },
        });
        Assert.Equal(new[] { "Home", "Errands" }, s.Items.Select(t => t.Name));
        Assert.All(s.Items, t => Assert.InRange(t.Color, 0, TagSet.PaletteSize - 1));
        Assert.False(string.IsNullOrEmpty(s.Items[1].Id));
    }

    [Fact]
    public void ExportIsACopyAndSettingsKeepTags()
    {
        var s = new TagSet();
        s.TryAdd("work", out _);
        s.Export()[0].Name = "changed";
        Assert.Equal("work", s.Items[0].Name);

        var dir = Path.Combine(Path.GetTempPath(), "nyvora-tags-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "settings.json");
            new AppSettings { Tags = s.Export() }.Save(path);
            Assert.Equal("work", new TagSet(AppSettings.Load(path).Tags).Items.Single().Name);
            Assert.Empty(new AppSettings().Tags);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

public class TaskTagTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToggleAddsAndRemovesAndNotifies()
    {
        var tags = new TagSet();
        var work = tags.TryAdd("work", out _)!;
        var list = new TaskList(null, null, tags);
        var t = list.Add("x", Now)!;
        var n = 0;
        list.Changed += () => n++;

        Assert.True(list.ToggleTag(t.Id, work.Id));
        Assert.Equal(new[] { work.Id }, t.TagIds);
        Assert.True(list.ToggleTag(t.Id, work.Id));
        Assert.Empty(t.TagIds);
        Assert.Equal(2, n);
        Assert.False(list.ToggleTag(t.Id, "nope"));
        Assert.False(list.ToggleTag(Guid.NewGuid(), work.Id));
    }

    [Fact]
    public void TagsOfFollowsTheUsersTagOrder()
    {
        var tags = new TagSet();
        var a = tags.TryAdd("a", out _)!;
        var b = tags.TryAdd("b", out _)!;
        var list = new TaskList(null, null, tags);
        var t = list.Add("x", Now)!;
        list.ToggleTag(t.Id, b.Id);
        list.ToggleTag(t.Id, a.Id);
        Assert.Equal(new[] { "a", "b" }, list.TagsOf(t).Select(x => x.Name));
    }

    [Fact]
    public void DeletingATagRemovesItFromTasks()
    {
        var tags = new TagSet();
        var a = tags.TryAdd("a", out _)!;
        var b = tags.TryAdd("b", out _)!;
        var list = new TaskList(null, null, tags);
        var t = list.Add("x", Now)!;
        list.ToggleTag(t.Id, a.Id);
        list.ToggleTag(t.Id, b.Id);

        tags.Remove(a.Id);
        list.ApplyTags();

        Assert.Equal(new[] { b.Id }, t.TagIds);
    }

    [Fact]
    public void OldFilesWithoutTagsLoadCleanly()
    {
        var json = "[{\"Id\":\"" + Guid.NewGuid() + "\",\"Title\":\"old\",\"Done\":false,\"Created\":\"2026-03-10T12:00:00+00:00\"}]";
        var items = JsonSerializer.Deserialize<List<TaskItem>>(json)!;
        var list = new TaskList(items);
        Assert.Empty(items[0].TagIds);
        Assert.Empty(list.TagsOf(items[0]));
    }

    [Fact]
    public void StaleAndDuplicateTagIdsAreCleanedOnLoad()
    {
        var tags = new TagSet();
        var a = tags.TryAdd("a", out _)!;
        var item = new TaskItem { Id = Guid.NewGuid(), Title = "x", Created = Now, TagIds = new List<string> { a.Id, a.Id, "gone" } };
        var list = new TaskList(new[] { item }, null, tags);
        Assert.Equal(new[] { a.Id }, item.TagIds);
        Assert.Single(list.Items);
    }
}
