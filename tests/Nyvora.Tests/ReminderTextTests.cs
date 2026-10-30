using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public sealed class ReminderTextTests
{
    // Friday 30 October 2026, 12:00.
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(5.5));

    private static DateTimeOffset At(int y, int mo, int d, int h = 0, int mi = 0) => new(y, mo, d, h, mi, 0, Now.Offset);

    private static DateTimeOffset? Parse(string? text) => ReminderText.TryParse(text, Now, out var at) ? at : null;

    [Theory]
    [InlineData("in 30m", 12, 30)]
    [InlineData("30 min", 12, 30)]
    [InlineData("in 2 hours", 14, 0)]
    [InlineData("2h", 14, 0)]
    [InlineData("IN 90 MINUTES", 13, 30)]
    public void RelativeTimes(string text, int hour, int minute) =>
        Assert.Equal(At(2026, 10, 30, hour, minute), Parse(text));

    [Fact]
    public void RelativeDaysAndWeeks()
    {
        Assert.Equal(At(2026, 10, 31, 12), Parse("1 day"));
        Assert.Equal(At(2026, 11, 13, 12), Parse("in 2 weeks"));
    }

    [Theory]
    [InlineData("tomorrow", 9, 0)]
    [InlineData("tomorrow 5pm", 17, 0)]
    [InlineData("tmrw 14:30", 14, 30)]
    [InlineData("TOMORROW AT 9AM", 9, 0)]
    public void Tomorrow(string text, int hour, int minute) =>
        Assert.Equal(At(2026, 10, 31, hour, minute), Parse(text));

    [Fact]
    public void TodayNeedsATimeThatIsStillAhead()
    {
        Assert.Equal(At(2026, 10, 30, 18), Parse("today 18:00"));
        Assert.Equal(At(2026, 10, 30, 20), Parse("tonight"));
        Assert.Null(Parse("today 9am"));  // already past
        Assert.Null(Parse("today"));      // no time given
    }

    [Fact]
    public void Weekdays()
    {
        Assert.Equal(At(2026, 11, 2, 9), Parse("mon"));
        Assert.Equal(At(2026, 11, 2, 17), Parse("monday 5pm"));
        Assert.Equal(At(2026, 10, 30, 17), Parse("fri 5pm"));     // today, still ahead
        Assert.Equal(At(2026, 11, 6, 9), Parse("fri 9am"));       // today's 9am is gone: next week
        Assert.Equal(At(2026, 11, 6, 9), Parse("next fri"));      // never today
        Assert.Equal(At(2026, 11, 4, 9), Parse("next wed"));
        Assert.Equal(At(2026, 10, 31, 9), Parse("sat"));          // "sat" is not mangled by removing "at"
    }

    [Fact]
    public void MonthAndDayInEitherOrder()
    {
        Assert.Equal(At(2026, 11, 2, 9), Parse("nov 2"));
        Assert.Equal(At(2026, 11, 2, 15), Parse("2 nov 3pm"));
        Assert.Equal(At(2026, 12, 25, 9), Parse("december 25"));
        Assert.Equal(At(2027, 10, 29, 9), Parse("oct 29"));       // this year's date is past: next year
    }

    [Fact]
    public void IsoDates()
    {
        Assert.Equal(At(2026, 11, 5, 14), Parse("2026-11-05 14:00"));
        Assert.Equal(At(2026, 11, 5, 9), Parse("2026-11-05"));
        Assert.Null(Parse("2026-10-01"));   // past
        Assert.Null(Parse("2026-13-01"));   // not a date
    }

    [Fact]
    public void PlainTimesUseTheNextOccurrence()
    {
        Assert.Equal(At(2026, 10, 30, 14, 30), Parse("14:30"));
        Assert.Equal(At(2026, 10, 31, 9), Parse("9am"));          // 9am has passed today
        Assert.Equal(At(2026, 10, 30, 12, 0).AddDays(1), Parse("12"));
        Assert.Equal(At(2026, 10, 31, 12), Parse("noon"));
        Assert.Equal(At(2026, 11, 2, 12), Parse("mon noon"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("soon")]
    [InlineData("in 0m")]
    [InlineData("tomorrow banana")]
    [InlineData("32 oct")]
    [InlineData("feb 30")]
    [InlineData("25:00")]
    public void ThingsThatAreNotATimeAreRejected(string? text) => Assert.Null(Parse(text));

    [Fact]
    public void ResultsAreAlwaysInTheFuture()
    {
        foreach (var text in new[] { "in 1m", "tomorrow", "mon", "fri 9am", "nov 2", "9am", "14:30", "2026-12-01" })
        {
            var at = Parse(text);
            Assert.NotNull(at);
            Assert.True(at > Now, text);
        }
    }

    [Fact]
    public void TimeOfDayParsingIsSharedWithTheClockParser()
    {
        Assert.True(Reminders.TryParseTimeOfDay("2:30 pm", out var t));
        Assert.Equal(new TimeSpan(14, 30, 0), t);
        Assert.False(Reminders.TryParseTimeOfDay("noon", out _)); // words are only understood by ReminderText
        Assert.False(Reminders.TryParseTimeOfDay("13pm", out _));
    }
}
