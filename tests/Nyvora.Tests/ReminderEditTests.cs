using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class ReminderEditTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 13, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void SetReminderAfterCreationMakesItFire()
    {
        var list = new TaskList();
        var t = list.Add("call", Now)!;
        Assert.Empty(list.TakePendingNotifications(Now.AddHours(5)));

        Assert.True(list.SetReminder(t.Id, Now.AddHours(1)));
        Assert.Empty(list.TakePendingNotifications(Now.AddMinutes(30)));
        Assert.Single(list.TakePendingNotifications(Now.AddHours(1)));
    }

    [Fact]
    public void MovingAFiredReminderArmsItAgain()
    {
        var list = new TaskList();
        var t = list.Add("call", Now, Now)!;
        Assert.Single(list.TakePendingNotifications(Now));

        list.SetReminder(t.Id, Now.AddHours(2));
        Assert.Equal(0, list.DueCount(Now.AddHours(1)));
        Assert.Single(list.TakePendingNotifications(Now.AddHours(2)));
    }

    [Fact]
    public void ClearingRemovesTheReminder()
    {
        var list = new TaskList();
        var t = list.Add("call", Now, Now)!;
        Assert.True(list.SetReminder(t.Id, null));
        Assert.Null(t.RemindAt);
        Assert.Equal(0, list.DueCount(Now.AddDays(1)));
        Assert.False(list.SetReminder(Guid.NewGuid(), Now));
    }

    [Theory]
    [InlineData("14:30", 14, 30, 0)]   // later today
    [InlineData("9", 9, 0, 1)]          // already past today, so tomorrow
    [InlineData("1:00 pm", 13, 0, 1)]   // exactly now counts as passed
    [InlineData("1:01pm", 13, 1, 0)]
    [InlineData("12am", 0, 0, 1)]
    [InlineData("12 PM", 12, 0, 1)]
    [InlineData("  23:59 ", 23, 59, 0)]
    [InlineData("2:30 p.m.", 14, 30, 0)]
    public void ParsesClockTimes(string text, int hour, int minute, int dayOffset)
    {
        Assert.True(Reminders.TryParseClock(text, Now, out var at));
        Assert.Equal(new DateTimeOffset(Now.Date.AddDays(dayOffset).AddHours(hour).AddMinutes(minute), Now.Offset), at);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("noon")]
    [InlineData("24:00")]
    [InlineData("13pm")]
    [InlineData("0am")]
    [InlineData("10:60")]
    [InlineData("10:5")]
    [InlineData("1:2:3")]
    public void RejectsInvalidClockTimes(string? text)
    {
        Assert.False(Reminders.TryParseClock(text, Now, out _));
    }
}
