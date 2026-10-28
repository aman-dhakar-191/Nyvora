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
