using System.Globalization;
using System.Text.RegularExpressions;

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

    private static readonly Regex Clock = new(
        @"^\s*(\d{1,2})(?::(\d{2}))?\s*(?:([ap])\.?m\.?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Parses a time of day ("14:30", "9", "2:30 pm", "12am") and returns its next occurrence after
    /// <paramref name="now"/>: today if it is still ahead, otherwise tomorrow.
    /// </summary>
    public static bool TryParseClock(string? text, DateTimeOffset now, out DateTimeOffset at)
    {
        at = default;
        if (!TryParseTimeOfDay(text, out var time))
        {
            return false;
        }

        var candidate = new DateTimeOffset(now.Date.Add(time), now.Offset);
        at = candidate > now ? candidate : candidate.AddDays(1);
        return true;
    }

    /// <summary>Parses a time of day ("14:30", "9", "2:30 pm", "12am") without any date. Words are not times here.</summary>
    public static bool TryParseTimeOfDay(string? text, out TimeSpan time)
    {
        time = default;
        var m = text is null ? null : Clock.Match(text);
        if (m is null || !m.Success)
        {
            return false;
        }

        var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (minute > 59)
        {
            return false;
        }

        if (m.Groups[3].Success)
        {
            if (hour is < 1 or > 12)
            {
                return false;
            }

            var pm = char.ToLowerInvariant(m.Groups[3].Value[0]) == 'p';
            hour = hour % 12 + (pm ? 12 : 0);
        }
        else if (hour > 23)
        {
            return false;
        }

        time = new TimeSpan(hour, minute, 0);
        return true;
    }

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
