using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

public class AttackRulesTests
{
    [Fact]
    public void ANatural20_HitsAndCrits_RegardlessOfArmorClass()
    {
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack(bonus: -5)]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(armorClass: 30), x: 1);

        var result = AttackRules.Resolve(
            new ScriptedRandomSource(20),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.True(result.Hit);
        Assert.True(result.Critical);
    }

    [Fact]
    public void AdamantineArmor_DemotesEveryCriticalToANormalHit()
    {
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack(bonus: 5)]));
        var target = CombatTestData.Combatant(
            "b",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(armorClass: 12) with { CriticalHitsAgainstBecomeNormal = true },
            x: 1);

        // "any Critical Hit against you becomes a normal hit" — the natural 20 still
        // hits (that rule is about hitting), but the dice-doubling is denied.
        var result = AttackRules.Resolve(
            new ScriptedRandomSource(20),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.True(result.Hit);
        Assert.False(result.Critical);
    }

    [Fact]
    public void ANatural1_MissesEvenAgainstAHopelesslyLowArmorClass()
    {
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack(bonus: 20)]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(armorClass: 1), x: 1);

        var result = AttackRules.Resolve(
            new ScriptedRandomSource(1),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.False(result.Hit);
        Assert.False(result.Critical);
    }

    [Fact]
    public void AnAttackMeetingArmorClassExactly_Hits()
    {
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack(bonus: 3)]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(armorClass: 13), x: 1);

        // 10 + 3 = 13, which meets AC 13.
        var result = AttackRules.Resolve(new ScriptedRandomSource(10), attacker, attacker.Stats.Attacks[0], target);

        Assert.True(result.Hit);
        Assert.False(result.Critical);
    }

    [Fact]
    public void ADodgingTarget_ImposesDisadvantage()
    {
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.True(circumstances.TargetIsDodging);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void ADodgingTargetThatIsIncapacitated_LosesTheBenefit()
    {
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();
        target.AddCondition(ConditionType.Incapacitated);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.False(circumstances.TargetIsDodging);
    }

    [Fact]
    public void ADodgingTargetThatIsGrappled_LosesTheBenefit()
    {
        // The printed exception's second half: "or if your Speed is 0". Grappled is the
        // Speed-0 condition that does not also bring Incapacitated, so it is the case
        // the Incapacitated check alone would miss.
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();
        target.AddCondition(ConditionType.Grappled);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.False(circumstances.TargetIsDodging);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void ADodgingTargetThatIsBlinded_DoesNotImposeDisadvantage()
    {
        // Dodge's attack-roll half is "if you can see the attacker", and a Blinded
        // dodger cannot — so the attacker keeps the plain Advantage Blinded grants
        // rather than Dodge cancelling it to a normal roll.
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();
        target.AddCondition(ConditionType.Blinded);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.False(circumstances.TargetIsDodging);
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Theory]
    // Prone gives the attacker Advantage up close and Disadvantage from further away.
    [InlineData(5, RollMode.Advantage)]
    [InlineData(10, RollMode.Disadvantage)]
    public void AProneTarget_DependsOnHowCloseTheAttackerIs(int distance, RollMode expected)
    {
        var circumstances = new AttackCircumstances(
            TargetIsDodging: false,
            TargetIsProne: true,
            TargetIsUnconscious: false,
            AttackerIsProne: false);

        Assert.Equal(expected, AttackRules.ResolveRollMode(circumstances, distance));
    }

    [Fact]
    public void AnUnconsciousTargetBeyondFiveFeet_RollsNormally()
    {
        // Worth pinning because it is genuinely counter-intuitive: Unconscious grants
        // Advantage, but Unconscious also means Prone, and Prone gives Disadvantage to
        // an attacker further than 5 feet away. They cancel.
        var circumstances = new AttackCircumstances(
            TargetIsDodging: false,
            TargetIsProne: true,
            TargetIsUnconscious: true,
            AttackerIsProne: false);

        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 15));
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AnyHitOnAnUnconsciousTargetWithinFiveFeet_IsACriticalHit()
    {
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.AddCondition(ConditionType.Unconscious);

        // Two dice, because Unconscious also grants Advantage at this range. Neither is
        // a natural 20 — the Critical Hit comes from the condition alone.
        var result = AttackRules.Resolve(
            new ScriptedRandomSource(12, 11),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.True(result.Hit);
        Assert.True(result.Critical);
    }

    [Fact]
    public void AProneAttacker_HasDisadvantage()
    {
        var circumstances = new AttackCircumstances(AttackerIsProne: true);

        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void APoisonedAttacker_HasDisadvantage()
    {
        var circumstances = new AttackCircumstances(AttackerIsPoisoned: true);

        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void PoisonedCancelsAgainstAdvantageRatherThanOverridingIt()
    {
        // Poisoned is Disadvantage like any other, so it cancels with Advantage instead
        // of winning. Worth pinning because a "poisoned creatures roll badly" shortcut
        // would look right and be wrong exactly here.
        var circumstances = new AttackCircumstances(
            TargetIsDodging: false,
            TargetIsProne: false,
            TargetIsUnconscious: true,
            AttackerIsPoisoned: true);

        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void ShootingBeyondNormalRange_HasDisadvantage()
    {
        var bow = CombatTestData.RangedAttack(normalFeet: 80, longFeet: 320);

        Assert.False(bow.IsAtLongRange(80));
        Assert.True(bow.IsAtLongRange(85));
        Assert.True(bow.IsAtLongRange(320));

        // Beyond long range the attack cannot be made at all, rather than being made
        // with Disadvantage.
        Assert.False(bow.IsAtLongRange(325));
        Assert.False(bow.CanReach(325));
    }

    [Fact]
    public void ConditionalDamage_IsOnlyDealtWhenItsConditionHolds()
    {
        // The Goblin Warrior's "plus 2 (1d4) Slashing damage if the attack roll had
        // Advantage". Treating this as unconditional silently makes the creature hit for
        // half again as much as the SRD says on every ordinary swing.
        var attack = new CombatAttack(
            "Scimitar",
            AttackKind.Melee,
            4,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [
                new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Slashing, 5),
                new AttackDamage(
                    DiceExpression.Parse("1d4"),
                    DamageType.Slashing,
                    2,
                    AttackDamageCondition.AttackRollHadAdvantage),
            ]);

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        // An ordinary hit: only the base component is rolled.
        var plain = AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target);
        Assert.True(plain.Hit);
        Assert.Equal(RollMode.Normal, plain.Roll.Mode);
        Assert.Single(AttackRules.RollDamage(new ScriptedRandomSource(3), attack, plain, attacker, target));

        // The same hit with Advantage: both components are rolled.
        target.AddCondition(ConditionType.Prone);
        var advantaged = AttackRules.Resolve(new ScriptedRandomSource(15, 9), attacker, attack, target);
        Assert.Equal(RollMode.Advantage, advantaged.Roll.Mode);
        Assert.Equal(2, AttackRules.RollDamage(new ScriptedRandomSource(3, 2), attack, advantaged, attacker, target).Count);
    }

    [Fact]
    public void AlternativeDamage_ReplacesTheBaseComponentInsteadOfJoiningIt()
    {
        // The Chimera's Bite (#371), simplified to one round trip: "Hit: 11 (2d6 + 4)
        // Piercing damage, or 18 (4d6 + 4) Piercing damage if the chimera had
        // Advantage on the attack roll." Unlike the goblins' "plus…if" rider above,
        // this is a replacement — the alternative's own damage stands in for the base
        // component whole, never alongside it.
        var attack = new CombatAttack(
            "Bite",
            AttackKind.Melee,
            7,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("2d6 + 4"), DamageType.Piercing, 11)])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("4d6 + 4"),
                DamageType.Piercing,
                18,
                AttackDamageCondition.AttackRollHadAdvantage),
        };

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        // An ordinary hit: exactly the base component, never the alternative alongside it.
        var plain = AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target);
        Assert.Equal(RollMode.Normal, plain.Roll.Mode);
        var plainDamage = AttackRules.RollDamage(new ScriptedRandomSource(3, 4), attack, plain, attacker, target);
        var plainComponent = Assert.Single(plainDamage);
        Assert.Equal(11, plainComponent.Component.PrintedAverage);

        // With Advantage: the alternative replaces the base component — one
        // component rolled, not two, and it is the alternative's own dice.
        target.AddCondition(ConditionType.Prone);
        var advantaged = AttackRules.Resolve(new ScriptedRandomSource(15, 9), attacker, attack, target);
        Assert.Equal(RollMode.Advantage, advantaged.Roll.Mode);
        var advantagedDamage = AttackRules.RollDamage(new ScriptedRandomSource(3, 4, 2, 1), attack, advantaged, attacker, target);
        var advantagedComponent = Assert.Single(advantagedDamage);
        Assert.Equal(18, advantagedComponent.Component.PrintedAverage);
    }

    [Fact]
    public void AlternativeDamage_AttackerIsBloodiedChecksTheAttackerNotTheTarget()
    {
        // A Swarm of Rats' Bites (#371): "Hit: 5 (2d4) Piercing damage, or 2 (1d4)
        // Piercing damage if the swarm is Bloodied." The condition reads the
        // attacker's own Hit Points, not the target's — halved, wounded prey does not
        // make the swarm bite softer.
        var attack = new CombatAttack(
            "Bites",
            AttackKind.Melee,
            2,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("2d4"), DamageType.Piercing, 5)])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("1d4"),
                DamageType.Piercing,
                2,
                AttackDamageCondition.AttackerIsBloodied),
        };

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        Assert.False(attacker.IsBloodied);
        var full = AttackRules.RollDamage(
            new ScriptedRandomSource(3, 3),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Equal(5, Assert.Single(full).Component.PrintedAverage);

        // Wound the attacker below half its own maximum — the target is untouched.
        DamageRules.Apply(
            attacker,
            attacker.Stats.MaximumHitPoints - attacker.Stats.MaximumHitPoints / 2,
            DamageType.Bludgeoning);
        Assert.True(attacker.IsBloodied);
        Assert.False(target.IsBloodied);

        var halved = AttackRules.RollDamage(
            new ScriptedRandomSource(2),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Equal(2, Assert.Single(halved).Component.PrintedAverage);
    }

    [Fact]
    public void AlternativeDamage_TargetIsBloodiedChecksTheTargetNotTheAttacker()
    {
        // The Blood Hawk's Beak (#371): "Hit: 4 (1d4 + 2) Piercing damage, or 6
        // (1d8 + 2) Piercing damage if the target is Bloodied." The opposite reading
        // from the swarms above — a wounded target draws the hawk's bigger hit.
        var attack = new CombatAttack(
            "Beak",
            AttackKind.Melee,
            4,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("1d4 + 2"), DamageType.Piercing, 4)])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("1d8 + 2"),
                DamageType.Piercing,
                6,
                AttackDamageCondition.TargetIsBloodied),
        };

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        Assert.False(target.IsBloodied);
        var full = AttackRules.RollDamage(
            new ScriptedRandomSource(3),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Equal(4, Assert.Single(full).Component.PrintedAverage);

        DamageRules.Apply(
            target,
            target.Stats.MaximumHitPoints - target.Stats.MaximumHitPoints / 2,
            DamageType.Bludgeoning);
        Assert.True(target.IsBloodied);
        Assert.False(attacker.IsBloodied);

        var bigger = AttackRules.RollDamage(
            new ScriptedRandomSource(4),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Equal(6, Assert.Single(bigger).Component.PrintedAverage);
    }

    [Fact]
    public void ADualModeAttackUsedInMelee_IsNotAtLongRange()
    {
        // Nineteen SRD attacks are "Melee or Ranged". Used in melee they carry both a
        // reach and a range, and must not pick up long-range Disadvantage.
        var attack = new CombatAttack(
            "Spear",
            AttackKind.Melee,
            5,
            ReachFeet: 5,
            NormalRangeFeet: 20,
            LongRangeFeet: 60,
            [new AttackDamage(DiceExpression.Parse("1d6 + 3"), DamageType.Piercing, 6)]);

        Assert.False(attack.IsAtLongRange(5));
        Assert.True(attack.IsAtLongRange(40));
        Assert.Equal(60, attack.MaximumRangeFeet);
    }
}
