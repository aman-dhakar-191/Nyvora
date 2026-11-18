using Nyvora.Core.Settings;
using Nyvora.Core.Tasks;
using Nyvora.Core.Timesheet;
using Xunit;

namespace Nyvora.Tests;

public sealed class CheckInScheduleTests
{
    // 2026-11-16 is a Monday.
    private static DateTimeOffset At(int day, int h, int m = 0) =>
        new(new DateTime(2026, 11, day, h, m, 0, DateTimeKind.Local));

    [Theory]
    [InlineData("18:00", 18, 0)]
    [InlineData("6:30 pm", 18, 30)]
    [InlineData("9:05", 9, 5)]
    public void ReadsTimes(string text, int h, int m)
    {
        Assert.True(CheckInSchedule.TryParseTime(text, out var t));
        Assert.Equal(new TimeSpan(h, m, 0), t);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("25:00")]
    public void RejectsBadTimes(string text) => Assert.False(CheckInSchedule.TryParseTime(text, out _));

    [Fact]
    public void ParsesDaysAndIgnoresJunk()
    {
        var days = CheckInSchedule.ParseDays("mon, Wednesday;fri xyz");
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, days.OrderBy(d => (int)d));
        Assert.Empty(CheckInSchedule.ParseDays(""));
    }

    [Fact]
    public void FormatsDaysMondayFirst() =>
        Assert.Equal("Mon,Fri,Sun", CheckInSchedule.FormatDays(new[] { DayOfWeek.Sunday, DayOfWeek.Friday, DayOfWeek.Monday }));

    [Fact]
    public void DueAfterTheTimeOnAChosenDay()
    {
        Assert.False(CheckInSchedule.IsDue(At(16, 17, 59), "18:00", "Mon,Tue", null));
        Assert.True(CheckInSchedule.IsDue(At(16, 18, 0), "18:00", "Mon,Tue", null));
        Assert.True(CheckInSchedule.IsDue(At(16, 23, 30), "18:00", "Mon,Tue", null)); // missed while away: still due tonight
    }

    [Fact]
    public void NotOnOtherDays() => Assert.False(CheckInSchedule.IsDue(At(21, 19), "18:00", "Mon,Tue,Wed,Thu,Fri", null)); // Saturday

    [Fact]
    public void OnlyOncePerDay()
    {
        var today = DateOnly.FromDateTime(At(16, 19).LocalDateTime);
        Assert.False(CheckInSchedule.IsDue(At(16, 19), "18:00", "Mon", today));
        Assert.True(CheckInSchedule.IsDue(At(23, 19), "18:00", "Mon", today)); // next Monday
    }

    [Fact]
    public void ABadTimeFallsBackToTheDefault()
    {
        Assert.False(CheckInSchedule.IsDue(At(16, 17), "oops", "Mon", null));
        Assert.True(CheckInSchedule.IsDue(At(16, 18), "oops", "Mon", null));
    }

    [Fact]
    public void WeeklyReminderIsDueOnItsDay()
    {
        Assert.False(CheckInSchedule.IsWeeklyDue(At(19, 16), "Fri", "17:00", null)); // Thursday
        Assert.False(CheckInSchedule.IsWeeklyDue(At(20, 16), "Fri", "17:00", null));
        Assert.True(CheckInSchedule.IsWeeklyDue(At(20, 17), "Fri", "17:00", null));
    }

    [Fact]
    public void DefaultsAreSensible()
    {
        var s = new AppSettings();
        Assert.True(s.CheckInEnabled);
        Assert.Equal("18:00", s.CheckInTime);
        Assert.Equal("Fri", s.WeeklyReminderDay);
        Assert.Equal("", s.KekaUrl);
    }

    [Fact]
    public void SettingsRoundTripTheNewFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "nyvora-ci-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new AppSettings { KekaUrl = "https://x.keka.com/a", LastCheckInPrompt = new DateOnly(2026, 11, 16) }.Save(path);
            Assert.True(AppSettings.TryRead(path, out var s));
            Assert.Equal("https://x.keka.com/a", s.KekaUrl);
            Assert.Equal(new DateOnly(2026, 11, 16), s.LastCheckInPrompt);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class CheckInStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nyvora-ci-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private CheckInStore Store() => new(Path.Combine(_dir, "checkins.json"));

    [Fact]
    public void SavesAndReadsAnEntry()
    {
        var s = Store();
        Assert.Null(s.Save(new CheckInEntry { Date = new DateOnly(2026, 11, 16), Project = "P", Task = "T", Hours = 7.5, Comment = "did x" }));
        var e = s.Get(new DateOnly(2026, 11, 16))!;
        Assert.Equal(7.5, e.Hours);
        Assert.Equal("did x", e.Comment);
        Assert.False(e.Filled);
    }

    [Fact]
    public void SavingTheSameDayReplaces()
    {
        var s = Store();
        s.Save(new CheckInEntry { Date = new DateOnly(2026, 11, 16), Hours = 4 });
        s.Save(new CheckInEntry { Date = new DateOnly(2026, 11, 16), Hours = 8 });
        Assert.Single(s.Load());
        Assert.Equal(8, s.Get(new DateOnly(2026, 11, 16))!.Hours);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    [InlineData(double.NaN)]
    public void RejectsBadHours(double hours)
    {
        var s = Store();
        Assert.NotNull(s.Save(new CheckInEntry { Date = new DateOnly(2026, 11, 16), Hours = hours }));
        Assert.Empty(s.Load());
    }

    [Fact]
    public void MarksFilled()
    {
        var s = Store();
        s.Save(new CheckInEntry { Date = new DateOnly(2026, 11, 16), Hours = 8 });
        s.MarkFilled(new DateOnly(2026, 11, 16));
        Assert.True(s.Get(new DateOnly(2026, 11, 16))!.Filled);
    }

    [Fact]
    public void AMissingOrBrokenFileIsEmpty()
    {
        Assert.Empty(Store().Load());
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "checkins.json"), "{ not json");
        Assert.Empty(Store().Load());
    }
}

public sealed class KekaResultTests
{
    [Theory]
    [InlineData(0, KekaOutcome.Filled)]
    [InlineData(10, KekaOutcome.SignInNeeded)]
    [InlineData(11, KekaOutcome.PageChanged)]
    [InlineData(12, KekaOutcome.PythonMissing)]
    [InlineData(14, KekaOutcome.PlaywrightMissing)]
    [InlineData(99, KekaOutcome.Failed)]
    [InlineData(-1, KekaOutcome.Failed)]
    public void MapsExitCodes(int code, KekaOutcome expected) => Assert.Equal(expected, KekaResult.FromExitCode(code));

    [Fact]
    public void ProblemsAreErrorsThatStayAWhileAndSuccessIsShort()
    {
        var now = DateTimeOffset.Now;
        var login = KekaResult.Notch(KekaOutcome.SignInNeeded, now);
        Assert.Equal(NotchTone.Error, login.Tone);
        Assert.Equal("Keka: sign in needed", login.Text);
        Assert.True(login.ExpiresAt > now.AddMinutes(30));
        var ok = KekaResult.Notch(KekaOutcome.Filled, now);
        Assert.Equal(NotchTone.Success, ok.Tone);
        Assert.True(ok.ExpiresAt < now.AddMinutes(1));
        Assert.All(Enum.GetValues<KekaOutcome>(), o => Assert.InRange(KekaResult.Notch(o, now).Text.Length, 1, NotchMessage.MaxLength));
    }

    [Theory]
    [InlineData("https://conscendo.keka.com/#/me/timesheet", true)]
    [InlineData("  https://a.keka.com/x ", true)]
    [InlineData("http://a.keka.com", false)]
    [InlineData("conscendo.keka.com", false)]
    [InlineData("", false)]
    [InlineData("javascript:alert(1)", false)]
    public void ValidatesTheUrl(string url, bool ok) => Assert.Equal(ok, KekaResult.ValidateUrl(url) is null);
}
