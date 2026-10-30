using Nyvora.Core.Storage;
using Xunit;

namespace Nyvora.Tests;

public sealed class DataFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nyvora-df-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(_root, "local");
    private string Roaming => Path.Combine(_root, "roaming");

    public DataFoldersTests() => Directory.CreateDirectory(Local);

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void CopiesDataFilesToRoamingAndKeepsTheOriginals()
    {
        File.WriteAllText(Path.Combine(Local, "tasks.json"), "[1]");
        File.WriteAllText(Path.Combine(Local, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(Local, "nyvora.log"), "log");

        var copied = DataFolders.MigrateFromLocal(Roaming, Local);

        Assert.Equal(new[] { "tasks.json", "settings.json" }, copied);
        Assert.Equal("[1]", File.ReadAllText(Path.Combine(Roaming, "tasks.json")));
        Assert.True(File.Exists(Path.Combine(Local, "tasks.json")));
        Assert.False(File.Exists(Path.Combine(Roaming, "nyvora.log"))); // only data moves, not logs
    }

    [Fact]
    public void NeverOverwritesExistingRoamingData()
    {
        File.WriteAllText(Path.Combine(Local, "tasks.json"), "old");
        Directory.CreateDirectory(Roaming);
        File.WriteAllText(Path.Combine(Roaming, "tasks.json"), "new");

        var copied = DataFolders.MigrateFromLocal(Roaming, Local);

        Assert.Empty(copied);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Roaming, "tasks.json")));
    }

    [Fact]
    public void NothingToMigrateIsFineAndCreatesNothing()
    {
        Assert.Empty(DataFolders.MigrateFromLocal(Roaming, Local));
        Assert.False(Directory.Exists(Roaming));
    }

    [Fact]
    public void MigratedTasksSurviveARoundTripUnchanged()
    {
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var tasks = new List<Nyvora.Core.Tasks.TaskItem>
        {
            new() { Id = Guid.NewGuid(), Title = "Keep me", Created = now, RemindAt = now.AddHours(2), StatusId = "doing", TagIds = new List<string> { "t1" } },
            new() { Id = Guid.NewGuid(), Title = "Done one", Created = now, Done = true, StatusId = "done" },
        };
        new Nyvora.Core.Tasks.JsonTaskStore(Path.Combine(Local, "tasks.json")).Save(tasks);

        DataFolders.MigrateFromLocal(Roaming, Local);
        var loaded = new Nyvora.Core.Tasks.JsonTaskStore(Path.Combine(Roaming, "tasks.json")).Load();

        Assert.True(Nyvora.Core.Tasks.TaskSnapshot.Same(tasks, loaded));
        Assert.Equal(File.ReadAllText(Path.Combine(Local, "tasks.json")), File.ReadAllText(Path.Combine(Roaming, "tasks.json")));
    }

    [Fact]
    public void ActiveFolderFallsBackToLocalWhileDataExistsOnlyThere()
    {
        File.WriteAllText(Path.Combine(Local, "tasks.json"), "[]");
        Assert.Equal(Local, DataFolders.Active(Roaming, Local)); // copy has not happened (or failed)

        DataFolders.MigrateFromLocal(Roaming, Local);
        Assert.Equal(Roaming, DataFolders.Active(Roaming, Local));
    }

    [Fact]
    public void ActiveFolderIsRoamingForANewInstall()
    {
        Assert.Equal(Roaming, DataFolders.Active(Roaming, Local));
    }

    [Fact]
    public void RoamingAndLocalAreDifferentFolders()
    {
        Assert.NotEqual(DataFolders.Roaming, DataFolders.Local);
        Assert.EndsWith("Nyvora", DataFolders.Roaming);
    }
}
