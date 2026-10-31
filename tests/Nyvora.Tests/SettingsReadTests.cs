using Nyvora.Core.Settings;
using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public sealed class SettingsReadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nyvora-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsReadTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ReadsAValidFile()
    {
        var path = Path.Combine(_dir, "settings.json");
        new AppSettings { Tags = new List<TagDef> { new() { Id = "a", Name = "Work" } }, AlwaysOnTop = false }.Save(path);

        Assert.True(AppSettings.TryRead(path, out var s));
        Assert.Equal("Work", s.Tags.Single().Name);
        Assert.False(s.AlwaysOnTop);
    }

    [Fact]
    public void RefusesAMissingOrBrokenFileInsteadOfInventingDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.False(AppSettings.TryRead(path, out _)); // missing

        File.WriteAllText(path, "{ \"Tags\": [ { \"Name\": ");
        Assert.False(AppSettings.TryRead(path, out _)); // half-written or hand-edited badly

        File.WriteAllText(path, "null");
        Assert.False(AppSettings.TryRead(path, out _));
    }
}
