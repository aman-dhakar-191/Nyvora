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
    public void RoamingAndLocalAreDifferentFolders()
    {
        Assert.NotEqual(DataFolders.Roaming, DataFolders.Local);
        Assert.EndsWith("Nyvora", DataFolders.Roaming);
    }
}
