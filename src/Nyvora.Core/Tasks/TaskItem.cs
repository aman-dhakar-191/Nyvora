namespace Nyvora.Core.Tasks;

public sealed class TaskItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public bool Done { get; set; }

    public DateTimeOffset Created { get; set; }

    public DateTimeOffset? RemindAt { get; set; }

    /// <summary>True once the reminder has been surfaced to the user; reset by snoozing.</summary>
    public bool Notified { get; set; }
}
