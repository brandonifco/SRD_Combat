namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="PlayMode.HoverDelayElapsed"/> (#304) — the latency half of the tooltip
/// issue. The review that filed it measured the old delay at two seconds and called it
/// "an eternity mid-fight"; #726/#728 made the per-hover highlight cost sub-millisecond
/// (<c>RefreshAfterAction</c> down to one <see cref="MovementRules.Reachable"/> call),
/// which is what the old delay was covering for, so it drops to about half a second.
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
