using System.Text.Json;

namespace Nyvora.Core.Settings;

public sealed class AppSettings
{
    public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    /// <summary>Set to false in settings.json to stop all automatic network requests.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheck { get; set; }

    public bool IsUpdateCheckDue(DateTimeOffset now) =>
        AutoCheckUpdates && (LastUpdateCheck is null || now - LastUpdateCheck >= UpdateCheckInterval);

    /// <summary>Missing or unreadable files yield defaults; settings are never worth failing startup for.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }
}
