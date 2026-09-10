using Godot;
using SRDCombat.Core.Combat;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The three pure decisions behind the path preview's wiring (#303) — extracted so a
/// Codex-review-found regression in any of them has a seam this project's own
/// convention says it should: pinned here the same way
/// <see cref="PlayMode.HoverDelayElapsed"/> already is, without a live Godot node.
/// <see cref="Vector2"/> and <see cref="GridPosition"/> are both plain managed
/// structs, safe to construct outside a running engine.
/// </summary>
public class PathPreviewWiringTests
{
    // ---- TrackedPointer (PR #731 round 2 review, defect #1: a refresh must read
    // wherever the pointer actually is, never a pixel the tooltip's own jitter gate
    // left behind) -----------------------------------------------------------------

    [Fact]
    public void TrackedPointerAlwaysAdoptsTheMotionPosition() =>
        Assert.Equal(
            new Vector2(40, 40),
            PlayMode.TrackedPointer(new Vector2(10, 10), new Vector2(40, 40)));

    [Fact]
    public void TrackedPointerAdoptsEvenASubJitterMove() =>
        // The exact shape Codex's round 2 review named: a pointer that drifts two
        // pixels — well under HoverJitterPixels (3) — from one grid square into an
        // adjacent one must still be tracked at its new position, or a later refresh
        // (a routed keyboard action, a camera change, RefreshAfterAction) reads the
        // old square's pixel and restores its route for a click that is about to
        // land on the new one.
        Assert.Equal(
            new Vector2(12, 10),
            PlayMode.TrackedPointer(new Vector2(10, 10), new Vector2(12, 10)));

    [Fact]
    public void TrackedPointerIgnoresThePreviousPositionEntirely() =>
        // However far away the previous pointer was, the tracked value is always the
        // new motion sample — there is no distance at which this reverts to the old
        // pixel, which is precisely the bug a reintroduced jitter gate here would be.
        Assert.Equal(
            new Vector2(500, 500),
            PlayMode.TrackedPointer(new Vector2(0, 0), new Vector2(500, 500)));

    // ---- PreviewSquareChanged (PR #731 round 1 review, defect #2: the preview must
    // not inherit the tooltip's pixel-jitter gate) ----------------------------------

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

    // ---- PreviewMayShow (PR #731 round 1 review, defect #4: the preview must not
    // show while a click would not actually move; round 2 review, defect: nor while
    // the pointer is over the fixed chrome) -----------------------------------------

    [Fact]
    public void BoardFocusWithNothingArmedAndNoOverlayMayShow() =>
        Assert.True(PlayMode.PreviewMayShow(focusIsBoard: true, armed: false, overOverlay: false));

    [Fact]
    public void BoardFocusWithSomethingArmedMayNotShow() =>
        // The exact shape Codex's round 1 review named: Attack armed via a cold Tab,
        // or a spell armed off a menu row, both leave focus on something other than
        // plain Board — but this is the predicate itself, tested directly against
        // "armed", so a future caller cannot pass Board-with-Armed-true and still show
        // a route for a click that would clear targeting instead of walking.
        Assert.False(PlayMode.PreviewMayShow(focusIsBoard: true, armed: true, overOverlay: false));

    [Fact]
    public void NonBoardFocusMayNotShowEvenUnarmed() =>
        Assert.False(PlayMode.PreviewMayShow(focusIsBoard: false, armed: false, overOverlay: false));

    [Fact]
    public void NonBoardFocusWithSomethingArmedMayNotShow() =>
        Assert.False(PlayMode.PreviewMayShow(focusIsBoard: false, armed: true, overOverlay: false));

    [Fact]
    public void OverOverlayMayNotShowEvenOnBoardUnarmed() =>
        // The exact shape Codex's round 2 review named: a reachable square sitting
        // under the initiative/log panel or the bottom banner strip must not light a
        // route, because a click there hits the chrome — RouteClick's own OverOverlay
        // check — never the square underneath it.
        Assert.False(PlayMode.PreviewMayShow(focusIsBoard: true, armed: false, overOverlay: true));

    [Fact]
    public void OverOverlayAndArmedMayNotShow() =>
        Assert.False(PlayMode.PreviewMayShow(focusIsBoard: true, armed: true, overOverlay: true));

    [Fact]
    public void PreviewMayShowReflectsArmedStateEachCallIndependently() =>
        // #303 defect, PR #731 round 2 review: "mouse dismissal doesn't restore the
        // preview" — ClearPending calls UpdatePreviewPath unconditionally, with no
        // "skip if the square looks the same as last time" guard, because this gate's
        // answer depends only on the arguments it is given right now, never on
        // anything remembered from an earlier call. Arming and then disarming on the
        // very same square must flip the answer both times, in sequence, or a mouse
        // click that returns focus to Board finds nothing here to restore it.
        Assert.Multiple(
            () => Assert.False(PlayMode.PreviewMayShow(focusIsBoard: true, armed: true, overOverlay: false)),
            () => Assert.True(PlayMode.PreviewMayShow(focusIsBoard: true, armed: false, overOverlay: false)));
}
