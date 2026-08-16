using SRDCombat.Core.Definitions;
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

        // Through the ally: allowed, and it costs the ordinary five feet a square. The
        // printed Difficult Terrain clause for another creature's space exempts allies
        // — "unless that creature is Tiny or your ally" — so two steps are ten feet.
        var throughAlly = MovementRules.FindPath(field, mover, new GridPosition(2, 0), 30, [mover, ally]);
        Assert.Equal(10, throughAlly?.CostFeet);

        // Through an able enemy on a one-square-wide corridor: no route at all.
        Assert.Null(MovementRules.FindPath(field, mover, new GridPosition(2, 0), 30, [mover, enemy]));
    }

    [Fact]
    public void FindPath_MayPassThroughADownedEnemyButNeverEndOnOne()
    {
        // "During your move, you can pass through the space of ... a creature that has
        // the Incapacitated condition" — the printed clause names a condition and not a
        // side, so a body in a doorway stops walling a corridor off.
        var field = new Battlefield(3, 1);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);
        var enemy = CombatTestData.Combatant("enemy", sideId: CombatTestData.Monsters, x: 1, y: 0);

        enemy.AddCondition(ConditionType.Unconscious);

        // Passable — and still Difficult Terrain, because the exemption is for allies
        // and this is not one: five feet for the clear square, ten for the body's.
        var through = MovementRules.FindPath(field, mover, new GridPosition(2, 0), 30, [mover, enemy]);
        Assert.Equal(15, through?.CostFeet);

        // "You can't willingly end a move in a space occupied by another creature."
        // Ending on the body stays refused however incapable its occupant is, which is
        // what makes it impossible for it to wake up underneath somebody.
        Assert.Null(MovementRules.FindPath(field, mover, new GridPosition(1, 0), 30, [mover, enemy]));
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

    [Fact]
    public void ADownedCreatureStillOccupiesItsSquare()
    {
        // Reading occupancy as "active" let a creature end its move standing on an
        // unconscious one. That was invisible until healing existed: the downed creature
        // then stood up inside somebody else, and the next path finder found two
        // combatants in one square and threw.
        var mover = CombatTestData.Combatant("mover");
        // A character rather than a monster: a monster dies at 0 hit points, and the
        // dead deliberately do not block — it is the unconscious who still take up room.
        var downed = CombatTestData.Combatant(
            "downed",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(diesAtZeroHitPoints: false),
            x: 2);

        DamageRules.Apply(downed, downed.Stats.MaximumHitPoints, DamageType.Bludgeoning);

        Assert.False(downed.IsDead);
        Assert.False(downed.IsActive);

        var onto = MovementRules.FindPath(
            new Battlefield(8, 8),
            mover,
            downed.Position,
            budgetFeet: 30,
            [mover, downed]);

        Assert.Null(onto);
    }

    [Fact]
    public void PathfindingSurvivesTwoCombatantsInOneSquare()
    {
        // Whatever produces it, a path finder that throws is the worst possible failure
        // mode — it takes down a whole run mid-fight rather than picking a square.
        var mover = CombatTestData.Combatant("mover");
        var first = CombatTestData.Combatant("first", sideId: CombatTestData.Monsters, x: 3);
        var second = CombatTestData.Combatant("second", sideId: CombatTestData.Monsters, x: 3);

        var path = MovementRules.FindPath(
            new Battlefield(8, 8),
            mover,
            new GridPosition(1, 0),
            budgetFeet: 30,
            [mover, first, second]);

        Assert.NotNull(path);
    }
}
