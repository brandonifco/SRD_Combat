namespace SRDCombat.Core.Combat;

/// <summary>What kind of thing happened.</summary>
public enum CombatStepKind
{
    EncounterStarted,
    RoundStarted,
    TurnStarted,
    Move,
    Attack,
    Damage,
    Downed,
    Died,
    DeathSave,
    Stabilized,
    Dodge,
    Dash,
    Disengage,
    OpportunityAttack,

    /// <summary>A condition being imposed on a creature.</summary>
    Condition,

    /// <summary>A class feature being used or expiring.</summary>
    Feature,

    /// <summary>A spell being cast, resisted, or lost.</summary>
    Spell,

    /// <summary>A spent Recharge ability rolling its d6 at the start of a turn.</summary>
    Recharge,

    /// <summary>A stat block entry being used, and the saving throws it forces.</summary>
    Entry,

    /// <summary>A carried consumable being used — a potion drunk or administered.</summary>
    Item,
    TurnEnded,
    EncounterEnded,
}

/// <summary>
/// One thing that happened in a fight, and the line of narration describing it.
/// </summary>
/// <remarks>
/// The narration is deliberately part of the engine rather than left to a client. It is
/// what the frozen-transcript tests pin, and it is what the Gold Box-style combat log
/// this project has committed to showing is built from — every roll, save and damage
/// number visible. A client chooses how to display these; it never has to invent them.
/// </remarks>
/// <param name="Kind">What happened.</param>
/// <param name="Narration">A complete, human-readable sentence.</param>
/// <param name="ActorId">The combatant who acted, when there is one.</param>
/// <param name="TargetId">The combatant acted upon, when there is one.</param>
/// <param name="Path">
/// For a <see cref="CombatStepKind.Move"/> that crossed the grid: every square the mover
/// occupied, in order, starting square first — cut short where an Opportunity Attack
/// dropped them. Null on every other step, standing up included. It is here so a client
/// can show the walk the engine took without recomputing the route, which would be a
/// second place movement rules live.
/// </param>
public sealed record CombatStep(
    CombatStepKind Kind,
    string Narration,
    string? ActorId = null,
    string? TargetId = null,
    IReadOnlyList<GridPosition>? Path = null,
    RangedAttackKind Ranged = RangedAttackKind.None)
{
    /// <summary>
    /// Whether an attack step was a <em>ranged</em> attack roll, and of which sort —
    /// something crossed the distance rather than a blade reaching it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded here for the reason <see cref="Path"/> is: so no client has to work it
    /// out. The answer is the engine's own — <c>CombatAttack.IsRangedAttackRoll</c>, the
    /// same predicate the printed "Ranged Attacks in Close Combat" Disadvantage hangs on
    /// — and a client guessing from the gap instead would read a Halberd's ten-foot reach
    /// as a shot, and would still have to know which of an attacker's attacks was used.
    /// </para>
    /// <para>
    /// <b>Weapon and spell are told apart here rather than downstream</b>, because the
    /// only other way for a client to know is to read the narration, and this project
    /// does not parse its own prose — a reworded sentence would silently change what the
    /// screen draws. Which art each kind gets is the client's business; that there
    /// <em>was</em> a spell attack is the engine's.
    /// </para>
    /// </remarks>
    public RangedAttackKind Ranged { get; init; } = Ranged;

    public override string ToString() => Narration;
}

/// <summary>Whether an attack crossed a distance, and by what means.</summary>
public enum RangedAttackKind
{
    /// <summary>Not a ranged attack roll — a blade, a claw, a reach weapon.</summary>
    None,

    /// <summary>A ranged weapon attack: a bow, a sling, a thrown spear.</summary>
    Weapon,

    /// <summary>A spell attack roll, which crosses the room the same way.</summary>
    Spell,
}
