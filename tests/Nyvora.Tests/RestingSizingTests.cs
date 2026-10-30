using Nyvora.Core.Animation;
using Xunit;

namespace Nyvora.Tests;

public class RestingSizingTests
{
    [Fact]
    public void ShortContentKeepsTheDefaultWidth() =>
        Assert.Equal(PillAnimator.RestingSize.Width, RestingSizing.Width(60, 1920));

    [Fact]
    public void WiderContentGrowsThePillByItsPadding() =>
        Assert.Equal(200 + 2 * RestingSizing.Padding, RestingSizing.Width(200, 1920));

    [Fact]
    public void NeverWiderThanTheCapOrTheScreen()
    {
        Assert.Equal(RestingSizing.MaxWidth, RestingSizing.Width(900, 1920));
        Assert.Equal(300, RestingSizing.Width(900, 300));
        Assert.Equal(PillAnimator.RestingSize.Width, RestingSizing.Width(900, 50)); // a tiny screen still gets the default
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-50)]
    public void NonsenseContentFallsBackToTheDefault(double content) =>
        Assert.Equal(PillAnimator.RestingSize.Width, RestingSizing.Width(content, 1920));

    [Fact]
    public void ProgressIsMeasuredFromTheActualRestingWidth()
    {
        var resting = new SizeD(260, 36);
        var expanded = new SizeD(420, 400);
        Assert.Equal(0, PillAnimator.ExpansionProgress(resting, expanded, resting)); // a wide resting pill is still "not expanded"
        Assert.Equal(1, PillAnimator.ExpansionProgress(expanded, expanded, resting));
        Assert.Equal(0.5, PillAnimator.ExpansionProgress(new SizeD(340, 200), expanded, resting), 6);
        Assert.Equal(1, PillAnimator.ExpansionProgress(expanded, expanded, new SizeD(420, 36))); // no range: treated as fully expanded
    }
}
