using Nyvora.Core.Animation;
using Xunit;

namespace Nyvora.Tests;

public class ExpandedSizingTests
{
    private static readonly SizeD Screen = new(1500, 900);

    [Fact]
    public void ClampEnforcesMinimumAndScreen()
    {
        Assert.Equal(ExpandedSizing.Min, ExpandedSizing.Clamp(new SizeD(10, 10), Screen));
        Assert.Equal(Screen, ExpandedSizing.Clamp(new SizeD(5000, 5000), Screen));
        Assert.Equal(new SizeD(600, 500), ExpandedSizing.Clamp(new SizeD(600, 500), Screen));
    }

    [Fact]
    public void ClampHandlesNonFiniteAndTinyScreens()
    {
        Assert.Equal(ExpandedSizing.Min, ExpandedSizing.Clamp(new SizeD(double.NaN, double.PositiveInfinity), Screen));
        // A screen smaller than the minimum must not throw; the minimum wins.
        Assert.Equal(ExpandedSizing.Min, ExpandedSizing.Clamp(new SizeD(500, 500), new SizeD(300, 200)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 400)]
    [InlineData(500, 0)]
    [InlineData(double.NaN, double.NaN)]
    public void FromSettingsFallsBackToDefault(double w, double h)
    {
        Assert.Equal(PillAnimator.DefaultExpandedSize, ExpandedSizing.FromSettings(w, h, Screen));
    }

    [Fact]
    public void FromSettingsUsesAndClampsSavedSize()
    {
        Assert.Equal(new SizeD(640, 520), ExpandedSizing.FromSettings(640, 520, Screen));
        Assert.Equal(new SizeD(1500, 900), ExpandedSizing.FromSettings(4000, 4000, Screen)); // saved on a bigger display
        Assert.Equal(ExpandedSizing.Min, ExpandedSizing.FromSettings(50, 50, Screen));
    }

    [Fact]
    public void ResizeGrowsWidthSymmetricallyAndHeightDownwards()
    {
        var start = new SizeD(420, 400);
        Assert.Equal(new SizeD(520, 450), ExpandedSizing.Resize(start, 50, 50, Screen));
        Assert.Equal(new SizeD(420, 400), ExpandedSizing.Resize(start, 0, 0, Screen));
    }

    [Fact]
    public void ResizeStopsAtLimits()
    {
        var start = new SizeD(420, 400);
        Assert.Equal(ExpandedSizing.Min, ExpandedSizing.Resize(start, -300, -300, Screen));
        Assert.Equal(Screen, ExpandedSizing.Resize(start, 2000, 2000, Screen));
    }

    [Fact]
    public void ExpansionProgressUsesTheCurrentExpandedSize()
    {
        var big = new SizeD(700, 600);
        Assert.Equal(1, PillAnimator.ExpansionProgress(big, big));
        Assert.True(PillAnimator.ExpansionProgress(PillAnimator.DefaultExpandedSize, big) < 1);
    }
}
