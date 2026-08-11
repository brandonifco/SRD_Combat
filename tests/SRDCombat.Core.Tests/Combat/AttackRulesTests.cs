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
            AttackerIsProne: false,
            AtLongRange: false);

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
            AttackerIsProne: false,
            AtLongRange: false);

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
        var circumstances = new AttackCircumstances(false, false, false, AttackerIsProne: true, false);

        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
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
