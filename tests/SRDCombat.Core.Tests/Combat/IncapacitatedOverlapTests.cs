using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// #614: two Incapacitated-bringers on one <see cref="Combatant"/> used to collide in
/// the single Incapacitated dictionary slot — the second's <c>TryAdd</c> no-op'd, so
/// removing whichever bringer got the slot first (or Turn Undead's standalone rider)
/// freed a creature the other bringer should still hold Incapacitated over.
/// </summary>
/// <remarks>
/// The fix stops materializing a brought Incapacitated at all: the dictionary's one
/// Incapacitated key is reserved for a standalone Incapacitated (Turn Undead's rider,
/// the only one this engine executes), and <c>Combatant.EffectiveIncapacitation()</c>
/// derives its presence on every read instead — "Incapacitated present ⇔ a standalone
/// entry exists OR any <c>BringsIncapacitated</c> condition is present." These tests
/// exercise that invariant directly against bare <see cref="Combatant"/>s, without
/// going through <see cref="Encounter"/> — the collision is a <c>Combatant</c>-level
/// bookkeeping question, and <see cref="TurnUndeadTests"/> covers the same fix through
/// a full turn/attack sequence.
/// </remarks>
public class IncapacitatedOverlapTests
{
    [Fact]
    public void TwoBringers_StunnedThenParalyzed_BothRemovalOrdersLeaveIncapacitatedUntilBothAreGone()
    {
        var creature = CombatTestData.Combatant();

        creature.AddCondition(ConditionType.Stunned, sourceId: "a");
        creature.AddCondition(ConditionType.Paralyzed, sourceId: "b");

        Assert.True(creature.HasCondition(ConditionType.Incapacitated));

        Assert.True(creature.RemoveCondition(ConditionType.Stunned));
        Assert.True(creature.HasCondition(ConditionType.Incapacitated));
        Assert.False(creature.HasCondition(ConditionType.Stunned));

        Assert.True(creature.RemoveCondition(ConditionType.Paralyzed));
        Assert.False(creature.HasCondition(ConditionType.Incapacitated));
        Assert.True(creature.CanAct);
    }

    [Fact]
    public void TwoBringers_ParalyzedThenStunned_ReverseRemovalOrderAlsoLeavesIncapacitatedUntilBothAreGone()
    {
        var creature = CombatTestData.Combatant();

        creature.AddCondition(ConditionType.Paralyzed, sourceId: "a");
        creature.AddCondition(ConditionType.Stunned, sourceId: "b");

        Assert.True(creature.HasCondition(ConditionType.Incapacitated));

        Assert.True(creature.RemoveCondition(ConditionType.Paralyzed));
        Assert.True(creature.HasCondition(ConditionType.Incapacitated));
        Assert.False(creature.HasCondition(ConditionType.Paralyzed));

        Assert.True(creature.RemoveCondition(ConditionType.Stunned));
        Assert.False(creature.HasCondition(ConditionType.Incapacitated));
        Assert.True(creature.CanAct);
    }

    [Fact]
    public void StandaloneThenStunnedFromAnotherSource_RemovingTheStandaloneLeavesStunnedHoldingIncapacitated()
    {
        // Turn-Undead-shaped: a flagged standalone Incapacitated landing first, from
        // the Cleric, followed by an unrelated Stunned from a different source.
        var creature = CombatTestData.Combatant();

        creature.AddCondition(new ActiveCondition(
            ConditionType.Incapacitated,
            SourceId: "cleric",
            EndsEarlyOnDamageOrSourceDown: true));
        creature.AddCondition(ConditionType.Stunned, sourceId: "other");

        Assert.True(creature.HasCondition(ConditionType.Incapacitated));
        Assert.True(creature.HasCondition(ConditionType.Stunned));

        // Removing the standalone entry directly (as BreakTurnEffectOnDamage does)
        // must not take Incapacitated away from the creature — Stunned still holds it.
        Assert.True(creature.RemoveCondition(ConditionType.Incapacitated));
        Assert.True(creature.HasCondition(ConditionType.Incapacitated));
        Assert.False(creature.CanAct);
    }

    [Fact]
    public void StunnedFirstThenStandaloneFromAnotherSource_LandsCleanlyAndOutlivesStunned()
    {
        // The empty-slot case: Stunned lands first, so the dictionary's Incapacitated
        // key is never occupied by anything materialized — a standalone Incapacitated
        // from a different source then lands into that empty slot without hitting the
        // flag-mismatch guard (there is nothing occupying the slot to mismatch against).
        var creature = CombatTestData.Combatant();

        creature.AddCondition(ConditionType.Stunned, sourceId: "other");
        Assert.True(creature.HasCondition(ConditionType.Incapacitated));

        var added = creature.AddCondition(new ActiveCondition(
            ConditionType.Incapacitated,
            SourceId: "cleric",
            EndsEarlyOnDamageOrSourceDown: true));
        Assert.True(added);
        Assert.True(creature.HasCondition(ConditionType.Incapacitated));

        Assert.True(creature.RemoveCondition(ConditionType.Stunned));
        Assert.True(creature.HasCondition(ConditionType.Incapacitated));
        Assert.False(creature.CanAct);

        Assert.True(creature.RemoveCondition(ConditionType.Incapacitated));
        Assert.False(creature.HasCondition(ConditionType.Incapacitated));
        Assert.True(creature.CanAct);
    }

    [Fact]
    public void RemovingOneOfTwoBringersLeavesIncapacitatedWhileAtLeastOneStillHoldsIt()
    {
        // The invariant stated explicitly: Incapacitated present ⇔ a standalone entry
        // exists OR any BringsIncapacitated condition is present. With two bringers
        // and no standalone entry, removing either one alone must never flip it false.
        var creature = CombatTestData.Combatant();

        creature.AddCondition(ConditionType.Unconscious, sourceId: "a");
        creature.AddCondition(ConditionType.Petrified, sourceId: "b");

        Assert.True(creature.HasCondition(ConditionType.Incapacitated));
        Assert.Contains(ConditionType.Incapacitated, creature.Conditions);
        Assert.Contains(creature.ActiveConditions, active => active.Condition == ConditionType.Incapacitated);

        creature.RemoveCondition(ConditionType.Unconscious);

        Assert.True(creature.HasCondition(ConditionType.Incapacitated), "Petrified alone must still hold Incapacitated.");
        Assert.True(creature.HasCondition(ConditionType.Petrified));

        // Prone is left behind by Unconscious ending, per print — unaffected by this fix.
        Assert.True(creature.HasCondition(ConditionType.Prone));

        creature.RemoveCondition(ConditionType.Petrified);

        Assert.False(creature.HasCondition(ConditionType.Incapacitated));
        Assert.DoesNotContain(ConditionType.Incapacitated, creature.Conditions);
    }
}
