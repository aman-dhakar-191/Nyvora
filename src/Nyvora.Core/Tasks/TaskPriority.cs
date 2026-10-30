namespace Nyvora.Core.Tasks;

/// <summary>How much a task matters. Stored as a number (None = 0), so files from before priorities existed read as None.</summary>
public enum TaskPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public static class TaskPriorities
{
    public static string Name(TaskPriority p) => p switch
    {
        TaskPriority.Low => "Low",
        TaskPriority.Medium => "Medium",
        TaskPriority.High => "High",
        _ => "None",
    };

    /// <summary>Accepts none/low/medium/med/high in any case; anything else is not a priority.</summary>
    public static bool TryParse(string? text, out TaskPriority priority)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "none" or "":
                priority = TaskPriority.None;
                return true;
            case "low":
                priority = TaskPriority.Low;
                return true;
            case "medium" or "med":
                priority = TaskPriority.Medium;
                return true;
            case "high":
                priority = TaskPriority.High;
                return true;
            default:
                priority = TaskPriority.None;
                return false;
        }
    }

    /// <summary>The click order: none, then high down to low, then back to none.</summary>
    public static TaskPriority Next(TaskPriority p) => p switch
    {
        TaskPriority.None => TaskPriority.High,
        TaskPriority.High => TaskPriority.Medium,
        TaskPriority.Medium => TaskPriority.Low,
        _ => TaskPriority.None,
    };
}
