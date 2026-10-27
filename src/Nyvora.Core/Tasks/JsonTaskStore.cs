using System.Text.Json;

namespace Nyvora.Core.Tasks;

/// <summary>Persists tasks as JSON. Saves are atomic (temp file + replace) so a crash cannot truncate the file.</summary>
public sealed class JsonTaskStore
{
    private readonly string _path;

    public JsonTaskStore(string path) => _path = path;

    /// <summary>True when the last <see cref="Load"/> found an unreadable file and set it aside as *.corrupt.</summary>
    public bool RecoveredFromCorruption { get; private set; }

    public List<TaskItem> Load()
    {
        RecoveredFromCorruption = false;
        if (!File.Exists(_path))
        {
            return new List<TaskItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(_path)) ?? new List<TaskItem>();
        }
        catch (JsonException)
        {
            File.Move(_path, _path + ".corrupt", overwrite: true);
            RecoveredFromCorruption = true;
            return new List<TaskItem>();
        }
    }

    public void Save(IEnumerable<TaskItem> items)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
