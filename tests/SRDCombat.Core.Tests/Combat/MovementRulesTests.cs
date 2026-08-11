using SRDCombat.Core.Combat;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

public class MovementRulesTests
{
    [Fact]
    public void FindPath_TakesDiagonalsBecauseTheyCostTheSame()
    {
        var field = new Battlefield(6, 6);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);

        var path = MovementRules.FindPath(field, mover, new GridPosition(3, 3), 30, [mover]);

        Assert.NotNull(path);
        Assert.Equal(3, path.Steps.Count);
        Assert.Equal(15, path.CostFeet);
    }

    [Fact]
    public void FindPath_RefusesADestinationBeyondTheMovementBudget()
    {
        var field = new Battlefield(10, 10);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);

        Assert.Null(MovementRules.FindPath(field, mover, new GridPosition(9, 0), 30, [mover]));
    }

    [Fact]
    public void FindPath_RoutesAroundBlockedSquares()
    {
        // A wall across the middle with one gap; the route has to use the gap.
        var wall = Enumerable.Range(0, 4).Select(y => new GridPosition(2, y)).ToArray();
        var field = new Battlefield(5, 5, blocked: wall);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);

        var path = MovementRules.FindPath(field, mover, new GridPosition(4, 0), 60, [mover]);

        Assert.NotNull(path);
        Assert.DoesNotContain(new GridPosition(2, 0), path.Steps);
        Assert.Contains(new GridPosition(2, 4), path.Steps);
    }

    [Fact]
    public void FindPath_ChargesDoubleForDifficultTerrain()
    {
        var field = new Battlefield(4, 1, difficultTerrain: [new GridPosition(1, 0), new GridPosition(2, 0)]);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);

        var path = MovementRules.FindPath(field, mover, new GridPosition(3, 0), 30, [mover]);

        // 10 + 10 + 5 rather than 15.
        Assert.Equal(25, path?.CostFeet);
    }

    [Fact]
    public void FindPath_WillNotEndOnAnotherCreature()
    {
        var field = new Battlefield(5, 1);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);
        var ally = CombatTestData.Combatant("ally", x: 2, y: 0);

        Assert.Null(MovementRules.FindPath(field, mover, new GridPosition(2, 0), 30, [mover, ally]));
    }

    [Fact]
    public void FindPath_MayPassThroughAnAllyButNotAnEnemy()
    {
        var field = new Battlefield(3, 1);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);
        var ally = CombatTestData.Combatant("ally", x: 1, y: 0);
        var enemy = CombatTestData.Combatant("enemy", sideId: CombatTestData.Monsters, x: 1, y: 0);

        // Through the ally: allowed, and the occupied square costs double.
        var throughAlly = MovementRules.FindPath(field, mover, new GridPosition(2, 0), 30, [mover, ally]);
        Assert.Equal(15, throughAlly?.CostFeet);

        // Through the enemy on a one-square-wide corridor: no route at all.
        Assert.Null(MovementRules.FindPath(field, mover, new GridPosition(2, 0), 30, [mover, enemy]));
    }

    [Fact]
    public void MeleeReachFeet_UsesTheLongestMeleeAttack()
    {
        var combatant = CombatTestData.Combatant(
            "m",
            stats: CombatTestData.Stats(attacks:
            [
                CombatTestData.MeleeAttack("Claw", reachFeet: 5),
                CombatTestData.MeleeAttack("Tail", reachFeet: 10),
            ]));

        Assert.Equal(10, MovementRules.MeleeReachFeet(combatant));
    }

    [Fact]
    public void FindOpportunityAttackers_FiresOnlyWhenReachIsActuallyLeft()
    {
        var mover = CombatTestData.Combatant("m", x: 1, y: 0);
        var enemy = CombatTestData.Combatant("e", sideId: CombatTestData.Monsters, x: 0, y: 0);
        mover.Turn.BeginTurn(30);
        enemy.Turn.BeginTurn(30);

        // Stepping from adjacent to two squares away leaves reach.
        Assert.Single(MovementRules.FindOpportunityAttackers(
            mover,
            new GridPosition(1, 0),
            new GridPosition(2, 0),
            [mover, enemy]));

        // Sidestepping while staying adjacent does not.
        Assert.Empty(MovementRules.FindOpportunityAttackers(
            mover,
            new GridPosition(1, 0),
            new GridPosition(1, 1),
            [mover, enemy]));

        // Moving between two squares that were both already out of reach does not.
        Assert.Empty(MovementRules.FindOpportunityAttackers(
            mover,
            new GridPosition(5, 0),
            new GridPosition(6, 0),
            [mover, enemy]));
    }

    [Fact]
    public void FindOpportunityAttackers_IsAvoidedByDisengaging()
    {
        var mover = CombatTestData.Combatant("m", x: 1, y: 0);
        var enemy = CombatTestData.Combatant("e", sideId: CombatTestData.Monsters, x: 0, y: 0);
        mover.Turn.BeginTurn(30);
        enemy.Turn.BeginTurn(30);
        mover.Turn.Disengage();

        Assert.Empty(MovementRules.FindOpportunityAttackers(
            mover,
            new GridPosition(1, 0),
            new GridPosition(2, 0),
            [mover, enemy]));
    }

    [Fact]
    public void FindOpportunityAttackers_NeedsAnAvailableReaction()
    {
        var mover = CombatTestData.Combatant("m", x: 1, y: 0);
        var enemy = CombatTestData.Combatant("e", sideId: CombatTestData.Monsters, x: 0, y: 0);
        mover.Turn.BeginTurn(30);
        enemy.Turn.BeginTurn(30);
        enemy.Turn.SpendReaction();

        Assert.Empty(MovementRules.FindOpportunityAttackers(
            mover,
            new GridPosition(1, 0),
            new GridPosition(2, 0),
            [mover, enemy]));
    }

    [Fact]
    public void StandUpCostFeet_IsHalfSpeedRoundedDown()
    {
        Assert.Equal(15, MovementRules.StandUpCostFeet(
            CombatTestData.Combatant("m", stats: CombatTestData.Stats(speedFeet: 30))));

        Assert.Equal(12, MovementRules.StandUpCostFeet(
            CombatTestData.Combatant("m", stats: CombatTestData.Stats(speedFeet: 25))));
    }
}
