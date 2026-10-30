using Nyvora.Core.Animation;
using Xunit;

namespace Nyvora.Tests;

public class PillAnimatorTests
{
    private static readonly TimeSpan D = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void StartsAtFromAndEndsAtTo()
    {
        var a = new PillAnimator(PillAnimator.RestingSize, PillAnimator.DefaultExpandedSize, D);
        Assert.Equal(PillAnimator.RestingSize, a.Sample(TimeSpan.Zero));
        Assert.Equal(PillAnimator.DefaultExpandedSize, a.Sample(D));
        Assert.Equal(PillAnimator.DefaultExpandedSize, a.Sample(D * 5));
    }

    [Fact]
    public void IsMonotonicWhileExpanding()
    {
        var a = new PillAnimator(PillAnimator.RestingSize, PillAnimator.DefaultExpandedSize, D);
        var last = 0.0;
        for (var ms = 0; ms <= 200; ms += 10)
        {
            var w = a.Sample(TimeSpan.FromMilliseconds(ms)).Width;
            Assert.True(w >= last);
            last = w;
        }
    }

    [Fact]
    public void CompletionTracksDuration()
    {
        var a = new PillAnimator(PillAnimator.RestingSize, PillAnimator.DefaultExpandedSize, D);
        Assert.False(a.IsComplete(TimeSpan.FromMilliseconds(199)));
        Assert.True(a.IsComplete(D));
    }

    [Fact]
    public void ExpansionProgressIsClamped()
    {
        Assert.Equal(0, PillAnimator.ExpansionProgress(PillAnimator.RestingSize, PillAnimator.DefaultExpandedSize));
        Assert.Equal(1, PillAnimator.ExpansionProgress(PillAnimator.DefaultExpandedSize, PillAnimator.DefaultExpandedSize));
        Assert.Equal(0, PillAnimator.ExpansionProgress(new SizeD(1, 1), PillAnimator.DefaultExpandedSize));
        Assert.Equal(1, PillAnimator.ExpansionProgress(new SizeD(9999, 1), PillAnimator.DefaultExpandedSize));
    }

    [Fact]
    public void RejectsNonPositiveDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PillAnimator(PillAnimator.RestingSize, PillAnimator.DefaultExpandedSize, TimeSpan.Zero));
    }
}

public class PillEaseTests
{
    private static readonly SizeD From = PillAnimator.DefaultExpandedSize;
    private static readonly SizeD To = PillAnimator.RestingSize;
    private static readonly TimeSpan Dur = TimeSpan.FromMilliseconds(240);

    [Fact]
    public void InOutStartsAndEndsExactly()
    {
        var a = new PillAnimator(From, To, Dur, PillEase.InOutCubic);
        Assert.Equal(From, a.Sample(TimeSpan.Zero));
        Assert.Equal(To, a.Sample(Dur));
    }

    [Fact]
    public void InOutNeverOvershootsAndShrinksMonotonically()
    {
        var a = new PillAnimator(From, To, Dur, PillEase.InOutCubic);
        var last = double.MaxValue;
        for (var ms = 0; ms <= 240; ms += 8)
        {
            var w = a.Sample(TimeSpan.FromMilliseconds(ms)).Width;
            Assert.InRange(w, To.Width - 1e-9, From.Width + 1e-9);
            Assert.True(w <= last + 1e-9);
            last = w;
        }
    }

    [Fact]
    public void InOutIsSlowerThanOutAtTheStart()
    {
        var inOut = new PillAnimator(From, To, Dur, PillEase.InOutCubic).Sample(TimeSpan.FromMilliseconds(30)).Width;
        var outC = new PillAnimator(From, To, Dur).Sample(TimeSpan.FromMilliseconds(30)).Width;
        Assert.True(inOut > outC); // still wider = hasn't moved as far
    }

    [Fact]
    public void CompactIsSmallerThanTheNormalMinimum()
    {
        Assert.True(PillAnimator.CompactSize.Width < ExpandedSizing.Min.Width);
        Assert.True(PillAnimator.CompactSize.Width > PillAnimator.RestingSize.Width);
    }
}
