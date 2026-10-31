using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class NotchCommandTests
{
    private static (string?, int?, string) T(string? tone, int? minutes, string text) => (tone, minutes, text);

    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData("Deploying", null, null, "Deploying")]
    [InlineData("30m Focus time", null, 30, "Focus time")]
    [InlineData("2h Focus time", null, 120, "Focus time")]
    [InlineData("30M focus", null, 30, "focus")]
    [InlineData("warn: Server down", "warn", null, "Server down")]
    [InlineData("30m warn: Server down", "warn", 30, "Server down")]
    [InlineData("warn: 30m Server down", "warn", 30, "Server down")]
    [InlineData("ok:done", "ok", null, "done")]
    [InlineData("  45m   error :  Disk full ", "error", 45, "Disk full")]
    public void ReadsDurationAndToneInEitherOrder(string payload, string? tone, int? minutes, string text) =>
        Assert.Equal(T(tone, minutes, text), NotchMessage.ParseCommandText(payload));

    [Theory]
    [InlineData("meeting at 10:30", "meeting at 10:30")]       // a colon that is not a tone
    [InlineData("30 minutes of focus", "30 minutes of focus")] // a number that is not a timer token
    [InlineData("30mins left", "30mins left")]                 // "30m" must be a whole word
    [InlineData("purple: nope", "purple: nope")]
    [InlineData("deploy 30m", "deploy 30m")]                   // options only count at the start
    public void LeavesOrdinaryTextAlone(string payload, string text) =>
        Assert.Equal(T(null, null, text), NotchMessage.ParseCommandText(payload));

    [Fact]
    public void OnlyOneTimerAndOneToneAreTaken()
    {
        Assert.Equal(T(null, 30, "10m left"), NotchMessage.ParseCommandText("30m 10m left"));
        Assert.Equal(T("warn", null, "ok: fine"), NotchMessage.ParseCommandText("warn: ok: fine"));
    }

    [Fact]
    public void ATimerWithoutTextIsAnErrorNotAClear()
    {
        var (tone, minutes, text) = NotchMessage.ParseCommandText("30m");
        Assert.Equal(T(null, 30, ""), T(tone, minutes, text));
        Assert.NotNull(NotchMessage.TryCreate(text, tone, minutes, Now, out _));
    }

    [Fact]
    public void TheTimerSetsTheExpiry()
    {
        var (tone, minutes, text) = NotchMessage.ParseCommandText("90m ok: Build running");
        Assert.Null(NotchMessage.TryCreate(text, tone, minutes, Now, out var m));
        Assert.Equal(NotchTone.Success, m!.Tone);
        Assert.Equal(Now.AddMinutes(90), m.ExpiresAt);
    }

    [Fact]
    public void ATimerOverADayIsRefused()
    {
        var (tone, minutes, text) = NotchMessage.ParseCommandText("25h Too long");
        Assert.Equal(1500, minutes);
        Assert.NotNull(NotchMessage.TryCreate(text, tone, minutes, Now, out var m));
        Assert.Null(m);
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
