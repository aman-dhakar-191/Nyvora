namespace Nyvora.Core.Tasks;

/// <summary>In-memory task list. Not thread-safe: use it from the UI thread only. Time is always passed in.</summary>
public sealed class TaskList
{
    public const int MaxTitleLength = 200;

    private readonly List<TaskItem> _items;

    public TaskList(IEnumerable<TaskItem>? items = null)
    {
        _items = items is null ? new List<TaskItem>() : new List<TaskItem>(items);
    }

    public event Action? Changed;

    public IReadOnlyList<TaskItem> Items => _items;

    public int OpenCount => _items.Count(i => !i.Done);

    public static bool IsDue(TaskItem item, DateTimeOffset now) =>
        !item.Done && item.RemindAt is { } at && at <= now;

    public int DueCount(DateTimeOffset now) => _items.Count(i => IsDue(i, now));

    public TaskItem? Add(string title, DateTimeOffset now, DateTimeOffset? remindAt = null)
    {
        title = title.Trim();
        if (title.Length == 0)
        {
            return null;
        }

        if (title.Length > MaxTitleLength)
        {
            title = title[..MaxTitleLength];
        }

        var item = new TaskItem { Id = Guid.NewGuid(), Title = title, Created = now, RemindAt = remindAt };
        _items.Add(item);
        Changed?.Invoke();
        return item;
    }

    public bool Toggle(Guid id)
    {
        var item = Find(id);
        if (item is null)
        {
            return false;
        }

        item.Done = !item.Done;
        Changed?.Invoke();
        return true;
    }

    public bool Remove(Guid id)
    {
        var item = Find(id);
        if (item is null)
        {
            return false;
        }

        _items.Remove(item);
        Changed?.Invoke();
        return true;
    }

    public bool Snooze(Guid id, DateTimeOffset now, TimeSpan by)
    {
        var item = Find(id);
        if (item is null || by <= TimeSpan.Zero)
        {
            return false;
        }

        item.RemindAt = now + by;
        item.Notified = false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Returns due, not-yet-notified tasks and marks them notified so each fires once.</summary>
    public IReadOnlyList<TaskItem> TakePendingNotifications(DateTimeOffset now)
    {
        var pending = _items.Where(i => IsDue(i, now) && !i.Notified).ToList();
        if (pending.Count == 0)
        {
            return pending;
        }

        foreach (var item in pending)
        {
            item.Notified = true;
        }

        Changed?.Invoke();
        return pending;
    }

    /// <summary>Open tasks first (soonest reminder first, then oldest), completed tasks last.</summary>
    public IReadOnlyList<TaskItem> Ordered() =>
        _items
            .OrderBy(i => i.Done)
            .ThenBy(i => i.RemindAt ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.Created)
            .ToList();

    private TaskItem? Find(Guid id) => _items.FirstOrDefault(i => i.Id == id);
}
