using SRDCombat.Core.Combat;

namespace SRDCombat.Viewer.Ui;

/// <summary>The kind of thing one pixel landed on.</summary>
/// <remarks>
/// One member per region <c>PlayMode.HandleClick</c> used to test for, in the order it
/// tested for them (#503, <c>docs/2026-08-26-playmode-refactor-design.md</c> §10 S4). The
/// node still does the rect testing — <c>Rect2</c> is a safe Godot struct and this is
/// layout, not decision — but which region wins, and what winning it means, moves to
/// <see cref="PlayFocusRouter"/> alongside the keyboard order it already owns.
/// </remarks>
internal enum HitKind
{
    /// <summary>A row of whichever menu is on top of the focus stack.</summary>
    MenuRow,

    /// <summary>A row of the turn's button strip.</summary>
    Button,

    /// <summary>A row of the merchant's stall.</summary>
    ShopRow,

    /// <summary>The stall's back button.</summary>
    ShopBack,

    /// <summary>The button that opens the stall.</summary>
    ShopOpen,

    /// <summary>The button that starts the next fight.</summary>
    Continue,

    /// <summary>The fixed chrome — the initiative-and-log panel, the banner strip.</summary>
    Overlay,

    /// <summary>A square of the board.</summary>
    Square,

    /// <summary>Nothing recognised — the interlude screen outside every button and row.</summary>
    Nothing,
}

/// <summary>
/// What one pixel hit, before anything decides what that means.
/// </summary>
/// <remarks>
/// <b>Not a decision, only a classification.</b> The same division <see cref="ClientInput"/>
/// draws for the keyboard: the node computes what the pixel hit, and
/// <see cref="PlayFocusRouter"/> decides what that means given what has the player's
/// attention. A plain record rather than anything Godot-owned, so it is constructible with
/// no display and no running engine.
/// </remarks>
/// <param name="Kind">The most specific region the pixel landed on.</param>
/// <param name="Index">
/// Which row or button, for <see cref="HitKind.MenuRow"/>, <see cref="HitKind.Button"/> and
/// <see cref="HitKind.ShopRow"/>. Unused otherwise.
/// </param>
/// <param name="Square">
/// The board square under the pixel, or null off the board — computed independently of
/// <paramref name="Kind"/>, because an armed action resolving against the board reads this
/// directly rather than waiting on <paramref name="Kind"/>'s own classification (S4 step 4).
/// </param>
/// <param name="OverOverlay">
/// Whether the pixel sits on the fixed chrome, computed independently of
/// <paramref name="Kind"/> for the same reason as <paramref name="Square"/>: an armed
/// action resolving against the chrome cancels regardless of which chrome element it was.
/// </param>
internal readonly record struct ClickHit(
    HitKind Kind,
    int Index,
    GridPosition? Square,
    bool OverOverlay);
