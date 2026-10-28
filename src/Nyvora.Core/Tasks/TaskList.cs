namespace Nyvora.Core.Tasks;

/// <summary>In-memory task list. Not thread-safe: use it from the UI thread only. Time is always passed in.</summary>
public sealed class TaskList
{
    public const int MaxTitleLength = 200;

    private readonly List<TaskItem> _items;
    private readonly StatusSet _statuses;

    public TaskList(IEnumerable<TaskItem>? items = null, StatusSet? statuses = null)
    {
        _items = items is null ? new List<TaskItem>() : new List<TaskItem>(items);
        _statuses = statuses ?? new StatusSet();
        Normalize(); // migrates files from before statuses existed
    }

    public StatusSet Statuses => _statuses;

    public StatusDef StatusOf(TaskItem item) =>
        _statuses.Find(item.StatusId) ?? (item.Done ? _statuses.FirstDone : _statuses.FirstOpen);

    /// <summary>Re-syncs every task with the current statuses, e.g. after one was removed or its "done" flag changed.</summary>
    public void ApplyStatuses()
    {
        if (Normalize())
        {
            Changed?.Invoke();
        }
    }

    private bool Normalize()
    {
        var changed = false;
        foreach (var item in _items)
        {
            var s = StatusOf(item);
            if (item.StatusId != s.Id || item.Done != s.IsDone)
            {
                item.StatusId = s.Id;
                item.Done = s.IsDone;
                changed = true;
            }
        }

        return changed;
    }

    public bool SetStatus(Guid id, string statusId)
    {
        var item = Find(id);
        var s = _statuses.Find(statusId);
        if (item is null || s is null)
        {
            return false;
        }

        item.StatusId = s.Id;
        item.Done = s.IsDone;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Moves the task to the next status in the user's order, wrapping around.</summary>
    public bool CycleStatus(Guid id)
    {
        var item = Find(id);
        return item is not null && SetStatus(id, _statuses.Next(StatusOf(item).Id).Id);
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

        var item = new TaskItem { Id = Guid.NewGuid(), Title = title, Created = now, RemindAt = remindAt, StatusId = _statuses.FirstOpen.Id };
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

        // The checkbox: finish an open task, reopen a finished one.
        var target = item.Done ? _statuses.FirstOpen : _statuses.FirstDone;
        item.StatusId = target.Id;
        item.Done = target.IsDone;
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

    /// <summary>Sets, moves or (with null) clears a task's reminder; a changed reminder fires again.</summary>
    public bool SetReminder(Guid id, DateTimeOffset? at)
    {
        var item = Find(id);
        if (item is null)
        {
            return false;
        }

        item.RemindAt = at;
        item.Notified = false;
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
