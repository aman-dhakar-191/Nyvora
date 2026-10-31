using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class NotchCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.FromHours(2));

    private static (string?, TimeSpan?, string) T(string? tone, int? seconds, string text) =>
        (tone, seconds is { } s ? TimeSpan.FromSeconds(s) : null, text);

    [Theory]
    [InlineData("Deploying", null, null, "Deploying")]
    [InlineData("30m Focus time", null, 1800, "Focus time")]
    [InlineData("2h Focus time", null, 7200, "Focus time")]
    [InlineData("30M focus", null, 1800, "focus")]
    [InlineData("30s Quick look", null, 30, "Quick look")]
    [InlineData("pin Deploying", null, 0, "Deploying")]          // pin: stays until cleared (a zero time)
    [InlineData("PIN warn: Careful", "warn", 0, "Careful")]
    [InlineData("warn: pin Careful", "warn", 0, "Careful")]
    [InlineData("10S go", null, 10, "go")]
    [InlineData("warn: Server down", "warn", null, "Server down")]
    [InlineData("30m warn: Server down", "warn", 1800, "Server down")]
    [InlineData("warn: 30s Server down", "warn", 30, "Server down")]
    [InlineData("ok:done", "ok", null, "done")]
    [InlineData("  45m   error :  Disk full ", "error", 2700, "Disk full")]
    public void ReadsDurationAndToneInEitherOrder(string payload, string? tone, int? seconds, string text) =>
        Assert.Equal(T(tone, seconds, text), NotchMessage.ParseCommandText(payload));

    [Theory]
    [InlineData("meeting at 10:30", "meeting at 10:30")]       // a colon that is not a tone
    [InlineData("30 minutes of focus", "30 minutes of focus")] // a number that is not a timer token
    [InlineData("30mins left", "30mins left")]                 // "30m" must be a whole word
    [InlineData("30 seconds left", "30 seconds left")]
    [InlineData("5sec left", "5sec left")]
    [InlineData("purple: nope", "purple: nope")]
    [InlineData("deploy 30m", "deploy 30m")]                   // options only count at the start
    public void LeavesOrdinaryTextAlone(string payload, string text) =>
        Assert.Equal(T(null, null, text), NotchMessage.ParseCommandText(payload));

    [Fact]
    public void OnlyOneTimerAndOneToneAreTaken()
    {
        Assert.Equal(T(null, 1800, "10m left"), NotchMessage.ParseCommandText("30m 10m left"));
        Assert.Equal(T("warn", null, "ok: fine"), NotchMessage.ParseCommandText("warn: ok: fine"));
    }

    [Fact]
    public void ATimerWithoutTextIsAnErrorNotAClear()
    {
        var (tone, duration, text) = NotchMessage.ParseCommandText("30m");
        Assert.Equal(T(null, 1800, ""), (tone, duration, text));
        Assert.NotNull(NotchMessage.TryCreateTimed(text, tone, duration, Now, out _));
    }

    [Theory]
    [InlineData("90m ok: Build running", 5400)]
    [InlineData("30s ok: Build running", 30)]
    [InlineData("5s ok: Build running", 5)]
    public void TheTimerSetsTheExpiry(string payload, int seconds)
    {
        var (tone, duration, text) = NotchMessage.ParseCommandText(payload);
        Assert.Null(NotchMessage.TryCreateTimed(text, tone, duration, Now, out var m));
        Assert.Equal(NotchTone.Success, m!.Tone);
        Assert.Equal(Now.AddSeconds(seconds), m.ExpiresAt);
    }

    [Theory]
    [InlineData("25h Too long")]
    [InlineData("4s Too short")]
    [InlineData("1s Too short")]
    public void ATimerOutsideFiveSecondsToADayIsRefused(string payload)
    {
        var (tone, duration, text) = NotchMessage.ParseCommandText(payload);
        var error = NotchMessage.TryCreateTimed(text, tone, duration, Now, out var m);
        Assert.Contains("5 seconds to 24 hours", error);
        Assert.Null(m);
    }

    [Fact]
    public void WithNoTimeTheMessageShowsBrieflyThenGoes()
    {
        var (tone, duration, text) = NotchMessage.ParseCommandText("Deploying v2");
        Assert.Null(duration); // nothing asked for
        Assert.Equal(NotchMessage.DefaultDuration, NotchMessage.Effective(duration));

        Assert.Null(NotchMessage.TryCreateTimed(text, tone, NotchMessage.Effective(duration), Now, out var m));
        Assert.Equal(Now + NotchMessage.DefaultDuration, m!.ExpiresAt);
        Assert.True(m.IsActive(Now.AddSeconds(4)));
        Assert.False(m.IsActive(Now.AddSeconds(6)));
    }

    [Fact]
    public void PinKeepsTheMessageUntilItIsCleared()
    {
        var (tone, duration, text) = NotchMessage.ParseCommandText("pin Deploying v2");
        Assert.Null(NotchMessage.Effective(duration));
        Assert.Null(NotchMessage.TryCreateTimed(text, tone, NotchMessage.Effective(duration), Now, out var m));
        Assert.Null(m!.ExpiresAt);
        Assert.True(m.IsActive(Now.AddDays(30)));
    }

    [Fact]
    public void ATimerIsUsedAsGiven()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), NotchMessage.Effective(TimeSpan.FromMinutes(30)));
        Assert.Equal(TimeSpan.FromSeconds(10), NotchMessage.Effective(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void TheDefaultIsInsideTheAllowedRange()
    {
        Assert.True(NotchMessage.DefaultDuration >= NotchMessage.MinDuration);
        Assert.True(NotchMessage.DefaultDuration <= NotchMessage.MaxDuration);
    }

    [Fact]
    public void SecondsExpireOnTime()
    {
        Assert.Null(NotchMessage.TryCreateTimed("Quick", null, TimeSpan.FromSeconds(30), Now, out var m));
        Assert.True(m!.IsActive(Now.AddSeconds(29)));
        Assert.False(m.IsActive(Now.AddSeconds(30)));
    }
}

public class ProcessStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(0.0, "Downloading v1.9.1 · 0%")]
    [InlineData(0.424, "Downloading v1.9.1 · 42%")]
    [InlineData(1.0, "Downloading v1.9.1 · 100%")]
    [InlineData(7.0, "Downloading v1.9.1 · 100%")]   // clamped
    [InlineData(-1.0, "Downloading v1.9.1 · 0%")]
    public void ShowsDownloadProgress(double progress, string expected) =>
        Assert.Equal(expected, ProcessStatus.Updating("v1.9.1", progress, "ignored").Text);

    [Fact]
    public void FallsBackToTheStepThenToAGenericLine()
    {
        Assert.Equal("Verifying download…", ProcessStatus.Updating("v1.9.1", null, " Verifying download… ").Text);
        Assert.Equal("Updating v1.9.1…", ProcessStatus.Updating("v1.9.1", null, null).Text);
        Assert.Equal("Updating v1.9.1…", ProcessStatus.Updating("v1.9.1", double.NaN, "").Text);
    }

    [Fact]
    public void ProgressMessagesStayUntilTheUpdateMovesOn()
    {
        var m = ProcessStatus.Updating("v1.9.1", 0.5, null);
        Assert.Null(m.ExpiresAt);
        Assert.True(m.IsActive(Now.AddHours(5)));
    }

    [Fact]
    public void FailuresAndSyncsComeAndGo()
    {
        var failed = ProcessStatus.UpdateFailed(Now);
        Assert.Equal(NotchTone.Error, failed.Tone);
        Assert.True(failed.IsActive(Now.AddSeconds(19)));
        Assert.False(failed.IsActive(Now.AddSeconds(21)));

        var synced = ProcessStatus.Synced(Now);
        Assert.Equal(NotchTone.Success, synced.Tone);
        Assert.True(synced.IsActive(Now.AddSeconds(4)));
        Assert.False(synced.IsActive(Now.AddSeconds(6)));
    }

    [Fact]
    public void LongTextIsShortenedToWhatTheNotchFits()
    {
        var m = ProcessStatus.Updating("v1.9.1", null, new string('x', 80));
        Assert.Equal(NotchMessage.MaxLength, m.Text.Length);
        Assert.EndsWith("…", m.Text);
    }
}
