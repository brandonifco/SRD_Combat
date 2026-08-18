using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The long-range discount: an attack rolled at long range is at Disadvantage, so the
/// policy values it at half when ranking attacks against each other — which is what
/// sends a thrower walking in rather than lobbing from across the board, and a
/// stranded archer closing into her own normal band. A preference, never a veto: with
/// no movement left, the long throw is still taken.
/// </summary>
/// <remarks>
/// Every test scripts its dice exactly. A Disadvantage roll consumes two d20s where a
/// normal roll consumes one, so a turn that shoots at long range when these tests say
/// it must not asks the scripted source for a die it does not hold and fails loudly.
/// </remarks>
public class LongRangeThrowTests
{
    /// <summary>A thrown melee weapon: reach 5, range 20/60, the Handaxe's shape.</summary>
    private static CombatAttack ThrownAxe(string name = "Handaxe", string damage = "1d6 + 3")
    {
        var dice = DiceExpression.Parse(damage);

        return new CombatAttack(
            name,
            AttackKind.Melee,
            4,
            5,
            20,
            60,
            [new AttackDamage(dice, DamageType.Slashing, dice.Average)]);
    }

    private static (Encounter Encounter, Combatant Actor, Combatant Target) Fight(
        CombatAttack[] attacks,
        int speedFeet,
        params int[] dice)
    {
        var actor = CombatTestData.Combatant(
            "thrower",
            stats: CombatTestData.Stats(
                speedFeet: speedFeet,
                initiativeBonus: 10,
                attacks: attacks));

        var target = CombatTestData.Combatant(
            "victim",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: 0),
            x: 8);

        var encounter = Encounter.Start(
            new Battlefield(12, 3),
            [actor, target],
            new ScriptedRandomSource(dice));

        return (encounter, actor, target);
    }

    [Fact]
    public void AnArcherBeyondNormalRangeClosesInsteadOfShootingAtDisadvantage()
    {
        // 40 feet with a 20/60 sling: the shot from here is at Disadvantage and worth
        // half, so walking into the normal band beats standing. Before the discount
        // this was a tie — the sling against itself — and a tie stands still, so the
        // archer shot at Disadvantage from the spawn square all fight.
        var (encounter, actor, target) = Fight(
            [CombatTestData.RangedAttack("Sling", bonus: 4, normalFeet: 20, longFeet: 60, damage: "1d6 + 2")],
            speedFeet: 30,
            // Two initiatives, then one d20 — a normal roll, not two — and its damage.
            15, 1, 10, 3);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(encounter.Log, step =>
            step.Kind == CombatStepKind.Move && step.ActorId == actor.Id);

        var attack = Assert.Single(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.ActorId == actor.Id);
        Assert.Equal(RangedAttackKind.Weapon, attack.Ranged);
        Assert.True(actor.Position.DistanceFeetTo(target.Position) <= 20);
    }

    [Fact]
    public void TheLongThrowIsStillTakenWhenClosingIsImpossible()
    {
        // No movement to close with: the discounted throw is still the best thing this
        // turn holds, so it is taken — at Disadvantage, two d20s and the worse kept.
        var (encounter, actor, _) = Fight(
            [ThrownAxe()],
            speedFeet: 0,
            // Two initiatives, then the Disadvantage pair and the damage die.
            15, 1, 12, 10, 3);

        SimpleTacticsPolicy.TakeTurn(encounter);

        var attack = Assert.Single(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.ActorId == actor.Id);
        Assert.Equal(RangedAttackKind.Weapon, attack.Ranged);
    }

    [Fact]
    public void TheBowInItsNormalBandOutranksTheHarderThrowAtLongRange()
    {
        // At 40 feet the axe averages more on paper and half as much through its
        // Disadvantage, so the bow shoots. Sorting on raw damage took the axe.
        var (encounter, actor, _) = Fight(
            [
                ThrownAxe("Heavy Axe", damage: "1d8 + 4"),
                CombatTestData.RangedAttack("Bow", bonus: 4, normalFeet: 80, longFeet: 320, damage: "1d6 + 3"),
            ],
            speedFeet: 0,
            // Two initiatives, then the bow's single d20 and its d6 — the axe's
            // Disadvantage pair and d8 would not fit this script.
            15, 1, 10, 3);

        SimpleTacticsPolicy.TakeTurn(encounter);

        var attack = Assert.Single(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.ActorId == actor.Id);
        Assert.Contains("Bow", attack.Narration);
    }
}
