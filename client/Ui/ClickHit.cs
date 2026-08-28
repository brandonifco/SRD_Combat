using SRDCombat.Core.Combat;

namespace SRDCombat.Viewer.Ui;

/// <summary>
/// What one pixel hit, before anything decides what that means.
/// </summary>
/// <remarks>
/// <para>
/// <b>Independent facts, not a single classification</b> (#503, qc review round 1). This
/// type used to carry one pre-chosen <c>HitKind</c> — <c>PlayMode</c>'s own hit-testing
/// order had already decided a menu row beats a button before <see cref="PlayFocusRouter"/>
/// ever saw the click, which meant the router could not actually make the step-5-vs-step-6
/// choice the slice exists to own: reordering the router's checks changed nothing, because
/// the node had already thrown the losing fact away. Every field below is instead tested on
/// its own — two regions that can both be visually live at once (an open menu's rows and
/// the button strip beneath it) are both reported, and <see cref="PlayFocusRouter.RouteClick"/>
/// is the one place that picks a winner.
/// </para>
/// <para>
/// The same reasoning applies to the interlude fields: whether the shop is open, or
/// available, or the phase is the interlude at all, are facts the router reads from
/// <c>RouteContext</c> and the focus stack, not gates <c>PlayMode.HitTest</c> applies before
/// deciding whether a region is even worth testing. <c>HitTest</c> tests every rect it
/// knows about unconditionally; a stale rect from a screen that is not currently showing
/// simply produces a fact the router's own state check declines to honour.
/// </para>
/// <para>
/// The same division <see cref="ClientInput"/> draws for the keyboard: the node computes
/// what the pixel hit, and the router decides what that means given what has the player's
/// attention. A plain record rather than anything Godot-owned, so it is constructible with
/// no display and no running engine.
/// </para>
/// </remarks>
/// <param name="ShopBack">Whether the pixel hit the stall's back button.</param>
/// <param name="ShopRow">
/// Which row of the stall's offers the pixel hit, or null if none. Meaningful only while
/// the shop is the top of the focus stack — the router's call, not this type's.
/// </param>
/// <param name="ShopOpen">Whether the pixel hit the button that opens the stall.</param>
/// <param name="Continue">Whether the pixel hit the button that starts the next fight.</param>
/// <param name="MenuRow">
/// Which row of whichever menu is on top of the focus stack the pixel hit, or null if none
/// (including when no row menu is open at all, since then no row rect exists to hit).
/// </param>
/// <param name="Button">Which button of the turn's button strip the pixel hit, or null.</param>
/// <param name="Square">The board square under the pixel, or null off the board.</param>
/// <param name="OverOverlay">
/// Whether the pixel sits on the fixed chrome — the initiative-and-log panel, the banner
/// strip. Computed independently of every other field: an armed action resolving against
/// the chrome cancels regardless of what, if anything, else the pixel also landed on.
/// </param>
internal readonly record struct ClickHit(
    bool ShopBack,
    int? ShopRow,
    bool ShopOpen,
    bool Continue,
    int? MenuRow,
    int? Button,
    GridPosition? Square,
    bool OverOverlay);
