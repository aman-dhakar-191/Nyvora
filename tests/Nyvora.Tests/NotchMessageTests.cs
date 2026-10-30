using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class NotchMessageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nyvora-notch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void CreatesATrimmedMessageWithDefaults()
    {
        Assert.Null(NotchMessage.TryCreate("  Deploying  ", null, null, Now, out var m));
        Assert.Equal("Deploying", m!.Text);
        Assert.Equal(NotchTone.Info, m.Tone);
        Assert.Null(m.ExpiresAt);
        Assert.True(m.IsActive(Now));
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData("ok", "purple", null)]
    [InlineData("ok", null, 0)]
    [InlineData("ok", null, 1441)]
    public void RejectsBadInput(string text, string? tone, int? minutes)
    {
        Assert.NotNull(NotchMessage.TryCreate(text, tone, minutes, Now, out var m));
        Assert.Null(m);
    }

    [Fact]
    public void RejectsTextLongerThanTheNotchFits()
    {
        Assert.NotNull(NotchMessage.TryCreate(new string('x', NotchMessage.MaxLength + 1), null, null, Now, out _));
        Assert.Null(NotchMessage.TryCreate(new string('x', NotchMessage.MaxLength), null, null, Now, out _));
    }

    [Theory]
    [InlineData("info", NotchTone.Info)]
    [InlineData("OK", NotchTone.Success)]
    [InlineData("success", NotchTone.Success)]
    [InlineData("Warn", NotchTone.Warning)]
    [InlineData("warning", NotchTone.Warning)]
    [InlineData("error", NotchTone.Error)]
    [InlineData("", NotchTone.Info)]
    public void ParsesTones(string text, NotchTone expected)
    {
        Assert.True(NotchMessage.TryParseTone(text, out var tone));
        Assert.Equal(expected, tone);
    }

    [Fact]
    public void ExpiresAfterTheGivenMinutes()
    {
        Assert.Null(NotchMessage.TryCreate("Focus", null, 30, Now, out var m));
        Assert.True(m!.IsActive(Now.AddMinutes(29)));
        Assert.False(m.IsActive(Now.AddMinutes(30)));
        Assert.False(new NotchMessage().IsActive(Now)); // empty means none
    }

    [Theory]
    [InlineData("/notch Deploying", true, "Deploying")]
    [InlineData("/NOTCH   Deploying  ", true, "Deploying")]
    [InlineData("/notch", true, "")]
    [InlineData("  /notch  ", true, "")]
    [InlineData("/notchy thing", false, "")]
    [InlineData("buy milk", false, "")]
    [InlineData("say /notch hi", false, "")]
    public void ParsesTheAddBoxCommand(string input, bool isCommand, string payload)
    {
        Assert.Equal(isCommand, NotchMessage.TryParseCommand(input, out var p));
        Assert.Equal(payload, p);
    }

    [Theory]
    [InlineData("warn: server down", "warn", "server down")]
    [InlineData("ok:done", "ok", "done")]
    [InlineData("meeting at 10:30", null, "meeting at 10:30")]
    [InlineData("plain text", null, "plain text")]
    [InlineData("purple: nope", null, "purple: nope")]
    public void SplitsAnOptionalToneOffTheText(string payload, string? tone, string text)
    {
        Assert.Equal((tone, text), NotchMessage.SplitTone(payload));
    }

    [Fact]
    public void SavesAndLoadsAndClears()
    {
        var path = Path.Combine(_dir, NotchMessageStore.FileName);
        Assert.Null(NotchMessageStore.Load(path, Now)); // no file yet

        Assert.Null(NotchMessage.TryCreate("Build passed", "ok", null, Now, out var m));
        NotchMessageStore.Save(path, m!);
        var loaded = NotchMessageStore.Load(path, Now);
        Assert.Equal("Build passed", loaded!.Text);
        Assert.Equal(NotchTone.Success, loaded.Tone);

        NotchMessageStore.Clear(path);
        Assert.Null(NotchMessageStore.Load(path, Now));
    }

    [Fact]
    public void AnExpiredOrCorruptFileMeansNoMessage()
    {
        var path = Path.Combine(_dir, NotchMessageStore.FileName);
        Assert.Null(NotchMessage.TryCreate("Soon gone", null, 5, Now, out var m));
        NotchMessageStore.Save(path, m!);
        Assert.NotNull(NotchMessageStore.Load(path, Now.AddMinutes(4)));
        Assert.Null(NotchMessageStore.Load(path, Now.AddMinutes(6)));

        File.WriteAllText(path, "{ not json");
        Assert.Null(NotchMessageStore.Load(path, Now));
    }

    [Fact]
    public void ToneIsStoredAsReadableText()
    {
        var path = Path.Combine(_dir, NotchMessageStore.FileName);
        Assert.Null(NotchMessage.TryCreate("Careful", "warn", null, Now, out var m));
        NotchMessageStore.Save(path, m!);
        Assert.Contains("\"Warning\"", File.ReadAllText(path));
    }
}
