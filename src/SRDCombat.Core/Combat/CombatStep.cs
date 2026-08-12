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
public sealed record CombatStep(
    CombatStepKind Kind,
    string Narration,
    string? ActorId = null,
    string? TargetId = null)
{
    public override string ToString() => Narration;
}
