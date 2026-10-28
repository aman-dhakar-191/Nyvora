namespace Nyvora.Core.Tasks;

public sealed class StatusDef
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>True when tasks in this status count as finished (no longer open, no reminders).</summary>
    public bool IsDone { get; set; }
}

/// <summary>
/// The user's ordered list of task statuses. Always valid: at least one "open" and one "done" status,
/// unique non-empty names. Edits return an error message (null on success) and leave the set unchanged on failure.
/// Not thread-safe: use it from the UI thread only.
/// </summary>
public sealed class StatusSet
{
    public const int MaxStatuses = 10;
    public const int MaxNameLength = 24;

    private readonly List<StatusDef> _items;

    /// <summary>Builds a valid set from stored data; anything unusable falls back to the defaults.</summary>
    public StatusSet(IEnumerable<StatusDef>? defs = null)
    {
        var list = new List<StatusDef>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in defs ?? Enumerable.Empty<StatusDef>())
        {
            var name = (d?.Name ?? "").Trim();
            if (d is null || name.Length == 0)
            {
                continue;
            }

            if (name.Length > MaxNameLength)
            {
                name = name[..MaxNameLength];
            }

            var id = string.IsNullOrWhiteSpace(d.Id) ? NewId() : d.Id.Trim();
            if (!ids.Add(id) || !names.Add(name))
            {
                continue;
            }

            list.Add(new StatusDef { Id = id, Name = name, IsDone = d.IsDone });
            if (list.Count == MaxStatuses)
            {
                break;
            }
        }

        _items = IsUsable(list) ? list : DefaultDefs();
    }

    public event Action? Changed;

    public IReadOnlyList<StatusDef> Items => _items;

    public StatusDef FirstOpen => _items.First(s => !s.IsDone);

    public StatusDef FirstDone => _items.First(s => s.IsDone);

    public static List<StatusDef> DefaultDefs() => new()
    {
        new StatusDef { Id = "todo", Name = "To do", IsDone = false },
        new StatusDef { Id = "doing", Name = "In progress", IsDone = false },
        new StatusDef { Id = "done", Name = "Done", IsDone = true },
    };

    public StatusDef? Find(string? id) => id is null ? null : _items.FirstOrDefault(s => s.Id == id);

    public int IndexOf(string? id) => _items.FindIndex(s => s.Id == id);

    /// <summary>The status after <paramref name="id"/> in list order, wrapping around.</summary>
    public StatusDef Next(string? id)
    {
        var i = IndexOf(id);
        return _items[(i + 1) % _items.Count];
    }

    public List<StatusDef> Export() =>
        _items.Select(s => new StatusDef { Id = s.Id, Name = s.Name, IsDone = s.IsDone }).ToList();

    public string? Add(string name, bool isDone)
    {
        if (_items.Count >= MaxStatuses)
        {
            return $"You can have at most {MaxStatuses} statuses.";
        }

        if (ValidateName(name, exceptId: null) is { } err)
        {
            return err;
        }

        _items.Add(new StatusDef { Id = NewId(), Name = name.Trim(), IsDone = isDone });
        Changed?.Invoke();
        return null;
    }

    public string? Rename(string id, string name)
    {
        var s = Find(id);
        if (s is null)
        {
            return "That status no longer exists.";
        }

        if (ValidateName(name, exceptId: id) is { } err)
        {
            return err;
        }

        if (s.Name == name.Trim())
        {
            return null;
        }

        s.Name = name.Trim();
        Changed?.Invoke();
        return null;
    }

    public string? SetDone(string id, bool isDone)
    {
        var s = Find(id);
        if (s is null)
        {
            return "That status no longer exists.";
        }

        if (s.IsDone == isDone)
        {
            return null;
        }

        var candidate = _items.Select(x => x.Id == id ? new StatusDef { Id = x.Id, Name = x.Name, IsDone = isDone } : x).ToList();
        if (!IsUsable(candidate))
        {
            return isDone
                ? "Keep at least one status that is not done."
                : "Keep at least one status that counts as done.";
        }

        s.IsDone = isDone;
        Changed?.Invoke();
        return null;
    }

    /// <summary>Tasks in a removed status are moved by <see cref="TaskList.ApplyStatuses"/>.</summary>
    public string? Remove(string id)
    {
        var s = Find(id);
        if (s is null)
        {
            return "That status no longer exists.";
        }

        if (!IsUsable(_items.Where(x => x.Id != id).ToList()))
        {
            return s.IsDone
                ? "Keep at least one status that counts as done."
                : "Keep at least one status that is not done.";
        }

        _items.Remove(s);
        Changed?.Invoke();
        return null;
    }

    public string? Move(string id, int delta)
    {
        var i = IndexOf(id);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _items.Count)
        {
            return null;
        }

        (_items[i], _items[j]) = (_items[j], _items[i]);
        Changed?.Invoke();
        return null;
    }

    private string? ValidateName(string name, string? exceptId)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0)
        {
            return "Give the status a name.";
        }

        if (n.Length > MaxNameLength)
        {
            return $"Names can be at most {MaxNameLength} characters.";
        }

        return _items.Any(s => s.Id != exceptId && string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase))
            ? "Another status already has that name."
            : null;
    }

    private static bool IsUsable(List<StatusDef> list) => list.Any(s => s.IsDone) && list.Any(s => !s.IsDone);

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];
}
