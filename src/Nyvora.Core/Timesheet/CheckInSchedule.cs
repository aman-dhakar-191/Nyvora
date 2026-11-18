namespace Nyvora.Core.Timesheet;

/// <summary>
/// When the daily check-in and the weekly "submit your timesheet" reminder are due. Time is passed in, so it is
/// deterministic to test. A prompt that was missed (the PC was off or asleep) is still due later the same day, and
/// <paramref name="lastDone"/> makes sure it appears only once per day.
/// </summary>
public static class CheckInSchedule
{
    public const string DefaultTime = "18:00";
    public const string DefaultDays = "Mon,Tue,Wed,Thu,Fri";
    public const string DefaultWeeklyDay = "Fri";
    public const string DefaultWeeklyTime = "17:00";

    private static readonly string[] Names = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    /// <summary>"18:00" or "6:30 pm" style text. False (and midnight) when it is not a time.</summary>
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (DateTime.TryParseExact(text.Trim(), new[] { "H:mm", "HH:mm", "h:mm tt", "h:mmtt", "h tt", "htt" },
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            time = parsed.TimeOfDay;
            return true;
        }

        return false;
    }

    /// <summary>"Mon,Tue,Fri" (three letters or full names, any case). Unknown words are ignored; empty text gives no days.</summary>
    public static IReadOnlySet<DayOfWeek> ParseDays(string? text)
    {
        var days = new HashSet<DayOfWeek>();
        foreach (var part in (text ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseDay(part, out var day))
            {
                days.Add(day);
            }
        }

        return days;
    }

    public static bool TryParseDay(string? text, out DayOfWeek day)
    {
        day = DayOfWeek.Sunday;
        var t = (text ?? "").Trim();
        if (t.Length < 3)
        {
            return false;
        }

        for (var i = 0; i < Names.Length; i++)
        {
            if (t.StartsWith(Names[i], StringComparison.OrdinalIgnoreCase))
            {
                day = (DayOfWeek)i;
                return true;
            }
        }

        return false;
    }

    public static string FormatDays(IEnumerable<DayOfWeek> days) =>
        string.Join(",", days.Distinct().OrderBy(d => ((int)d + 6) % 7).Select(d => Names[(int)d]));

    /// <summary>
    /// True when today is one of <paramref name="days"/>, the time has passed, and the prompt has not been shown today.
    /// A time that cannot be read falls back to <paramref name="fallbackTime"/> so a typo never silences the reminder.
    /// </summary>
    public static bool IsDue(DateTimeOffset now, string? time, string? days, DateOnly? lastDone, string fallbackTime = DefaultTime)
    {
        if (!TryParseTime(time, out var at) && !TryParseTime(fallbackTime, out at))
        {
            at = TimeSpan.FromHours(18);
        }

        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (lastDone is { } done && done >= today)
        {
            return false;
        }

        return ParseDays(days).Contains(now.LocalDateTime.DayOfWeek) && now.LocalDateTime.TimeOfDay >= at;
    }

    /// <summary>The weekly reminder is the same rule with a single day.</summary>
    public static bool IsWeeklyDue(DateTimeOffset now, string? day, string? time, DateOnly? lastDone) =>
        IsDue(now, time, TryParseDay(day, out var d) ? Names[(int)d] : DefaultWeeklyDay, lastDone, DefaultWeeklyTime);
}
