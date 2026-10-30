namespace Nyvora.Core.Tasks;

public sealed class TaskItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    /// <summary>Mirrors the task's status (<see cref="StatusDef.IsDone"/>); kept so older files and reminder logic keep working.</summary>
    public bool Done { get; set; }

    /// <summary>Id of a <see cref="StatusDef"/>. Null in files written before statuses existed; resolved from <see cref="Done"/>.</summary>
    public string? StatusId { get; set; }

    /// <summary>Ids of the <see cref="TagDef"/>s on this task. Empty for files written before tags existed.</summary>
    public List<string> TagIds { get; set; } = new();

    /// <summary>None for files written before priorities existed.</summary>
    public TaskPriority Priority { get; set; }

    public DateTimeOffset Created { get; set; }

    public DateTimeOffset? RemindAt { get; set; }

    /// <summary>True once the reminder has been surfaced to the user; reset by snoozing.</summary>
    public bool Notified { get; set; }
}
