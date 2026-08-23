using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Target-aware attack valuation (#224): <c>ValueAt</c> ranks attacks by average
/// damage against a specific target, zeroed by an Immunity and halved by a
/// Resistance — the same shape as the long-range discount in
/// <see cref="LongRangeThrowTests"/>, a preference over the figure the ordering
/// already sorts on rather than a special case. Before this, an Ochre Jelly
/// standing between a hero's Longsword and Javelin got hit with the Longsword —
/// the raw-average winner — for zero damage every round, forever, while the
/// Piercing weapon that actually worked stayed on the belt.
/// </summary>
public class DamageResponseValuationTests
{
    [Fact]
    public void AnAttackChoiceFlipsToTheNonImmuneWeapon()
    {
        // The Longsword averages more raw damage than the Javelin, so before the
        // fix TryAttack swings it every time — for zero, since the target is
        // Immune to Slashing. Ranked against this target instead, the Javelin's
        // real 6.5 beats the Longsword's zeroed 7.5, so it swings instead.
        var actor = CombatTestData.Combatant(
            "hero",
            stats: CombatTestData.Stats(
                initiativeBonus: 10,
                attacks:
                [
                    CombatTestData.MeleeAttack("Longsword", damage: "1d6 + 5", type: DamageType.Slashing),
                    CombatTestData.MeleeAttack("Javelin", damage: "1d6 + 3", type: DamageType.Piercing),
                ]));

        var target = CombatTestData.Combatant(
            "jelly",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                initiativeBonus: 0,
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Slashing] = DamageResponse.Immunity,
                }),
            x: 1);

        var encounter = Encounter.Start(
            new Battlefield(5, 5),
            [actor, target],
            // Two initiatives, then a hit and its damage die.
            new ScriptedRandomSource(15, 1, 10, 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        var attack = Assert.Single(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.ActorId == actor.Id);
        Assert.Contains("Javelin", attack.Narration);
    }

    [Fact]
    public void AResistantTargetStillLosesToAHarderNonResistedAttack()
    {
        // A Resistance halves rather than zeroes, so the ordering is a genuine
        // comparison rather than an on/off switch: the Warhammer's raw 7.5 halves to
        // 3.75 against Bludgeoning Resistance, short of the Dagger's un-resisted 4.5,
        // so the Dagger wins even though it is the visibly weaker weapon on paper.
        var actor = CombatTestData.Combatant(
            "hero",
            stats: CombatTestData.Stats(
                initiativeBonus: 10,
                attacks:
                [
                    CombatTestData.MeleeAttack("Warhammer", damage: "1d6 + 4", type: DamageType.Bludgeoning),
                    CombatTestData.MeleeAttack("Dagger", damage: "1d4 + 2", type: DamageType.Piercing),
                ]));

        var target = CombatTestData.Combatant(
            "skeleton",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                initiativeBonus: 0,
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Bludgeoning] = DamageResponse.Resistance,
                }),
            x: 1);

        var encounter = Encounter.Start(
            new Battlefield(5, 5),
            [actor, target],
            new ScriptedRandomSource(15, 1, 10, 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        var attack = Assert.Single(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.ActorId == actor.Id);
        Assert.Contains("Dagger", attack.Narration);
    }

    [Fact]
    public void TheOchreJellyStandoffResolvesInTheHerosFavour()
    {
        // #224, at unit scale: a Slashing-Immune target and a hero who owns both a
        // harder Slashing weapon and a weaker Piercing one. Before the fix, the
        // Longsword always won the raw ranking and never dealt a point of damage —
        // on this seed that is a clean loss (the jelly's own chip damage eventually
        // downs a hero who can never fight back), and on the issue's own seed,
        // where Second Wind kept pace with the jelly's chip damage, the same
        // mechanism produced a real round-limit stall instead. Either way the fight
        // never goes as an unbeatable jelly should. With the target-aware
        // valuation, the Javelin wins the ranking instead and the hero actually
        // wins.
        var actor = CombatTestData.Combatant(
            "hero",
            stats: CombatTestData.Stats(
                maximumHitPoints: 30,
                initiativeBonus: 10,
                attacks:
                [
                    CombatTestData.MeleeAttack("Longsword", damage: "1d6 + 5", type: DamageType.Slashing),
                    CombatTestData.MeleeAttack("Javelin", damage: "1d6 + 3", type: DamageType.Piercing),
                ]));

        var jelly = CombatTestData.Combatant(
            "jelly",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                armorClass: 8,
                maximumHitPoints: 12,
                initiativeBonus: 0,
                attacks: [CombatTestData.MeleeAttack("Pseudopod", damage: "1d4", type: DamageType.Acid)],
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Slashing] = DamageResponse.Immunity,
                }),
            x: 1);

        var encounter = Encounter.Start(new Battlefield(5, 5), [actor, jelly], new SeededRandomSource(3));

        // A generous limit against the hero's own hit points, not the policy's real
        // 50-round default: the point is that the fight ends at all, not to pin how
        // many rounds it happens to take on this seed.
        SimpleTacticsPolicy.RunToCompletion(encounter, roundLimit: 20);

        Assert.True(encounter.IsComplete);
        Assert.Equal(CombatTestData.Heroes, encounter.WinningSide);
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack
                && step.ActorId == actor.Id
                && step.Narration.Contains("Javelin"));
    }
}
