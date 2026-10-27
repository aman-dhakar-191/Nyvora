using System.Globalization;

namespace Nyvora.Core.Tasks;

public enum ReminderPreset
{
    None,
    InTenMinutes,
    InOneHour,
    TomorrowMorning,
}

public static class Reminders
{
    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromMinutes(10);

    public static DateTimeOffset? Resolve(ReminderPreset preset, DateTimeOffset now) => preset switch
    {
        ReminderPreset.None => null,
        ReminderPreset.InTenMinutes => now.AddMinutes(10),
        ReminderPreset.InOneHour => now.AddHours(1),
        ReminderPreset.TomorrowMorning => new DateTimeOffset(now.Date.AddDays(1).AddHours(9), now.Offset),
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    /// <summary>Compact, local-time label such as "14:30", "Tomorrow 09:00" or "Thu 09:00".</summary>
    public static string FormatDue(DateTimeOffset due, DateTimeOffset now)
    {
        var local = due.ToOffset(now.Offset);
        var days = (local.Date - now.Date).Days;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);

        return days switch
        {
            0 => time,
            1 => $"Tomorrow {time}",
            > 1 and < 7 => $"{local.ToString("ddd", CultureInfo.CurrentCulture)} {time}",
            _ => $"{local.ToString("MMM d", CultureInfo.CurrentCulture)} {time}",
        };
    }
}
