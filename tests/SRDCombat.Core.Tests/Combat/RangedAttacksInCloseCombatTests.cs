using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Ranged Attacks in Close Combat, printed page 15: "When you make a ranged attack roll
/// with a weapon, a spell, or some other means, you have Disadvantage on the roll if you
/// are within 5 feet of an enemy who can see you and doesn't have the Incapacitated
/// condition."
/// </summary>
/// <remarks>
/// Sight is unmodelled, so "who can see you" is read as any enemy without the Blinded
/// condition — the same shape of reading Frightened records. The rule was missing until
/// the Godot client's probe walked an archer adjacent to its target and shot with a flat
/// roll (#96).
/// </remarks>
public class RangedAttacksInCloseCombatTests
{
    [Fact]
    public void ARangedAttackBesideItsTarget_HasDisadvantage()
    {
        var (archer, bow, target) = Archery(targetDistanceSquares: 1);

        var circumstances = AttackRules.DescribeCircumstances(archer, bow, target, [archer, target]);

        Assert.True(circumstances.RangedAttackInCloseCombat);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AnyAdjacentEnemyCounts_NotJustTheTarget()
    {
        // The target stands 30 feet off; a second enemy is breathing down the archer's
        // neck. The printed rule says "an enemy", not "the target".
        var (archer, bow, target) = Archery(targetDistanceSquares: 6);
        var bystander = CombatTestData.Combatant("harasser", sideId: CombatTestData.Monsters, x: 0, y: 1);

        var circumstances = AttackRules.DescribeCircumstances(archer, bow, target, [archer, target, bystander]);

        Assert.True(circumstances.RangedAttackInCloseCombat);
    }

    [Fact]
    public void AnIncapacitatedNeighbour_ImposesNothing()
    {
        var (archer, bow, target) = Archery(targetDistanceSquares: 1);
        target.AddCondition(ConditionType.Incapacitated);

        var circumstances = AttackRules.DescribeCircumstances(archer, bow, target, [archer, target]);

        Assert.False(circumstances.RangedAttackInCloseCombat);
    }

    [Fact]
    public void ABlindedNeighbour_CannotSeeTheArcher_AndImposesNothing()
    {
        var (archer, bow, target) = Archery(targetDistanceSquares: 1);
        target.AddCondition(ConditionType.Blinded);

        var circumstances = AttackRules.DescribeCircumstances(archer, bow, target, [archer, target]);

        Assert.False(circumstances.RangedAttackInCloseCombat);
        // The Blinded target still grants Advantage against, so the roll swings the
        // other way rather than merely back to normal.
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AnAdjacentAlly_ImposesNothing()
    {
        var (archer, bow, target) = Archery(targetDistanceSquares: 6);
        var friend = CombatTestData.Combatant("friend", sideId: CombatTestData.Heroes, x: 0, y: 1);

        var circumstances = AttackRules.DescribeCircumstances(archer, bow, target, [archer, target, friend]);

        Assert.False(circumstances.RangedAttackInCloseCombat);
    }

    [Fact]
    public void AMeleeAttackBesideAnEnemy_IsUntouched()
    {
        var swordsman = CombatTestData.Combatant("swordsman");
        var target = CombatTestData.Combatant("orc", sideId: CombatTestData.Monsters, x: 1);

        var circumstances = AttackRules.DescribeCircumstances(
            swordsman, swordsman.Stats.Attacks[0], target, [swordsman, target]);

        Assert.False(circumstances.RangedAttackInCloseCombat);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Theory]
    // A dual-mode attack ("Melee or Ranged Attack Roll") inside its reach is the melee
    // use and rolls clean; thrown from further out it is a ranged attack roll, and the
    // enemy still standing beside the thrower puts it at Disadvantage.
    [InlineData(1, false)]
    [InlineData(4, true)]
    public void ADualModeAttack_RollsRangedOnlyBeyondItsReach(int targetDistanceSquares, bool expected)
    {
        var javelin = new CombatAttack(
            "Javelin",
            AttackKind.Melee,
            AttackBonus: 4,
            ReachFeet: 5,
            NormalRangeFeet: 30,
            LongRangeFeet: 120,
            [new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Piercing, 5)]);

        var thrower = CombatTestData.Combatant("thrower", stats: CombatTestData.Stats(attacks: [javelin]));
        var target = CombatTestData.Combatant("far", sideId: CombatTestData.Monsters, x: targetDistanceSquares);
        var harasser = CombatTestData.Combatant("near", sideId: CombatTestData.Monsters, x: 0, y: 1);

        var circumstances = AttackRules.DescribeCircumstances(thrower, javelin, target, [thrower, target, harasser]);

        Assert.Equal(expected, circumstances.RangedAttackInCloseCombat);
    }

    [Fact]
    public void TheEncounterWiresTheWholeField_AndNarratesTheDisadvantage()
    {
        // Through Encounter.Attack rather than the rules directly, so this pins the
        // _combatants plumbing: two d20s are consumed (7 then 15, keeping the 7 for a
        // miss that rolls no damage) and the narration says why.
        var archerStats = CombatTestData.Stats(
            initiativeBonus: 10,
            attacks: [CombatTestData.RangedAttack("Shortbow", bonus: 5)]);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                CombatTestData.Combatant("archer", stats: archerStats),
                CombatTestData.Combatant(
                    "brute",
                    sideId: CombatTestData.Monsters,
                    stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, attacks: []),
                    x: 1),
            ],
            new ScriptedRandomSource(20, 1, 7, 15));

        var brute = encounter.Combatants.Single(combatant => combatant.Id == "brute");

        Assert.Null(encounter.Attack("Shortbow", brute));

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack
                && step.Narration.Contains("with Disadvantage", StringComparison.Ordinal));
    }

    private static (Combatant Archer, CombatAttack Bow, Combatant Target) Archery(int targetDistanceSquares)
    {
        var bow = CombatTestData.RangedAttack("Shortbow", bonus: 5);
        var archer = CombatTestData.Combatant("archer", stats: CombatTestData.Stats(attacks: [bow]));
        var target = CombatTestData.Combatant("orc", sideId: CombatTestData.Monsters, x: targetDistanceSquares);

        return (archer, bow, target);
    }
}
