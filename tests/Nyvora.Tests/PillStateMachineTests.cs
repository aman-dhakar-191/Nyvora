using Nyvora.Core.State;
using Xunit;

namespace Nyvora.Tests;

public class PillStateMachineTests
{
    [Fact]
    public void StartsResting()
    {
        Assert.Equal(PillState.Resting, new PillStateMachine().State);
    }

    [Fact]
    public void FullCycle()
    {
        var m = new PillStateMachine();

        Assert.True(m.RequestExpand());
        Assert.Equal(PillState.Expanding, m.State);
        Assert.True(m.CompleteAnimation(m.Generation));
        Assert.Equal(PillState.Expanded, m.State);

        Assert.True(m.RequestCollapse());
        Assert.Equal(PillState.Collapsing, m.State);
        Assert.True(m.CompleteAnimation(m.Generation));
        Assert.Equal(PillState.Resting, m.State);
    }

    [Fact]
    public void InvalidRequestsAreRejectedWithoutChangingState()
    {
        var m = new PillStateMachine();
        Assert.False(m.RequestCollapse());
        Assert.Equal(PillState.Resting, m.State);

        m.RequestExpand();
        Assert.False(m.RequestExpand());
        Assert.Equal(PillState.Expanding, m.State);

        m.CompleteAnimation(m.Generation);
        Assert.False(m.RequestExpand());
        Assert.Equal(PillState.Expanded, m.State);
    }

    [Fact]
    public void CollapseInterruptsExpansionAndExpandInterruptsCollapse()
    {
        var m = new PillStateMachine();
        m.RequestExpand();
        Assert.True(m.RequestCollapse());
        Assert.Equal(PillState.Collapsing, m.State);
        Assert.True(m.RequestExpand());
        Assert.Equal(PillState.Expanding, m.State);
    }

    [Fact]
    public void StaleCompletionFromInterruptedAnimationIsIgnored()
    {
        var m = new PillStateMachine();
        m.RequestExpand();
        var stale = m.Generation;
        m.RequestCollapse();

        Assert.False(m.CompleteAnimation(stale));
        Assert.Equal(PillState.Collapsing, m.State);
    }

    [Fact]
    public void CompletionIsIgnoredWhenNothingIsAnimating()
    {
        var m = new PillStateMachine();
        Assert.False(m.CompleteAnimation(m.Generation));

        m.RequestExpand();
        var gen = m.Generation;
        m.CompleteAnimation(gen);
        Assert.False(m.CompleteAnimation(gen));
        Assert.Equal(PillState.Expanded, m.State);
    }

    [Fact]
    public void RapidToggleNeverLeavesInconsistentState()
    {
        var m = new PillStateMachine();
        for (var i = 0; i < 1000; i++)
        {
            if (i % 2 == 0) m.RequestExpand(); else m.RequestCollapse();
        }

        Assert.True(m.State is PillState.Expanding or PillState.Collapsing);
        Assert.True(m.CompleteAnimation(m.Generation));
        Assert.True(m.State is PillState.Expanded or PillState.Resting);
    }

    [Fact]
    public void RaisesStateChanged()
    {
        var m = new PillStateMachine();
        var seen = new List<(PillState, PillState)>();
        m.StateChanged += (a, b) => seen.Add((a, b));

        m.RequestExpand();
        m.CompleteAnimation(m.Generation);

        Assert.Equal(
            new[] { (PillState.Resting, PillState.Expanding), (PillState.Expanding, PillState.Expanded) },
            seen);
    }
}
