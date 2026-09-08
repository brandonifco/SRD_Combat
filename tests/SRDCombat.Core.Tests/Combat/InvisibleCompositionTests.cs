using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// #673's designer reading, §2: "Composition with #672's re-derived readings — no
/// special cases, once every consumer calls <c>CanSee</c>." Frightened, Ranged Attacks
/// in Close Combat and Opportunity Attacks were re-derived against the line-of-sight
/// predicate in #672; none of their call sites changed for #673 — they compose with
/// Invisible for free because they all route through <c>VisionRules.CanSee</c>'s
/// <see cref="Combatant"/>-shaped overload, which now carries the Invisible-defeat
/// clause. These tests are the proof, not an assumption.
/// </summary>
public class InvisibleCompositionTests
{
    [Fact]
    public void AnInvisibleAndUnseenFrightenedSource_GrantsNoDisadvantage()
    {
        // The fear source and the attack's own target are deliberately different
        // creatures: the source (Invisible, unseen) never enters this roll except
        // through the Frightened reading, so a Disadvantage here could only be that
        // reading firing incorrectly, not the separate Unseen-Attacker mechanic an
        // Invisible *target* would trigger of its own accord.
        var field = new Battlefield(8, 8);
        var bearer = CombatTestData.Combatant("bearer", x: 0, y: 0);
        var source = CombatTestData.Combatant("source", sideId: CombatTestData.Monsters, x: 3, y: 0);
        source.AddCondition(ConditionType.Invisible);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 1, y: 0);
        bearer.AddCondition(new ActiveCondition(ConditionType.Frightened, SourceId: source.Id));

        IReadOnlyCollection<Combatant> combatants = [bearer, source, target];

        Assert.False(ConditionRules.FrightenedSourceInSight(bearer, field, combatants));

        var circumstances = AttackRules.DescribeCircumstances(
            bearer, bearer.Stats.Attacks[0], target, combatants, field);

        Assert.False(circumstances.AttackerIsFrightened);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AVisibleFrightenedSource_StillGrantsDisadvantage()
    {
        // The control case: an ordinary (non-Invisible) source in the open still
        // hampers exactly as #672 pinned, so the test above is proving the Invisible
        // clause specifically rather than an accidentally-broken predicate.
        var field = new Battlefield(8, 8);
        var bearer = CombatTestData.Combatant("bearer", x: 0, y: 0);
        var source = CombatTestData.Combatant("source", sideId: CombatTestData.Monsters, x: 3, y: 0);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 1, y: 0);
        bearer.AddCondition(new ActiveCondition(ConditionType.Frightened, SourceId: source.Id));

        IReadOnlyCollection<Combatant> combatants = [bearer, source, target];

        var circumstances = AttackRules.DescribeCircumstances(
            bearer, bearer.Stats.Attacks[0], target, combatants, field);

        Assert.True(circumstances.AttackerIsFrightened);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AHiddenMoverLeavingReach_ProvokesNoOpportunityAttack()
    {
        var field = new Battlefield(8, 8);

        var mover = CombatTestData.Combatant("mover", x: 1, y: 0);
        mover.AddCondition(ConditionType.Invisible);
        mover.Turn.BeginTurn(30);

        var enemy = CombatTestData.Combatant("enemy", sideId: CombatTestData.Monsters, x: 0, y: 0);
        enemy.Turn.BeginTurn(30);

        var attackers = MovementRules.FindOpportunityAttackers(
            field, mover, new GridPosition(1, 0), new GridPosition(3, 0), [mover, enemy]);

        Assert.Empty(attackers);
    }

    [Fact]
    public void AVisibleMoverLeavingReach_StillProvokesAnOpportunityAttack()
    {
        // The control case for the test above: the same geometry with an ordinary
        // mover still provokes, so the empty result is proven to come from Invisible
        // rather than from the geometry alone.
        var field = new Battlefield(8, 8);

        var mover = CombatTestData.Combatant("mover", x: 1, y: 0);
        mover.Turn.BeginTurn(30);

        var enemy = CombatTestData.Combatant("enemy", sideId: CombatTestData.Monsters, x: 0, y: 0);
        enemy.Turn.BeginTurn(30);

        var attackers = MovementRules.FindOpportunityAttackers(
            field, mover, new GridPosition(1, 0), new GridPosition(3, 0), [mover, enemy]);

        Assert.Single(attackers);
    }

    [Fact]
    public void AnInvisibleArcherBesideAFoe_SuffersNoRangedAttackInCloseCombatDisadvantage()
    {
        var field = new Battlefield(8, 8);
        var archer = CombatTestData.Combatant(
            "archer", stats: CombatTestData.Stats(attacks: [CombatTestData.RangedAttack()]), x: 0, y: 0);
        archer.AddCondition(ConditionType.Invisible);
        var foe = CombatTestData.Combatant("foe", sideId: CombatTestData.Monsters, x: 1, y: 0);

        var circumstances = AttackRules.DescribeCircumstances(
            archer, archer.Stats.Attacks[0], foe, [archer, foe], field);

        Assert.False(circumstances.RangedAttackInCloseCombat);
    }

    [Fact]
    public void AVisibleArcherBesideAFoe_StillSuffersRangedAttackInCloseCombatDisadvantage()
    {
        var field = new Battlefield(8, 8);
        var archer = CombatTestData.Combatant(
            "archer", stats: CombatTestData.Stats(attacks: [CombatTestData.RangedAttack()]), x: 0, y: 0);
        var foe = CombatTestData.Combatant("foe", sideId: CombatTestData.Monsters, x: 1, y: 0);

        var circumstances = AttackRules.DescribeCircumstances(
            archer, archer.Stats.Attacks[0], foe, [archer, foe], field);

        Assert.True(circumstances.RangedAttackInCloseCombat);
    }
}
