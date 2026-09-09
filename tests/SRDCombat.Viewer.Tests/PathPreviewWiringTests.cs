using SRDCombat.Core.Combat;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The two pure decisions behind the path preview's wiring (#303, PR #731 round 1
/// review) — extracted so a Codex-review-found regression in either has a seam this
/// project's own convention says it should: pinned here the same way
/// <see cref="PlayMode.HoverDelayElapsed"/> already is, without a live Godot node.
/// </summary>
public class PathPreviewWiringTests
{
    // ---- PreviewSquareChanged (defect #2: the preview must not inherit the tooltip's
    // pixel-jitter gate) --------------------------------------------------------------

    [Fact]
    public void TheSameSquareTwiceIsNotAChange() =>
        Assert.False(PlayMode.PreviewSquareChanged(new GridPosition(3, 4), new GridPosition(3, 4)));

    [Fact]
    public void BothNullIsNotAChange() =>
        Assert.False(PlayMode.PreviewSquareChanged(null, null));

    [Fact]
    public void AnAdjacentSquareIsAChange() =>
        // The exact shape Codex's review named: a pointer that crosses a grid line in
        // a move well under HoverJitterPixels is still looking at a different
        // destination the instant it happens — this has no notion of pixel distance
        // at all, only the two squares.
        Assert.True(PlayMode.PreviewSquareChanged(new GridPosition(3, 4), new GridPosition(4, 4)));

    [Fact]
    public void LeavingTheBoardIsAChange() =>
        Assert.True(PlayMode.PreviewSquareChanged(new GridPosition(3, 4), null));

    [Fact]
    public void EnteringTheBoardIsAChange() =>
        Assert.True(PlayMode.PreviewSquareChanged(null, new GridPosition(3, 4)));

    // ---- PreviewMayShow (defect #4: the preview must not show while a click would
    // not actually move) ---------------------------------------------------------------

    [Fact]
    public void BoardFocusWithNothingArmedMayShow() =>
        Assert.True(PlayMode.PreviewMayShow(focusIsBoard: true, armed: false));

    [Fact]
    public void BoardFocusWithSomethingArmedMayNotShow() =>
        // The exact shape Codex's review named: Attack armed via a cold Tab, or a
        // spell armed off a menu row, both leave focus on something other than plain
        // Board — but this is the predicate itself, tested directly against "armed",
        // so a future caller cannot pass Board-with-Armed-true and still show a route
        // for a click that would clear targeting instead of walking.
        Assert.False(PlayMode.PreviewMayShow(focusIsBoard: true, armed: true));

    [Fact]
    public void NonBoardFocusMayNotShowEvenUnarmed() =>
        Assert.False(PlayMode.PreviewMayShow(focusIsBoard: false, armed: false));

    [Fact]
    public void NonBoardFocusWithSomethingArmedMayNotShow() =>
        Assert.False(PlayMode.PreviewMayShow(focusIsBoard: false, armed: true));
}
