using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class NotchSuggestionTests
{
    private static string[] Inserts(string input) => NotchSuggestions.For(input).Items.Select(s => s.Insert).ToArray();

    [Theory]
    [InlineData("")]
    [InlineData("buy milk")]
    [InlineData("say /notch hi")]
    [InlineData("/other thing")]
    [InlineData("/x")]
    public void OffersNothingForOrdinaryTextAndOtherCommands(string input) =>
        Assert.Empty(NotchSuggestions.For(input).Items);

    [Theory]
    [InlineData("/")]
    [InlineData("/n")]
    [InlineData("/no")]
    [InlineData("/NOTCH")]
    public void OffersTheCommandWhileItsWordIsBeingTyped(string input) =>
        Assert.Equal(new[] { "/notch" }, Inserts(input));

    [Fact]
    public void AfterTheCommandItOffersEveryTimerAndTone()
    {
        Assert.Equal(new[] { "10s", "30s", "15m", "30m", "1h", "2h", "pin", "ok:", "warn:", "error:", "info:" }, Inserts("/notch "));
        Assert.Contains("clears", NotchSuggestions.For("/notch ").Hint);
        Assert.Contains("5 seconds", NotchSuggestions.For("/notch ").Hint); // says what happens with no timer
    }

    [Fact]
    public void NarrowsByWhatHasBeenTyped()
    {
        Assert.Equal(new[] { "30s", "30m" }, Inserts("/notch 3"));
        Assert.Equal(new[] { "30s", "30m" }, Inserts("/notch 30"));
        Assert.Equal(new[] { "warn:" }, Inserts("/notch wa"));
        Assert.Equal(new[] { "10s", "15m", "1h" }, Inserts("/notch 1"));
        Assert.Equal(new[] { "error:" }, Inserts("/notch E"));
    }

    [Fact]
    public void OffersOnlyWhatIsNotUsedYet()
    {
        Assert.Equal(new[] { "ok:", "warn:", "error:", "info:" }, Inserts("/notch 30m "));
        Assert.Equal(new[] { "10s", "30s", "15m", "30m", "1h", "2h", "pin" }, Inserts("/notch warn: "));
        Assert.Empty(Inserts("/notch 30m warn: "));
        Assert.Empty(Inserts("/notch warn: 30m "));
    }

    [Fact]
    public void StopsOnceTheMessageItselfIsBeingTyped()
    {
        Assert.Empty(Inserts("/notch Deploying")); // the first word is not an option
        Assert.Empty(Inserts("/notch Deploying v2"));
        Assert.Empty(Inserts("/notch 30m Deploying v2"));
        Assert.Empty(Inserts("/notch zzz"));
    }

    [Fact]
    public void ApplyingASuggestionKeepsWhatWasTypedAndAddsASpace()
    {
        var set = NotchSuggestions.For("/no");
        Assert.Equal("/notch ", set.Apply(set.Items[0]));

        set = NotchSuggestions.For("/notch 3");
        Assert.Equal("/notch 30s ", set.Apply(set.Items[0]));
        Assert.Equal("/notch 30m ", set.Apply(set.Items[1]));

        set = NotchSuggestions.For("/notch 30m wa");
        Assert.Equal("/notch 30m warn: ", set.Apply(set.Items[0]));
    }

    [Fact]
    public void PickingEverySuggestionInTurnBuildsAValidCommand()
    {
        var text = "/";
        text = Pick(text, "/notch");
        text = Pick(text, "30m");
        text = Pick(text, "warn:");
        Assert.Equal("/notch 30m warn: ", text);
        var (tone, duration, rest) = NotchMessage.ParseCommandText(text["/notch".Length..] + "Server down");
        Assert.Equal("warn", tone);
        Assert.Equal(TimeSpan.FromMinutes(30), duration);
        Assert.Equal("Server down", rest);
    }

    [Fact]
    public void PinIsATimerChoiceToo()
    {
        Assert.Equal(new[] { "pin" }, Inserts("/notch p"));
        Assert.Equal(new[] { "ok:", "warn:", "error:", "info:" }, Inserts("/notch pin ")); // pin counts as the timer
        Assert.Empty(Inserts("/notch pin warn: "));
        var set = NotchSuggestions.For("/notch pi");
        Assert.Equal("/notch pin ", set.Apply(set.Items[0]));
    }

    [Fact]
    public void SecondsAreSuggestedAndRecognisedAsATimer()
    {
        Assert.Equal(new[] { "ok:", "warn:", "error:", "info:" }, Inserts("/notch 30s ")); // 30s counts as the timer
        Assert.Empty(Inserts("/notch 30s warn: "));
    }

    [Fact]
    public void HintsGuideTheNextStep()
    {
        Assert.Contains("tone", NotchSuggestions.For("/notch 30m ").Hint);
        Assert.Contains("timer", NotchSuggestions.For("/notch warn: ").Hint);
        Assert.Contains("message", NotchSuggestions.For("/notch 30m warn: ").Hint);
        Assert.Null(NotchSuggestions.For("/notch 3").Hint);
    }

    private static string Pick(string text, string insert)
    {
        var set = NotchSuggestions.For(text);
        return set.Apply(set.Items.Single(s => s.Insert == insert));
    }
}

public class SuggestionCursorTests
{
    [Theory]
    [InlineData(-1, 4, 1, 0)]   // nothing highlighted: Down takes the first
    [InlineData(-1, 4, -1, 3)]  // Up takes the last
    [InlineData(0, 4, 1, 1)]
    [InlineData(2, 4, 1, 3)]
    [InlineData(3, 4, 1, 0)]    // wraps forward
    [InlineData(0, 4, -1, 3)]   // wraps back
    [InlineData(1, 4, -1, 0)]
    [InlineData(0, 1, 1, 0)]    // a single suggestion stays put
    [InlineData(0, 0, 1, -1)]   // nothing to pick
    [InlineData(-1, 0, -1, -1)]
    [InlineData(9, 4, 1, 0)]    // a stale index (the list shrank) starts over
    public void MovesAndWraps(int current, int count, int delta, int expected) =>
        Assert.Equal(expected, NotchSuggestionSet.Move(current, count, delta));
}
