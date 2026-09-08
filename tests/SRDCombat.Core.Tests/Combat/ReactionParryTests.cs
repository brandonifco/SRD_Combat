using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The reaction-trigger seam, built on its first case — Parry (#677, slice 1 of the
/// #413 ledger): a Reaction that raises the reacting creature's Armor Class against one
/// melee attack that would otherwise hit it, a deterministic recompute of an
/// already-rolled attack with no new dice.
/// </summary>
/// <remarks>
/// <para>
/// Every fixture here is hand-authored — the parrying creature carries a
/// <see cref="ReactionEffect"/> with an <see cref="ExecutableReaction"/> set directly,
/// the way <c>CombatTestData</c> hand-builds every combat fixture so an engine test
/// fails when the engine changes rather than when the bestiary is re-extracted. The
/// content half — that the real Bandit Captain, Knight, Warrior Veteran and Noble
/// actually carry this field — is #413-D (the extractor slice), not this one.
/// </para>
/// <para>
/// The rolls are scripted exactly: <c>[init, init, attackRoll, ...damage]</c>. AC 15
/// against a +5 attack means a natural 11 totals 16 and beats AC by exactly 1, so a
/// Parry that adds 2 flips it to a miss; a natural 14 totals 19 and beats AC by 4, which
/// no +2 can undo. The scripts carry a damage die only on the outcomes that actually
/// deal damage, so a surplus or missing die throws — that is the proof the recompute
/// really did (or did not) turn the hit into a miss.
/// </para>
/// </remarks>
public class ReactionParryTests
{
    private const int ParryBonus = 2;

    [Fact]
    public void AParryTurnsAWouldHitMeleeAttackIntoAMissAndSpendsTheReaction()
    {
        // Natural 11 + 5 = 16 beats AC 15 by 1; +2 raises AC to 17, so the hit misses.
        var (encounter, attacker, target) = ParryFight(attackRoll: 11);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.False(target.Turn.HasReaction);
        Assert.Equal(target.Stats.MaximumHitPoints, target.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Feature
                && step.Narration.Contains("Parries", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.Hit == false);
    }

    [Fact]
    public void AParrySpentOnOneAttackCannotFireAgainstASecondTheSameRound()
    {
        // Two attackers, each rolling 11 (a would-hit-by-1). The first is parried; the
        // Reaction is now spent and refreshes only at the start of the target's own turn,
        // so the second lands. One damage die is scripted — for the second, hitting swing.
        var (encounter, first, second, target) = TwoAttackerParryFight(firstRoll: 11, secondRoll: 11, secondDamage: 4);

        Assert.Null(encounter.Attack(first.Stats.Attacks[0].Name, target));
        Assert.False(target.Turn.HasReaction);
        var afterFirst = target.CurrentHitPoints;

        encounter.EndTurn(); // first attacker's turn ends, second attacker's begins

        Assert.Null(encounter.Attack(second.Stats.Attacks[0].Name, target));

        Assert.Equal(target.Stats.MaximumHitPoints, afterFirst); // first was parried: no damage
        Assert.True(target.CurrentHitPoints < afterFirst);       // second landed: damage dealt
        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Kind == CombatStepKind.Feature
                && step.Narration.Contains("Parries", StringComparison.Ordinal)));
    }

    [Fact]
    public void AParryDoesNotFireAgainstARangedAttack()
    {
        // Same would-hit-by-1 roll, but the attacker swings a ranged weapon: Parry's
        // trigger is a melee attack roll, so nothing reacts and the hit lands.
        var (encounter, attacker, target) = ParryFight(
            attackRoll: 11,
            attackerAttack: CombatTestData.RangedAttack(bonus: 5, damage: "1d4"),
            attackerX: 3,
            damageRolls: [4]);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.True(target.Turn.HasReaction);
        Assert.True(target.CurrentHitPoints < target.Stats.MaximumHitPoints);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Parries", StringComparison.Ordinal));
    }

    [Fact]
    public void AParryDoesNotFireAgainstADualModeAttackUsedBeyondReach()
    {
        // A thrown weapon prints "Melee or Ranged Attack Roll" and the extractor stores
        // it as Melee, its modality carried by the distance fields — so the Kind alone
        // would wrongly admit it. Used at 15 feet (beyond its 5-foot reach) it is a
        // ranged attack roll, which Parry's melee trigger excludes.
        var (encounter, attacker, target) = ParryFight(
            attackRoll: 11,
            attackerAttack: ThrownAttack(bonus: 5, damage: "1d4"),
            attackerX: 0,
            targetX: 3,
            damageRolls: [4]);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.True(target.Turn.HasReaction);
        Assert.True(target.CurrentHitPoints < target.Stats.MaximumHitPoints);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Parries", StringComparison.Ordinal));
    }

    [Fact]
    public void AParryFiresAgainstADualModeAttackUsedInMelee()
    {
        // The same thrown weapon used inside its reach (5 feet) is the melee use, a
        // melee attack roll, and is parryable — so the ranged gate does not over-exclude.
        var (encounter, attacker, target) = ParryFight(
            attackRoll: 11,
            attackerAttack: ThrownAttack(bonus: 5, damage: "1d4"),
            attackerX: 0,
            targetX: 1);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.False(target.Turn.HasReaction);
        Assert.Equal(target.Stats.MaximumHitPoints, target.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Feature
                && step.Narration.Contains("Parries", StringComparison.Ordinal));
    }

    [Fact]
    public void AParryDoesNotFireAgainstAHitTheBonusCannotFlip()
    {
        // Natural 14 + 5 = 19 beats AC 15 by 4; +2 cannot pull it below the roll, so the
        // Reaction is not worth spending and is left intact.
        var (encounter, attacker, target) = ParryFight(attackRoll: 14, damageRolls: [4]);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.True(target.Turn.HasReaction);
        Assert.True(target.CurrentHitPoints < target.Stats.MaximumHitPoints);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Parries", StringComparison.Ordinal));
    }

    [Fact]
    public void ABlindedCreatureCannotParryForWantOfSeeingTheAttacker()
    {
        // Blinding the target hands the attacker Advantage, so the attack rolls two dice
        // (the higher, 11, is the natural); the lower, 2, is scripted alongside it.
        var (encounter, attacker, target) = ParryFight(attackRoll: 11, advantageDie: 2, damageRolls: [4]);
        target.AddCondition(ConditionType.Blinded);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.True(target.Turn.HasReaction);
        Assert.True(target.CurrentHitPoints < target.Stats.MaximumHitPoints);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Parries", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIncapacitatedCreatureDoesNotParry()
    {
        var (encounter, attacker, target) = ParryFight(attackRoll: 11, damageRolls: [4]);
        target.AddCondition(ConditionType.Incapacitated);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.True(target.Turn.HasReaction);
        Assert.True(target.CurrentHitPoints < target.Stats.MaximumHitPoints);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Parries", StringComparison.Ordinal));
    }

    [Fact]
    public void ACreatureNotHoldingAMeleeWeaponCannotParry()
    {
        // The Parry entry is present, but the creature's only attack is ranged — nothing
        // to raise a melee guard with, so the printed "while holding a weapon" gate fails.
        var (encounter, attacker, target) = ParryFight(
            attackRoll: 11,
            targetAttacks: [CombatTestData.RangedAttack(bonus: 4, damage: "1d4")],
            damageRolls: [4]);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.True(target.Turn.HasReaction);
        Assert.True(target.CurrentHitPoints < target.Stats.MaximumHitPoints);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Parries", StringComparison.Ordinal));
    }

    [Fact]
    public void ACreatureWithoutAParryEntryIsUnaffected()
    {
        // The no-op proof at unit scale, mirroring the frozen-transcript byte-flat proof:
        // the same would-hit-by-1 attack against a creature with no Parry entry lands
        // untouched, nothing is spent, and no parry is narrated.
        var (encounter, attacker, target) = ParryFight(attackRoll: 11, withParry: false, damageRolls: [4]);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.True(target.Turn.HasReaction);
        Assert.True(target.CurrentHitPoints < target.Stats.MaximumHitPoints);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Parries", StringComparison.Ordinal));
    }

    // ---- fixtures ----

    /// <summary>
    /// A dual-mode "Melee or Ranged" attack — a thrown weapon — with both a reach and a
    /// range band, stored (as the extractor stores it) with <see cref="AttackKind.Melee"/>
    /// so its modality is decided by distance rather than by Kind.
    /// </summary>
    private static CombatAttack ThrownAttack(
        int bonus = 4,
        int reachFeet = 5,
        int normalFeet = 20,
        int longFeet = 60,
        string damage = "1d6 + 2",
        DamageType type = DamageType.Piercing)
    {
        var dice = DiceExpression.Parse(damage);

        return new CombatAttack(
            "Javelin",
            AttackKind.Melee,
            bonus,
            reachFeet,
            normalFeet,
            longFeet,
            [new AttackDamage(dice, type, dice.Average)]);
    }

    private static MonsterEntry ParryEntry(int bonus = ParryBonus) =>
        new(
            "Parry",
            MonsterEntrySection.Reaction,
            "Trigger: The bandit is hit by a melee attack roll while holding a weapon. "
                + "Response: The bandit adds 2 to its AC against that attack, possibly causing it to miss.",
            Reaction: new ReactionEffect(
                "The bandit is hit by a melee attack roll while holding a weapon.",
                $"The bandit adds {bonus} to its AC against that attack, possibly causing it to miss.")
            {
                Executable = new ExecutableReaction(ReactionTrigger.HitByMeleeAttack, bonus),
            });

    private static Combatant ParryTarget(
        bool withParry,
        IReadOnlyList<CombatAttack>? attacks,
        int x)
    {
        var stats = CombatTestData.Stats(
            armorClass: 15,
            maximumHitPoints: 30,
            initiativeBonus: -10,
            attacks: attacks ?? [CombatTestData.MeleeAttack(name: "Scimitar", bonus: 4, damage: "1d6 + 2")]);

        if (withParry)
        {
            stats = stats with { Entries = [ParryEntry()] };
        }

        return CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, stats: stats, x: x);
    }

    private static (Encounter Encounter, Combatant Attacker, Combatant Target) ParryFight(
        int attackRoll,
        bool withParry = true,
        CombatAttack? attackerAttack = null,
        IReadOnlyList<CombatAttack>? targetAttacks = null,
        int attackerX = 0,
        int targetX = 1,
        int? advantageDie = null,
        IReadOnlyList<int>? damageRolls = null)
    {
        var attacker = CombatTestData.Combatant(
            "attacker",
            stats: CombatTestData.Stats(
                initiativeBonus: 20,
                attacks: [attackerAttack ?? CombatTestData.MeleeAttack(bonus: 5, damage: "1d4")]),
            x: attackerX);

        var target = ParryTarget(withParry, targetAttacks, targetX);

        // [init, init, attack roll, (second attack die when the roll has Advantage), ...damage].
        int[] attackDice = advantageDie is { } second ? [attackRoll, second] : [attackRoll];
        int[] dice = [10, 10, .. attackDice, .. damageRolls ?? []];
        var encounter = Encounter.Start(new Battlefield(12, 12), [attacker, target], new ScriptedRandomSource(dice));

        return (encounter, attacker, target);
    }

    private static (Encounter Encounter, Combatant First, Combatant Second, Combatant Target) TwoAttackerParryFight(
        int firstRoll,
        int secondRoll,
        int secondDamage)
    {
        var first = CombatTestData.Combatant(
            "first",
            stats: CombatTestData.Stats(initiativeBonus: 20, attacks: [CombatTestData.MeleeAttack(bonus: 5, damage: "1d4")]),
            x: 0);

        var second = CombatTestData.Combatant(
            "second",
            stats: CombatTestData.Stats(initiativeBonus: 15, attacks: [CombatTestData.MeleeAttack(bonus: 5, damage: "1d4")]),
            x: 2);

        var target = ParryTarget(withParry: true, attacks: null, x: 1);

        // Three initiative rolls, then the first attack (parried, no damage), then the
        // second attack and its one damage die.
        int[] dice = [10, 10, 10, firstRoll, secondRoll, secondDamage];
        var encounter = Encounter.Start(
            new Battlefield(12, 12), [first, second, target], new ScriptedRandomSource(dice));

        return (encounter, first, second, target);
    }
}
