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
        Assert.Equal(new[] { "15m", "30m", "1h", "2h", "ok:", "warn:", "error:", "info:" }, Inserts("/notch "));
        Assert.Contains("clears", NotchSuggestions.For("/notch ").Hint);
    }

    [Fact]
    public void NarrowsByWhatHasBeenTyped()
    {
        Assert.Equal(new[] { "30m" }, Inserts("/notch 3"));
        Assert.Equal(new[] { "30m" }, Inserts("/notch 30"));
        Assert.Equal(new[] { "warn:" }, Inserts("/notch wa"));
        Assert.Equal(new[] { "15m", "1h" }, Inserts("/notch 1"));
        Assert.Equal(new[] { "error:" }, Inserts("/notch E"));
    }

    [Fact]
    public void OffersOnlyWhatIsNotUsedYet()
    {
        Assert.Equal(new[] { "ok:", "warn:", "error:", "info:" }, Inserts("/notch 30m "));
        Assert.Equal(new[] { "15m", "30m", "1h", "2h" }, Inserts("/notch warn: "));
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
        Assert.Equal("/notch 30m ", set.Apply(set.Items[0]));

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
        var (tone, minutes, rest) = NotchMessage.ParseCommandText(text["/notch".Length..] + "Server down");
        Assert.Equal("warn", tone);
        Assert.Equal(30, minutes);
        Assert.Equal("Server down", rest);
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
