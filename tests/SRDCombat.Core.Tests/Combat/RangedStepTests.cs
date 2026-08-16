using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// What an attack step records about having crossed a distance.
/// </summary>
/// <remarks>
/// The fact is the engine's rather than a client's for the reason <c>CombatStep.Path</c>
/// is: a client reading the gap instead would call a reach weapon a shot, and would still
/// have to know which of an attacker's attacks was swung. These pin the two edges where
/// guessing from distance gives the wrong answer.
/// </remarks>
public class RangedStepTests
{
    [Fact]
    public void ABowAcrossTheBoardIsRecordedAsAWeaponShot()
    {
        var (encounter, target) = Fight(
            CombatTestData.RangedAttack("Shortbow", bonus: 5),
            targetX: 6);

        Assert.Null(encounter.Attack("Shortbow", target));

        Assert.Equal(RangedAttackKind.Weapon, AttackStep(encounter).Ranged);
    }

    [Fact]
    public void ABladeInReachIsNotAShot()
    {
        var (encounter, target) = Fight(
            CombatTestData.MeleeAttack("Longsword", bonus: 5),
            targetX: 1);

        Assert.Null(encounter.Attack("Longsword", target));

        Assert.Equal(RangedAttackKind.None, AttackStep(encounter).Ranged);
    }

    [Fact]
    public void AReachWeaponAtItsFullReachIsStillNotAShot()
    {
        // The case that makes this the engine's answer rather than the client's: two
        // squares of gap, and nothing has crossed it. A Halberd reaches.
        var (encounter, target) = Fight(
            CombatTestData.MeleeAttack("Halberd", bonus: 5, reachFeet: 10),
            targetX: 2);

        Assert.Null(encounter.Attack("Halberd", target));

        Assert.Equal(RangedAttackKind.None, AttackStep(encounter).Ranged);
    }

    [Fact]
    public void ABowFiredPointBlankIsStillAShot()
    {
        // The mirror: no gap at all, and an arrow still crosses it. This is exactly the
        // roll the printed close-combat Disadvantage applies to, so the engine already
        // has to know — the step simply carries what it knew.
        var (encounter, target) = Fight(
            CombatTestData.RangedAttack("Shortbow", bonus: 5),
            targetX: 1);

        Assert.Null(encounter.Attack("Shortbow", target));

        Assert.Equal(RangedAttackKind.Weapon, AttackStep(encounter).Ranged);
    }

    private static CombatStep AttackStep(Encounter encounter) =>
        encounter.Log.Last(step => step.Kind == CombatStepKind.Attack);

    private static (Encounter Encounter, Combatant Target) Fight(CombatAttack attack, int targetX)
    {
        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                CombatTestData.Combatant(
                    "attacker",
                    stats: CombatTestData.Stats(initiativeBonus: 10, attacks: [attack])),
                CombatTestData.Combatant(
                    "target",
                    sideId: CombatTestData.Monsters,
                    stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, attacks: []),
                    x: targetX),
            ],
            new SeededRandomSource(3));

        return (encounter, encounter.Combatants.Single(combatant => combatant.Id == "target"));
    }
}
