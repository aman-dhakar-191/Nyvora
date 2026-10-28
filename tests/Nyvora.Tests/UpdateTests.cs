using System.Text;
using System.Text.Json;
using Nyvora.Core.Settings;
using Nyvora.Core.Updates;
using Xunit;

namespace Nyvora.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("V0.10.0-beta.1", 0, 10, 0)]
    [InlineData("2.0.1+abc123", 2, 0, 1)]
    public void Parses(string text, int major, int minor, int build)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new Version(major, minor, build, 0), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("1")]
    public void RejectsGarbage(string? text) => Assert.False(AppVersion.TryParse(text, out _));

    [Fact]
    public void TenIsGreaterThanNineNotLexical()
    {
        AppVersion.TryParse("0.10.0", out var a);
        AppVersion.TryParse("0.9.0", out var b);
        Assert.True(a > b);
    }
}

public class ReleaseFeedTests
{
    private static readonly Version Current = new(0, 1, 0, 0);
    private const string P = ReleaseFeed.TrustedDownloadPrefix;

    private static string Release(string tag, bool draft = false, bool prerelease = true, string? prefix = null, bool checksum = true)
    {
        prefix ??= P;
        var zip = $"Nyvora-{tag}-win-x64.zip";
        var assets = new StringBuilder($"{{\"name\":\"{zip}\",\"browser_download_url\":\"{prefix}{tag}/{zip}\"}}");
        if (checksum)
        {
            assets.Append($",{{\"name\":\"{zip}.sha256\",\"browser_download_url\":\"{prefix}{tag}/{zip}.sha256\"}}");
        }

        return $"{{\"tag_name\":\"{tag}\",\"draft\":{draft.ToString().ToLowerInvariant()},\"prerelease\":{prerelease.ToString().ToLowerInvariant()},"
             + $"\"html_url\":\"https://github.com/{ReleaseFeed.Repository}/releases/tag/{tag}\",\"assets\":[{assets}]}}";
    }

    private static string Feed(params string[] releases) => "[" + string.Join(",", releases) + "]";

    [Fact]
    public void PicksNewestAndIncludesPrereleases()
    {
        var info = ReleaseFeed.FindUpdate(Feed(Release("v0.2.0"), Release("v0.10.0"), Release("v0.3.0")), Current)!;
        Assert.Equal("v0.10.0", info.Tag);
        Assert.EndsWith("Nyvora-v0.10.0-win-x64.zip", info.PackageUrl.ToString());
        Assert.EndsWith(".zip.sha256", info.ChecksumUrl.ToString());
    }

    [Fact]
    public void NullWhenNotNewer()
    {
        Assert.Null(ReleaseFeed.FindUpdate(Feed(Release("v0.1.0"), Release("v0.0.9")), Current));
        Assert.Null(ReleaseFeed.FindUpdate("[]", Current));
    }

    [Fact]
    public void IgnoresDraftsAndUnparseableTags()
    {
        Assert.Null(ReleaseFeed.FindUpdate(Feed(Release("v9.0.0", draft: true), Release("nightly")), Current));
    }

    [Fact]
    public void RefusesReleasesWithoutChecksum()
    {
        Assert.Null(ReleaseFeed.FindUpdate(Feed(Release("v0.2.0", checksum: false)), Current));
    }

    [Fact]
    public void RefusesDownloadsFromOtherLocations()
    {
        Assert.Null(ReleaseFeed.FindUpdate(Feed(Release("v0.2.0", prefix: "https://evil.example/releases/download/")), Current));
        Assert.Null(ReleaseFeed.FindUpdate(Feed(Release("v0.2.0", prefix: "http://github.com/" + ReleaseFeed.Repository + "/releases/download/")), Current));
    }

    [Fact]
    public void FallsBackWhenOlderReleaseIsBrokenButNewerIsFine()
    {
        var info = ReleaseFeed.FindUpdate(Feed(Release("v0.5.0", checksum: false), Release("v0.4.0")), Current)!;
        Assert.Equal("v0.4.0", info.Tag);
    }

    [Fact]
    public void NonArrayJsonThrows()
    {
        Assert.Throws<JsonException>(() => ReleaseFeed.FindUpdate("{\"message\":\"Not Found\"}", Current));
        Assert.ThrowsAny<JsonException>(() => ReleaseFeed.FindUpdate("<html>", Current));
    }
}

public class ChecksumTests
{
    private const string Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // SHA-256 of empty input

    [Fact]
    public void ParsesFileStyleAndBare()
    {
        Assert.Equal(Hash, Checksum.ParseExpected($"{Hash.ToUpperInvariant()}  Nyvora.zip\n"));
        Assert.Equal(Hash, Checksum.ParseExpected(Hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("deadbeef  file.zip")]
    [InlineData("zz3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void RejectsMalformed(string text) => Assert.Null(Checksum.ParseExpected(text));

    [Fact]
    public async Task HashesStream()
    {
        Assert.Equal(Hash, await Checksum.Sha256HexAsync(new MemoryStream()));
        Assert.NotEqual(Hash, await Checksum.Sha256HexAsync(new MemoryStream(new byte[] { 1 })));
    }
}

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nyvora-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void DefaultsWhenMissingOrCorrupt()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.True(AppSettings.Load(path).AutoCheckUpdates);
        Assert.True(AppSettings.Load(path).AlwaysOnTop);

        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ nope");
        Assert.True(AppSettings.Load(path).AutoCheckUpdates);
    }

    [Fact]
    public void RoundTrips()
    {
        var path = Path.Combine(_dir, "settings.json");
        var when = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        new AppSettings { AutoCheckUpdates = false, AlwaysOnTop = false, LastUpdateCheck = when }.Save(path);

        var loaded = AppSettings.Load(path);
        Assert.False(loaded.AutoCheckUpdates);
        Assert.False(loaded.AlwaysOnTop);
        Assert.Equal(when, loaded.LastUpdateCheck);
    }

    [Fact]
    public void CheckIsDueOncePerDayAndNeverWhenDisabled()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.True(new AppSettings().IsUpdateCheckDue(now));
        Assert.False(new AppSettings { LastUpdateCheck = now.AddHours(-23) }.IsUpdateCheckDue(now));
        Assert.True(new AppSettings { LastUpdateCheck = now.AddHours(-25) }.IsUpdateCheckDue(now));
        Assert.False(new AppSettings { AutoCheckUpdates = false }.IsUpdateCheckDue(now));
    }
}
