namespace Nyvora.Core.Tasks;

public static class PillLabel
{
    /// <summary>Text on the resting pill: due reminders first, then open tasks, otherwise the app name.</summary>
    public static string For(int due, int open) => (due, open) switch
    {
        ( > 0, > 0) => $"{due} due · {open} open",
        ( > 0, _) => $"{due} due",
        (_, > 0) => $"{open} open",
        _ => "Nyvora",
    };
}
