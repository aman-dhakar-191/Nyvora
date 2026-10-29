using System.Text.Json;

namespace Nyvora.Core.Tasks;

public static class TaskSnapshot
{
    /// <summary>True when both lists serialize identically (same tasks, same order, same fields).</summary>
    public static bool Same(IEnumerable<TaskItem> a, IEnumerable<TaskItem> b) =>
        JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
}
