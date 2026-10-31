namespace Nyvora.Core.Tasks;

public sealed class TagDef
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Index into the UI's color palette (0 .. <see cref="TagSet.PaletteSize"/> - 1).</summary>
    public int Color { get; set; }
}

/// <summary>
/// The user's tags. Names are unique (ignoring case), trimmed, and never start with '#'. Edits return an error
/// message (null on success) and leave the set unchanged on failure. Not thread-safe: use it from the UI thread only.
/// </summary>
public sealed class TagSet
{
    public const int MaxTags = 30;
    public const int MaxNameLength = 20;
    public const int PaletteSize = 6;

    private readonly List<TagDef> _items = new();

    /// <summary>Builds a clean set from stored data, dropping blanks and duplicates.</summary>
    public TagSet(IEnumerable<TagDef>? defs = null)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs ?? Enumerable.Empty<TagDef>())
        {
            var name = Clean(d?.Name);
            if (d is null || name.Length == 0)
            {
                continue;
            }

            // Stored data is repaired rather than rejected; typed names are validated (see ValidateName).
            if (name.Length > MaxNameLength)
            {
                name = name[..MaxNameLength].TrimEnd();
            }

            var id = string.IsNullOrWhiteSpace(d.Id) ? NewId() : d.Id.Trim();
            if (!ids.Add(id) || !names.Add(name))
            {
                continue;
            }

            _items.Add(new TagDef { Id = id, Name = name, Color = Wrap(d.Color) });
            if (_items.Count == MaxTags)
            {
                break;
            }
        }
    }

    public event Action? Changed;

    public IReadOnlyList<TagDef> Items => _items;

    public TagDef? Find(string? id) => id is null ? null : _items.FirstOrDefault(t => t.Id == id);

    public List<TagDef> Export() =>
        _items.Select(t => new TagDef { Id = t.Id, Name = t.Name, Color = t.Color }).ToList();

    /// <summary>
    /// Replaces the tags with a freshly read list (another program, such as the MCP server, edited settings.json).
    /// Returns true, and raises <see cref="Changed"/> once, only when something actually differs.
    /// </summary>
    public bool Reload(IEnumerable<TagDef>? defs)
    {
        var fresh = new TagSet(defs)._items;
        if (fresh.Count == _items.Count && fresh.Zip(_items).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name && p.First.Color == p.Second.Color))
        {
            return false;
        }

        _items.Clear();
        _items.AddRange(fresh);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Creates a tag with the next palette color. Returns null and sets <paramref name="error"/> on failure.</summary>
    public TagDef? TryAdd(string name, out string? error)
    {
        if (_items.Count >= MaxTags)
        {
            error = $"You can have at most {MaxTags} tags.";
            return null;
        }

        error = ValidateName(name, exceptId: null);
        if (error is not null)
        {
            return null;
        }

        var tag = new TagDef { Id = NewId(), Name = Clean(name), Color = _items.Count % PaletteSize };
        _items.Add(tag);
        Changed?.Invoke();
        return tag;
    }

    public string? Rename(string id, string name)
    {
        var t = Find(id);
        if (t is null)
        {
            return "That tag no longer exists.";
        }

        if (ValidateName(name, exceptId: id) is { } err)
        {
            return err;
        }

        if (t.Name == Clean(name))
        {
            return null;
        }

        t.Name = Clean(name);
        Changed?.Invoke();
        return null;
    }

    public string? SetColor(string id, int color)
    {
        var t = Find(id);
        if (t is null)
        {
            return "That tag no longer exists.";
        }

        var c = Wrap(color);
        if (t.Color != c)
        {
            t.Color = c;
            Changed?.Invoke();
        }

        return null;
    }

    /// <summary>Tasks lose the removed tag via <see cref="TaskList.ApplyTags"/>.</summary>
    public string? Remove(string id)
    {
        var t = Find(id);
        if (t is null)
        {
            return "That tag no longer exists.";
        }

        _items.Remove(t);
        Changed?.Invoke();
        return null;
    }

    private string? ValidateName(string name, string? exceptId)
    {
        var n = Clean(name);
        if (n.Length == 0)
        {
            return "Give the tag a name.";
        }

        if (n.Length > MaxNameLength)
        {
            return $"Tag names can be at most {MaxNameLength} characters.";
        }

        return _items.Any(t => t.Id != exceptId && string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase))
            ? "A tag with that name already exists."
            : null;
    }

    private static string Clean(string? name) => (name ?? "").Trim().TrimStart('#').Trim();

    private static int Wrap(int color) => ((color % PaletteSize) + PaletteSize) % PaletteSize;

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];
}
