using System.Text.Json;
using Nyvora.Core.Tasks;

namespace Nyvora.Core.Settings;

public sealed class AppSettings
{
    public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    /// <summary>Set to false in settings.json to stop all automatic network requests.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Keep the pill above other windows. Exclusive-fullscreen apps and games still cover it.</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Start Nyvora when you sign in to Windows (per-user Run key, no admin needed).</summary>
    public bool LaunchAtSignIn { get; set; } = true;

    /// <summary>User-defined task statuses (name and whether they count as done). Validated by <see cref="StatusSet"/>.</summary>
    public List<StatusDef> Statuses { get; set; } = StatusSet.DefaultDefs();

    /// <summary>How often (minutes) Nyvora opens to ask you to update task statuses. 0 turns it off.</summary>
    public int StatusReminderMinutes { get; set; } = 60;

    /// <summary>Expanded pill size in DIPs; 0 means the default. Set by dragging the corner grip.</summary>
    public double ExpandedWidth { get; set; }

    public double ExpandedHeight { get; set; }

    /// <summary>User-defined tags (name and color). Validated by <see cref="TagSet"/>.</summary>
    public List<TagDef> Tags { get; set; } = new();

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
