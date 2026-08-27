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
    /// <b>PopToRoot, deliberately.</b> Today Esc from the slot menu clears every menu
    /// flag and every pending field at once and lands the player on the board. A stack
    /// that popped one layer would land them on the spell menu instead — a game change
    /// wearing a refactor's clothes. Whether the game *wants* pop-one is a designer
    /// question and is filed separately; it is not this enum's to decide.
    /// </remarks>
    DropToBoard,

    /// <summary>Close this layer and leave whatever is under it open.</summary>
    CloseSelf,

    /// <summary>Take the layer's own answer as given — the outcome card's acknowledgement.</summary>
    Commit,

    /// <summary>Quit the game outright. Only the quit confirmation does this.</summary>
    LeaveTheGame,
}
