using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nyvora.Core.Updates;

public sealed record UpdateInfo(Version Version, string Tag, Uri PackageUrl, Uri ChecksumUrl, Uri ReleaseUrl);

/// <summary>Picks the newest installable release from the GitHub "list releases" JSON.</summary>
public static class ReleaseFeed
{
    public const string Repository = "aman-dhakar-191/Nyvora";
    public const string ReleasesApiUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=15";
    public const string TrustedDownloadPrefix = "https://github.com/" + Repository + "/releases/download/";
    public const string ReleasesPageUrl = "https://github.com/" + Repository + "/releases";

    private static readonly Regex PackageName = new(@"^Nyvora-.+-win-x64\.zip$", RegexOptions.Compiled);

    /// <summary>
    /// Returns the newest non-draft release that is newer than <paramref name="current"/> and ships both a
    /// win-x64 zip and its .sha256 file from this repository's release downloads; otherwise null.
    /// Pre-releases are included on purpose: the /releases/latest endpoint would hide them.
    /// Throws <see cref="JsonException"/> if the document is not a release list.
    /// </summary>
    public static UpdateInfo? FindUpdate(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Expected a JSON array of releases.");
        }

        UpdateInfo? best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || GetBool(release, "draft"))
            {
                continue;
            }

            var tag = GetString(release, "tag_name");
            if (!AppVersion.TryParse(tag, out var version) || version <= current || (best is not null && version <= best.Version))
            {
                continue;
            }

            if (!TryFindAssets(release, out var package, out var checksum))
            {
                continue;
            }

            var page = GetString(release, "html_url") is { } h && h.StartsWith("https://github.com/" + Repository + "/", StringComparison.Ordinal)
                ? new Uri(h)
                : new Uri(ReleasesPageUrl);
            best = new UpdateInfo(version, tag!, package, checksum, page);
        }

        return best;
    }

    private static bool TryFindAssets(JsonElement release, out Uri package, out Uri checksum)
    {
        package = checksum = null!;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        string? zipName = null;
        var urls = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            var url = GetString(asset, "browser_download_url");
            if (name is null || url is null || !url.StartsWith(TrustedDownloadPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            urls[name] = new Uri(url);
            if (zipName is null && PackageName.IsMatch(name))
            {
                zipName = name;
            }
        }

        // No checksum means the download cannot be verified, so the release is not installable.
        if (zipName is null || !urls.TryGetValue(zipName + ".sha256", out var sum))
        {
            return false;
        }

        package = urls[zipName];
        checksum = sum;
        return true;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
