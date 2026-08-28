namespace SRDCombat.Viewer.Ui;

/// <summary>
/// What Esc does from a given focus. One value per destination, so the cascade that
/// used to decide it by branch order is a property of the layer instead.
/// </summary>
/// <remarks>
/// The branch order this replaces lived in <c>PlayMode._UnhandledInput</c> and was read
/// top to bottom: shop, then outcome card, then anything armed, then quit. Nothing named
/// that order or held it in place — a modal added in the wrong place inherited the wrong
/// Esc silently, which is the defect #327 is removing.
/// </remarks>
internal enum EscapeMeaning
{
    /// <summary>Raise the quit confirmation. Only the board does this.</summary>
    AskToQuit,

    /// <summary>
    /// Back all the way out to the board, not one layer.
    /// </summary>
    /// <remarks>
    /// <b>No <see cref="PlayFocus"/> answers with this any more.</b> It shipped as the
    /// behaviour-preserving landing for every menu and <see cref="PlayFocus.Targeting"/>
    /// before the stack existed, when Esc had nothing to step back <em>to</em> — S1 cleared
    /// every menu flag and pending field as one set, so PopToRoot was the only faithful
    /// translation. #509 (Brandon, 2026-08-27: "ESC should drop one level until it's at the
    /// base game") answered the reserved question this remark used to defer, and moved
    /// every menu and <see cref="PlayFocus.Targeting"/> onto <see cref="CloseSelf"/>. Left
    /// defined because <c>PlayFocusRouter.RouteAction.DropToBoard</c> still has a use — the
    /// click pipeline's menu-swallow step (#503) — even with no <see cref="EscapeMeaning"/>
    /// left to reach it.
    /// </remarks>
    DropToBoard,

    /// <summary>Close this layer and leave whatever is under it open.</summary>
    CloseSelf,

    /// <summary>Take the layer's own answer as given — the outcome card's acknowledgement.</summary>
    Commit,

    /// <summary>Quit the game outright. Only the quit confirmation does this.</summary>
    LeaveTheGame,
}
