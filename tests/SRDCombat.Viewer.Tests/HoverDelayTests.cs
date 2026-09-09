namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="PlayMode.HoverDelayElapsed"/> (#304) — the latency half of the tooltip
/// issue. The review that filed it measured the old delay at two seconds and called it
/// "an eternity mid-fight"; it drops to about half a second. The delay is a
/// rest-detection threshold, not a cost budget — the hover path reads a hint and
/// recomputes nothing — so nothing else had to change first.
/// Pinned here because <see cref="PlayMode.AdvanceHover"/> itself needs a live Godot
/// node (<c>_pointer</c>, <c>QueueRedraw</c>) and this decision is the one part of it
/// that does not.
/// </summary>
public class HoverDelayTests
{
    [Fact]
    public void AQuarterSecondRestShowsNothing() =>
        Assert.False(PlayMode.HoverDelayElapsed(0.25));

    [Fact]
    public void JustShortOfTheDelayShowsNothing() =>
        // Would have been well short of the *old* two-second delay too, so on its own
        // this does not distinguish the two — the next two facts do.
        Assert.False(PlayMode.HoverDelayElapsed(0.49));

    [Fact]
    public void ExactlyAtTheDelayShowsTheHint() =>
        Assert.True(PlayMode.HoverDelayElapsed(0.5));

    [Fact]
    public void PastTheDelayShowsTheHint() =>
        Assert.True(PlayMode.HoverDelayElapsed(0.75));

    [Fact]
    public void ARestThatUsedToBeTooShortNowShowsTheHint() =>
        // The behavioural claim of this slice: a hover that the *old* 2-second delay
        // would still have been sitting on now clears it. If this regresses to false,
        // the latency fix regressed with it.
        Assert.True(PlayMode.HoverDelayElapsed(1.0));
}
