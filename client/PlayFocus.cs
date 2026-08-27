using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Game;
using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer;

/// <summary>
/// One layer of the play screen's attention: the board, a menu over it, or an armed
/// action waiting for a target.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every member here is abstract on purpose</b> (#500, and #327's fourth acceptance
/// criterion). Each replaces an expression that used to be written out by hand in
/// <c>PlayMode</c>, where a new modal could simply fail to appear:
/// </para>
/// <list type="bullet">
/// <item><see cref="Escape"/> — the Esc cascade's branch order.</item>
/// <item><see cref="TakesRowKeys"/> — <c>OpenMenuLength is &gt; 0</c>, written twice.</item>
/// <item><see cref="SuppressesHotkeys"/> — the "nothing is armed" test around the hotkey lookup.</item>
/// <item><see cref="SuppressesBoard"/> — the "not while the stall is up" term on the
/// fighting-phase keyboard gate.</item>
/// <item><see cref="HoldsTurnOpen"/> — <c>NothingLeftButEndTurn</c>'s four-flag conjunction.</item>
/// </list>
/// <para>
/// Because they are abstract, <b>a new modal that fails to answer one does not
/// compile</b>. That is the whole of the design: the previous shape asked an author to
/// remember five separate places, and the record of this project is that authors did
/// not.
/// </para>
/// </remarks>
internal abstract record PlayFocus
{
    /// <summary>What Esc means from this layer.</summary>
    internal abstract EscapeMeaning Escape { get; }

    /// <summary>Whether Up/Down and Enter belong to this layer rather than the board.</summary>
    internal abstract bool TakesRowKeys { get; }

    /// <summary>
    /// Whether a letter key must not run its action while this layer is up.
    /// </summary>
    /// <remarks>
    /// True only for <see cref="Targeting"/>. A player who has armed an attack and then
    /// types a letter is reaching past a choice they are in the middle of; the old code
    /// spelled this as a test for the armed state around the hotkey lookup.
    /// </remarks>
    internal abstract bool SuppressesHotkeys { get; }

    /// <summary>
    /// Whether this layer takes the whole keyboard, leaving the board none of it.
    /// </summary>
    /// <remarks>
    /// False for every focus in this slice. The shop is the layer that answers true, and
    /// it arrives in S2 — the flag exists now because the landing table it belongs to is
    /// asserted now.
    /// </remarks>
    internal abstract bool SuppressesBoard { get; }

    /// <summary>
    /// Whether a turn must stay open because the player is mid-choice, even when the
    /// engine has nothing left to offer but End Turn.
    /// </summary>
    internal abstract bool HoldsTurnOpen { get; }

    /// <summary>The board itself: the root, and the only focus that is never closed.</summary>
    internal sealed record Board : PlayFocus
    {
        internal override EscapeMeaning Escape => EscapeMeaning.AskToQuit;

        internal override bool TakesRowKeys => false;

        internal override bool SuppressesHotkeys => false;

        internal override bool SuppressesBoard => false;

        internal override bool HoldsTurnOpen => false;
    }

    /// <summary>
    /// The merchant's stall, open between fights.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The notice rides the layer</b> (#501). It used to be a second field beside
    /// the open/closed flag, and Esc had to remember to clear both — with the notice here,
    /// clearing it <em>is</em> the pop, and a purchase is one <c>ReplaceTop</c> rather
    /// than an assignment somebody could forget to pair.
    /// </para>
    /// <para>
    /// <b><see cref="SuppressesBoard"/> is true, and the guard it preserves never
    /// fires.</b> The old "not while the stall is up" term gated the fighting-phase keyboard,
    /// but the stall can only be opened from the interlude, so the term was defence
    /// against a state that cannot arise. It is kept deliberately: an unreachable guard
    /// deleted during a no-behaviour-change refactor is exactly the change no capture can
    /// show.
    /// </para>
    /// </remarks>
    /// <param name="Notice">What the last purchase or refusal said, or null.</param>
    internal sealed record Shop(string? Notice = null) : PlayFocus
    {
        internal override EscapeMeaning Escape => EscapeMeaning.CloseSelf;

        internal override bool TakesRowKeys => false;

        internal override bool SuppressesHotkeys => false;

        internal override bool SuppressesBoard => true;

        internal override bool HoldsTurnOpen => false;
    }

    /// <summary>
    /// The card announcing how the fight ended, before the run moves on.
    /// </summary>
    /// <remarks>
    /// <b>Escape is <see cref="EscapeMeaning.Commit"/>, not a cancel</b>, and this is the
    /// sharp edge of the whole table (#501). Esc on this card calls
    /// <c>CompleteAndReport</c>: it <em>advances the run</em> — experience, loot, the
    /// autosave. A structure that assumed "Esc backs out" would silently turn an
    /// acknowledgement into a dismissal and lose the fight's rewards. Esc is not
    /// uniformly "back out" in this client, and the enum exists so that fact is written
    /// down rather than remembered.
    /// </remarks>
    internal sealed record Outcome : PlayFocus
    {
        internal override EscapeMeaning Escape => EscapeMeaning.Commit;

        internal override bool TakesRowKeys => false;

        internal override bool SuppressesHotkeys => false;

        internal override bool SuppressesBoard => false;

        internal override bool HoldsTurnOpen => false;
    }

    /// <summary>
    /// "LEAVE THE GAME?" — the confirmation that stands between Esc and losing the fight
    /// in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The highest-consequence layer in the client.</b> It exists because two runs were
    /// lost to accidental exits from play on 2026-08-18: Esc is also the key that backs out
    /// of an armed action, so one press past the last thing to cancel used to be the whole
    /// game gone mid-fight, with the run rolled back to the last cleared fight.
    /// </para>
    /// <para>
    /// <b><see cref="HoldsTurnOpen"/> is false, and that preserves an oddity</b> (#510):
    /// <c>_Process</c> never asks whether this card is up, so an auto-end-turn can fire
    /// underneath it. That is today's behaviour, kept here deliberately — whether it
    /// <em>should</em> be suppressed is a question filed separately, not one a
    /// no-behaviour-change refactor gets to answer.
    /// </para>
    /// <para>
    /// It is also the one layer with a rule the five members cannot express: any key that
    /// is not Esc, and any click, takes it down. <c>PlayFocusRouter</c> spells that out
    /// against this type by name, and says why.
    /// </para>
    /// </remarks>
    internal sealed record QuitConfirm : PlayFocus
    {
        internal override EscapeMeaning Escape => EscapeMeaning.LeaveTheGame;

        internal override bool TakesRowKeys => false;

        internal override bool SuppressesHotkeys => false;

        internal override bool SuppressesBoard => false;

        internal override bool HoldsTurnOpen => false;
    }

    /// <summary>A menu of rows over the board — the shared behaviour of the three.</summary>
    internal abstract record RowMenu : PlayFocus
    {
        internal override EscapeMeaning Escape => EscapeMeaning.DropToBoard;

        internal override bool TakesRowKeys => true;

        internal override bool SuppressesHotkeys => false;

        internal override bool SuppressesBoard => false;

        internal override bool HoldsTurnOpen => true;
    }

    /// <summary>The list of a character's attacks.</summary>
    internal sealed record AttackMenu : RowMenu;

    /// <summary>The list of a caster's prepared spells.</summary>
    internal sealed record SpellMenu : RowMenu;

    /// <summary>
    /// The slot levels a chosen spell could burn — offered only when there is more than
    /// one, since one level is not a choice.
    /// </summary>
    /// <param name="Spell">The spell awaiting a slot.</param>
    internal sealed record SlotMenu(SpellDefinition Spell) : RowMenu;

    /// <summary>
    /// An armed action waiting for the player to point at something.
    /// </summary>
    /// <remarks>
    /// This record carries what the four loose payload fields used to hold separately,
    /// and the old <c>Pending</c> enum is gone rather than converted: its five members
    /// were <c>SRDCombat.Game</c>'s <see cref="TargetKind"/> spelled a second time, and
    /// <c>Pending.Nothing</c> is now simply the absence of this layer from the stack —
    /// which is a state that cannot be misspelled.
    /// </remarks>
    /// <param name="Kind">What is armed.</param>
    /// <param name="Attack">The named attack, when one was chosen off the menu.</param>
    /// <param name="Spell">The spell, when a spell is armed.</param>
    /// <param name="Slot">The slot level chosen, when the caster was offered a choice.</param>
    internal sealed record Targeting(
        TargetKind Kind,
        CombatAttack? Attack = null,
        SpellDefinition? Spell = null,
        int? Slot = null) : PlayFocus
    {
        internal override EscapeMeaning Escape => EscapeMeaning.DropToBoard;

        internal override bool TakesRowKeys => false;

        internal override bool SuppressesHotkeys => true;

        internal override bool SuppressesBoard => false;

        internal override bool HoldsTurnOpen => true;
    }
}
