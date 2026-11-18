using System.Text.Json;

namespace Nyvora.Core.Timesheet;

/// <summary>What you did on one day, as typed into the daily check-in. Kept on this PC (checkins.json).</summary>
public sealed class CheckInEntry
{
    public DateOnly Date { get; set; }

    /// <summary>Copied from Settings when the entry is saved, so the file shows what was filled.</summary>
    public string Project { get; set; } = "";

    public string Task { get; set; } = "";

    public double Hours { get; set; }

    public string Comment { get; set; } = "";

    /// <summary>True once the Keka script filled this day. Nothing is ever submitted for you.</summary>
    public bool Filled { get; set; }

    /// <summary>Null when valid, otherwise what to fix.</summary>
    public string? Validate()
    {
        if (!double.IsFinite(Hours) || Hours <= 0 || Hours > 24)
        {
            return "Hours must be more than 0 and at most 24.";
        }

        if (string.IsNullOrWhiteSpace(Comment))
        {
            return "Write what you did. Keka needs a comment.";
        }

        if (Project.Length > 200 || Task.Length > 200 || Comment.Length > 2000)
        {
            return "That text is too long.";
        }

        return null;
    }
}

/// <summary>checkins.json: one entry per day (saving a day again replaces it). Written atomically.</summary>
public sealed class CheckInStore
{
    public const string FileName = "checkins.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public CheckInStore(string path) => _path = path;

    public IReadOnlyList<CheckInEntry> Load()
    {
        try
        {
            return File.Exists(_path)
                ? (JsonSerializer.Deserialize<List<CheckInEntry>>(File.ReadAllText(_path)) ?? new()).OrderBy(e => e.Date).ToList()
                : new List<CheckInEntry>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<CheckInEntry>();
        }
    }

    public CheckInEntry? Get(DateOnly date) => Load().FirstOrDefault(e => e.Date == date);

    /// <summary>Adds or replaces the entry for its date. Returns what is wrong with it, or null once saved.</summary>
    public string? Save(CheckInEntry entry)
    {
        if (entry.Validate() is { } problem)
        {
            return problem;
        }

        var all = Load().Where(e => e.Date != entry.Date).Append(entry).OrderBy(e => e.Date).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, Options));
        File.Move(tmp, _path, overwrite: true);
        return null;
    }

    public void MarkFilled(DateOnly date)
    {
        if (Get(date) is { } e)
        {
            e.Filled = true;
            Save(e);
        }
    }
}
