using SRDCombat.Game;
using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer;

/// <summary>
/// Pure decisions that govern when <see cref="PlayMode"/> may advance the fight.
/// </summary>
internal static class PlayTurnFlow
{
    /// <summary>
    /// Whether the commanded combatant has no remaining choice but ending the turn.
    /// </summary>
    /// <remarks>
    /// <b>The row is the whole question, and leftover movement is deliberately not part
    /// of it.</b> This first shipped also requiring the reachable squares to be empty, on
    /// the reasoning that walking is not a button so a row holding only End Turn says
    /// nothing about whether the character can still reposition. That reasoning is sound
    /// and the behaviour was wrong: <b>attacking spends the Action, never the movement</b>,
    /// so a character who swings from where they stand keeps a full Speed and every such
    /// turn still had to be dismissed by hand — which is nearly every turn, and exactly
    /// the friction this exists to remove.
    /// <para>
    /// The cost is stated rather than hidden: a character who attacks before moving no
    /// longer gets to step away afterwards. That is the XCOM convention — acting ends
    /// your turn — and it is predictable, which beats a rule that sometimes ends the turn
    /// and sometimes does not depending on a number the row never showed. Move first,
    /// then act.
    /// </para>
    /// <para>
    /// Anything the player has half-started — an armed attack, an open menu, or the quit
    /// confirmation — counts as a choice in progress and holds the turn open, so the
    /// screen never closes over something somebody was in the middle of (#510).
    /// </para>
    /// </remarks>
    internal static bool NothingLeftButEndTurn(
        FocusStack<PlayFocus> focus,
        IReadOnlyList<TurnAction> options) =>
        !focus.BottomUp.Any(layer => layer.HoldsTurnOpen)
        && options is [TurnAction.EndTurn];
}
