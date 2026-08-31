using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// What an attack step records about whether it connected, and what a damage step
/// records about how much it applied.
/// </summary>
/// <remarks>
/// Recorded on <see cref="CombatStep"/> for the reason <c>AttackName</c> and <c>Ranged</c>
/// are: "hit", "miss" and the digit are already in the narration, but this project does
/// not parse its own prose, so a client telling a hit from a miss and reading the amount
/// dealt — #298's floating damage numbers — reads these fields instead of the sentence.
/// </remarks>
public class HitAndDamageStepTests
{
    [Fact]
    public void AConnectingAttackRecordsHit()
    {
        // A nat 1 is an automatic miss regardless of bonus, so "guaranteed hit" still
        // needs a bonus large enough that every roll but a 1 connects, and a seed whose
        // first attack roll is not a 1.
        var (encounter, target) = Fight(
            CombatTestData.MeleeAttack("Sword", bonus: 50),
            armorClass: 13,
            seed: 3);

        Assert.Null(encounter.Attack("Sword", target));
        Assert.True(AttackStep(encounter).Hit);
    }

    [Fact]
    public void AConnectingAttackIsFollowedByADamageStepCarryingWhatWasApplied()
    {
        var before = 60;

        var (encounter, target) = Fight(
            CombatTestData.MeleeAttack("Sword", bonus: 50),
            armorClass: 13,
            seed: 3,
            maximumHitPoints: before);

        Assert.Null(encounter.Attack("Sword", target));

        var lost = before - target.CurrentHitPoints;
        var damageStep = encounter.Log.Last(step => step.Kind == CombatStepKind.Damage);

        Assert.Equal(lost, damageStep.Damage);
    }

    [Fact]
    public void AMissRecordsNoHitAndNoDamageStep()
    {
        // An AC no bonus can meet even on a natural 20, so this misses on every seed —
        // no need to hunt for one whose roll happens to fall short.
        var (encounter, target) = Fight(
            CombatTestData.MeleeAttack("Sword", bonus: 0),
            armorClass: 100,
            seed: 3);

        Assert.Null(encounter.Attack("Sword", target));
        Assert.False(AttackStep(encounter).Hit);
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Damage);
    }

    private static CombatStep AttackStep(Encounter encounter) =>
        encounter.Log.Last(step => step.Kind == CombatStepKind.Attack);

    private static (Encounter Encounter, Combatant Target) Fight(
        CombatAttack attack,
        int armorClass,
        int seed,
        int maximumHitPoints = 60)
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
                    stats: CombatTestData.Stats(
                        armorClass: armorClass,
                        maximumHitPoints: maximumHitPoints,
                        initiativeBonus: -10,
                        attacks: []),
                    x: 1),
            ],
            new SeededRandomSource(seed));

        return (encounter, encounter.Combatants.Single(combatant => combatant.Id == "target"));
    }
}
