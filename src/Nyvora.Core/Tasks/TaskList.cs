namespace Nyvora.Core.Tasks;

/// <summary>In-memory task list. Not thread-safe: use it from the UI thread only. Time is always passed in.</summary>
public sealed class TaskList
{
    public const int MaxTitleLength = 200;

    private readonly List<TaskItem> _items;
    private readonly StatusSet _statuses;

    private readonly TagSet _tags;

    public TaskList(IEnumerable<TaskItem>? items = null, StatusSet? statuses = null, TagSet? tags = null)
    {
        _items = items is null ? new List<TaskItem>() : new List<TaskItem>(items);
        _statuses = statuses ?? new StatusSet();
        _tags = tags ?? new TagSet();
        Normalize(); // migrates files from before statuses and tags existed
    }

    /// <summary>
    /// Swaps in tasks changed by another program (the MCP server) and raises <see cref="Changed"/> so views redraw.
    /// The caller decides whether that change should be written back (it normally should not).
    /// </summary>
    public void ReplaceItems(IEnumerable<TaskItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Normalize();
        Changed?.Invoke();
    }

    /// <summary>True when the task matches the search text (see <see cref="TaskSearch"/>).</summary>
    public bool Matches(TaskItem item, string? query) =>
        TaskSearch.Matches(query, item.Title, StatusOf(item).Name, TagsOf(item).Select(t => t.Name), TaskPriorities.Name(item.Priority));

    public bool SetPriority(Guid id, TaskPriority priority)
    {
        var item = Find(id);
        if (item is null || item.Priority == priority || !Enum.IsDefined(priority))
        {
            return false;
        }

        item.Priority = priority;
        Changed?.Invoke();
        return true;
    }

    /// <summary>One click on the priority button: none, high, medium, low, none...</summary>
    public bool CyclePriority(Guid id)
    {
        var item = Find(id);
        return item is not null && SetPriority(id, TaskPriorities.Next(item.Priority));
    }

    public StatusSet Statuses => _statuses;

    public TagSet Tags => _tags;

    /// <summary>The task's tags in the user's tag order.</summary>
    public IReadOnlyList<TagDef> TagsOf(TaskItem item) => _tags.Items.Where(t => item.TagIds.Contains(t.Id)).ToList();

    /// <summary>Adds the tag to the task, or removes it if already there.</summary>
    public bool ToggleTag(Guid id, string tagId)
    {
        var item = Find(id);
        if (item is null || _tags.Find(tagId) is null)
        {
            return false;
        }

        if (!item.TagIds.Remove(tagId))
        {
            item.TagIds.Add(tagId);
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Drops references to tags that were deleted.</summary>
    public void ApplyTags() => ApplyStatuses();

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

            if (!Enum.IsDefined(item.Priority))
            {
                item.Priority = TaskPriority.None; // an out-of-range number in a hand-edited file
                changed = true;
            }

            item.TagIds ??= new List<string>();
            var kept = item.TagIds.Where(t => _tags.Find(t) is not null).Distinct().ToList();
            if (kept.Count != item.TagIds.Count)
            {
                item.TagIds = kept;
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

    /// <summary>Open task whose reminder is later today (local date) and not yet due.</summary>
    public static bool IsLaterToday(TaskItem item, DateTimeOffset now) =>
        !item.Done && item.RemindAt is { } at && at > now && at.ToOffset(now.Offset).Date == now.Date;

    public int LaterTodayCount(DateTimeOffset now) => _items.Count(i => IsLaterToday(i, now));

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

    /// <summary>Changes a task's title. False when nothing changed (unknown task, blank or identical title).</summary>
    public bool Rename(Guid id, string title)
    {
        var item = Find(id);
        title = (title ?? "").Trim();
        if (item is null || title.Length == 0)
        {
            return false;
        }

        if (title.Length > MaxTitleLength)
        {
            title = title[..MaxTitleLength];
        }

        if (item.Title == title)
        {
            return false;
        }

        item.Title = title;
        Changed?.Invoke();
        return true;
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
            .ThenByDescending(i => i.Done ? 0 : _statuses.IndexOf(StatusOf(i).Id)) // furthest along (e.g. In progress) first
            .ThenByDescending(i => i.Done ? 0 : (int)i.Priority) // then the more important
            .ThenBy(i => i.RemindAt ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.Created)
            .ToList();

    private TaskItem? Find(Guid id) => _items.FirstOrDefault(i => i.Id == id);
}
