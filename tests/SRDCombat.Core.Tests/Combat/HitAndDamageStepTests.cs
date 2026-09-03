using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
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
/// Also covers #584: Graze's ordered Attack(miss)/Damage(applied) sequence, and the
/// trip-wire that keeps <see cref="CombatStep.Hit"/> null everywhere but an Attack step.
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
    public void AnOrdinaryMissRecordsNoHitAndNoDamageStep()
    {
        // An AC no bonus can meet even on a natural 20, so this misses on every seed —
        // no need to hunt for one whose roll happens to fall short. Named "ordinary" to
        // draw the boundary explicit against AGrazeMissRecordsBothTheMissAndAppliedDamage
        // below: this attacker carries no Weapon Mastery, so nothing follows the miss.
        var (encounter, target) = Fight(
            CombatTestData.MeleeAttack("Sword", bonus: 0),
            armorClass: 100,
            seed: 3);

        Assert.Null(encounter.Attack("Sword", target));
        Assert.False(AttackStep(encounter).Hit);
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Damage);
    }

    [Fact]
    public void AGrazeMissRecordsBothTheMissAndAppliedDamage()
    {
        // #584: Graze applies real damage on a miss (DamageRules.Apply in full — hit
        // points, Concentration, down/death), and the structured log a client reads
        // must carry both facts rather than hide the second inside a Feature step a
        // damage-only channel would silently drop. The ordered sequence is the pin:
        // Attack(Hit: false), immediately followed by Damage(Damage: the modifier).
        var attack = CombatTestData.MeleeAttack("Sword", bonus: 0) with
        {
            Mastery = WeaponMastery.Graze,
            AbilityModifier = 3,
        };

        var (encounter, target) = Fight(attack, armorClass: 100, seed: 3);

        Assert.Null(encounter.Attack("Sword", target));

        var log = encounter.Log.ToList();
        var attackIndex = log.FindIndex(step => step.Kind == CombatStepKind.Attack);
        var damageIndex = log.FindIndex(step => step.Kind == CombatStepKind.Damage);

        Assert.False(log[attackIndex].Hit);
        Assert.Equal(attackIndex + 1, damageIndex);

        var damageStep = log[damageIndex];
        Assert.Equal(target.Id, damageStep.TargetId);
        Assert.Equal(3, damageStep.Damage);
        Assert.Contains("Graze", damageStep.Narration, StringComparison.Ordinal);

        // Exactly one Damage step — Graze's application does not also linger behind
        // as a Feature step now that it has been promoted.
        Assert.Single(log, step => step.Kind == CombatStepKind.Damage);
    }

    [Fact]
    public void AGrazeMissCanKillAndRecordsDamageBeforeDeath()
    {
        // The extreme case #584 exists for: a Graze application that finishes the
        // target off. The structured log must still carry the amount before the Died
        // step, so a client can show the number and the fall rather than a death under
        // an unexplained "Miss".
        var attack = CombatTestData.MeleeAttack("Sword", bonus: 0) with
        {
            Mastery = WeaponMastery.Graze,
            AbilityModifier = 3,
        };

        var (encounter, target) = Fight(attack, armorClass: 100, seed: 3, maximumHitPoints: 2);

        Assert.Null(encounter.Attack("Sword", target));

        var log = encounter.Log.ToList();
        var attackIndex = log.FindIndex(step => step.Kind == CombatStepKind.Attack);
        var damageIndex = log.FindIndex(step => step.Kind == CombatStepKind.Damage);
        var diedIndex = log.FindIndex(step => step.Kind == CombatStepKind.Died);

        Assert.False(log[attackIndex].Hit);
        Assert.Equal(attackIndex + 1, damageIndex);
        Assert.Equal(damageIndex + 1, diedIndex);
        Assert.Equal(3, log[damageIndex].Damage);
        Assert.True(target.IsDead);
    }

    [Fact]
    public void EveryAttackStepRecordsHitAndEveryOtherKindRecordsNone()
    {
        // The trip-wire CombatStep.Hit's nullable default exists for (#584): a future
        // Attack emission site that forgets hit: must fail this rather than silently
        // reading as a connection that never happened. Both live emission sites are
        // exercised — a hit and a miss — alongside the non-Attack kinds Encounter.Start
        // and a plain attack already produce for free (EncounterStarted, RoundStarted,
        // TurnStarted, Damage).
        var (hitEncounter, hitTarget) = Fight(
            CombatTestData.MeleeAttack("Sword", bonus: 50), armorClass: 13, seed: 3);
        Assert.Null(hitEncounter.Attack("Sword", hitTarget));

        var (missEncounter, missTarget) = Fight(
            CombatTestData.MeleeAttack("Sword", bonus: 0), armorClass: 100, seed: 3);
        Assert.Null(missEncounter.Attack("Sword", missTarget));

        foreach (var log in new[] { hitEncounter.Log, missEncounter.Log })
        {
            Assert.NotEmpty(log);

            Assert.All(
                log.Where(step => step.Kind == CombatStepKind.Attack),
                step => Assert.NotNull(step.Hit));

            Assert.All(
                log.Where(step => step.Kind != CombatStepKind.Attack),
                step => Assert.Null(step.Hit));
        }
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
