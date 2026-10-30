using System.Text.RegularExpressions;

namespace Nyvora.Core.Tasks;

/// <summary>
/// Understands reminder times typed in plain words: "in 2h", "30 min", "tomorrow 9am", "tonight", "fri 5pm",
/// "next monday", "nov 2 3pm", "2 nov", "2026-11-05 14:00" or just a time ("14:30", "9am"). Everything is relative
/// to the <c>now</c> passed in; a result is always in the future.
/// </summary>
public static class ReminderText
{
    private const int DefaultHour = 9;

    private static readonly Regex Relative = new(
        @"^(?:in )?(\d{1,3}) ?(m|min|mins|minute|minutes|h|hr|hrs|hour|hours|d|day|days|w|wk|wks|week|weeks)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DayWord = new(@"^(today|tonight|tomorrow|tmrw|tom)(?: (.+))?$", RegexOptions.Compiled);
    private static readonly Regex Weekday = new(@"^(?:(next) )?([a-z]{3,9})(?: (.+))?$", RegexOptions.Compiled);
    private static readonly Regex DayMonth = new(@"^(\d{1,2}) ([a-z]{3,9})(?: (.+))?$", RegexOptions.Compiled);
    private static readonly Regex MonthDay = new(@"^([a-z]{3,9}) (\d{1,2})(?: (.+))?$", RegexOptions.Compiled);
    private static readonly Regex Iso = new(@"^(\d{4})-(\d{2})-(\d{2})(?: (.+))?$", RegexOptions.Compiled);

    private static readonly string[] DayNames = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    private static readonly string[] MonthNames =
    {
        "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december",
    };

    public static bool TryParse(string? text, DateTimeOffset now, out DateTimeOffset at)
    {
        at = default;
        var s = Regex.Replace((text ?? "").Trim().ToLowerInvariant().Replace(',', ' '), @"\s+", " ");
        s = Regex.Replace(s, @"\bat\b", " ");           // "tomorrow at 9am" == "tomorrow 9am"; "sat" is untouched
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length == 0)
        {
            return false;
        }

        var m = Relative.Match(s);
        if (m.Success)
        {
            var n = int.Parse(m.Groups[1].Value);
            if (n == 0)
            {
                return false;
            }

            var span = m.Groups[2].Value[0] switch
            {
                'm' => TimeSpan.FromMinutes(n),
                'h' => TimeSpan.FromHours(n),
                'd' => TimeSpan.FromDays(n),
                _ => TimeSpan.FromDays(7 * n),
            };
            at = Trim(now + span);
            return true;
        }

        m = DayWord.Match(s);
        if (m.Success)
        {
            var word = m.Groups[1].Value;
            var date = word is "today" or "tonight" ? now.Date : now.Date.AddDays(1);
            int? defaultHour = word == "tonight" ? 20 : word is "today" ? null : DefaultHour;
            return Finish(date, m.Groups[2], defaultHour, now, Roll.Fail, out at);
        }

        m = Weekday.Match(s);
        if (m.Success && TryWeekday(m.Groups[2].Value, out var dow))
        {
            var next = m.Groups[1].Success; // "next fri": this coming Friday, but never today
            var ahead = ((int)dow - (int)now.DayOfWeek + 7) % 7;
            if (next && ahead == 0)
            {
                ahead = 7;
            }

            return Finish(now.Date.AddDays(ahead), m.Groups[3], DefaultHour, now, Roll.Week, out at);
        }

        m = DayMonth.Match(s);
        if (m.Success && TryMonth(m.Groups[2].Value, out var month1))
        {
            return FromMonthDay(month1, int.Parse(m.Groups[1].Value), m.Groups[3], now, out at);
        }

        m = MonthDay.Match(s);
        if (m.Success && TryMonth(m.Groups[1].Value, out var month2))
        {
            return FromMonthDay(month2, int.Parse(m.Groups[2].Value), m.Groups[3], now, out at);
        }

        m = Iso.Match(s);
        if (m.Success)
        {
            try
            {
                var date = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
                return Finish(date, m.Groups[4], DefaultHour, now, Roll.Fail, out at);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return Reminders.TryParseClock(s, now, out at);
    }

    private enum Roll
    {
        Fail,
        Week,
    }

    private static bool FromMonthDay(int month, int day, Group time, DateTimeOffset now, out DateTimeOffset at)
    {
        at = default;
        for (var year = now.Year; year <= now.Year + 1; year++)
        {
            if (day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                continue;
            }

            if (Finish(new DateTime(year, month, day), time, DefaultHour, now, Roll.Fail, out var candidate))
            {
                at = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool Finish(DateTime date, Group timeText, int? defaultHour, DateTimeOffset now, Roll roll, out DateTimeOffset at)
    {
        at = default;
        TimeSpan time;
        if (timeText.Success && timeText.Value.Length > 0)
        {
            if (!Reminders.TryParseTimeOfDay(timeText.Value, out time))
            {
                return false;
            }
        }
        else if (defaultHour is { } h)
        {
            time = TimeSpan.FromHours(h);
        }
        else
        {
            return false; // "today" alone does not say when
        }

        var candidate = new DateTimeOffset(date.Date.Add(time), now.Offset);
        if (candidate <= now && roll == Roll.Week)
        {
            candidate = candidate.AddDays(7);
        }

        if (candidate <= now)
        {
            return false;
        }

        at = candidate;
        return true;
    }

    private static DateTimeOffset Trim(DateTimeOffset t) =>
        new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);

    private static bool TryWeekday(string word, out DayOfWeek day)
    {
        for (var i = 0; i < DayNames.Length; i++)
        {
            if (DayNames[i].StartsWith(word, StringComparison.Ordinal))
            {
                day = (DayOfWeek)i;
                return true;
            }
        }

        day = default;
        return false;
    }

    private static bool TryMonth(string word, out int month)
    {
        for (var i = 0; i < MonthNames.Length; i++)
        {
            if (MonthNames[i].StartsWith(word, StringComparison.Ordinal))
            {
                month = i + 1;
                return true;
            }
        }

        month = 0;
        return false;
    }
}
