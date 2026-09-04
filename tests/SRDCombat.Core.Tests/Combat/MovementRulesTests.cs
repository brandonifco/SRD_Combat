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
    public void AMoveMayEndOnAFallenAllyButNotOnAFallenEnemy()
    {
        // A house rule, and the engine's one deliberate contradiction of a printed
        // sentence — "you can't willingly end a move in a space occupied by another
        // creature". Asked for during the 2026-08-16 play session: standing over a
        // fallen friend is what a player expects to be able to do, and being refused
        // reads as the grid being broken rather than as a rule.
        var field = new Battlefield(8, 8);
        var mover = CombatTestData.Combatant("mover");

        var friend = CombatTestData.Combatant(
            "friend",
            stats: CombatTestData.Stats(diesAtZeroHitPoints: false),
            x: 2);

        var foe = CombatTestData.Combatant(
            "foe",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(diesAtZeroHitPoints: false),
            x: 2);

        DamageRules.Apply(friend, friend.Stats.MaximumHitPoints, DamageType.Bludgeoning);
        DamageRules.Apply(foe, foe.Stats.MaximumHitPoints, DamageType.Bludgeoning);

        Assert.NotNull(MovementRules.FindPath(field, mover, friend.Position, 30, [mover, friend]));

        // Deliberately not widened to the enemy: the request was about a comrade, and a
        // monster able to stop on the body it is trying to get past would delete the
        // only scenario the stuck-turn last resort is tested against.
        Assert.Null(MovementRules.FindPath(field, mover, foe.Position, 30, [mover, foe]));
    }

    [Fact]
    public void AnAbleAllyStillBlocksTheEndOfAMove()
    {
        // The house rule is scoped to the *fallen*. An ally on their feet is still
        // somewhere you may pass through and not somewhere you may stop.
        var field = new Battlefield(8, 8);
        var mover = CombatTestData.Combatant("mover");
        var friend = CombatTestData.Combatant("friend", x: 2);

        Assert.Null(MovementRules.FindPath(field, mover, friend.Position, 30, [mover, friend]));
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

    /// <summary>
    /// #493's load-bearing correction: <c>StepCostFeet</c> is the one cost authority both
    /// <see cref="MovementRules.FindPath"/> and <c>Encounter.WalkPath</c> price a step with, so
    /// a route's <see cref="MovementPath.CostFeet"/> and a partial walk's running spend can
    /// never disagree. Summing it over a full path must equal <c>CostFeet</c> exactly — the
    /// trip-wire CLAUDE.md's #412 doctrine asks for.
    /// </summary>
    [Fact]
    public void StepCostFeet_SumsToPathCostFeet_AcrossADoubleCostOccupiedSquare()
    {
        // A downed enemy's square is passable (Incapacitated) but double cost (not an
        // ally) — the exact shape #493's partial-spend fix targets: a stop mid-route that
        // has already crossed one of these must charge the walked feet correctly.
        var field = new Battlefield(3, 1);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);
        var enemy = CombatTestData.Combatant("enemy", sideId: CombatTestData.Monsters, x: 1, y: 0);

        enemy.AddCondition(ConditionType.Unconscious);

        var path = MovementRules.FindPath(field, mover, new GridPosition(2, 0), 30, [mover, enemy]);
        Assert.NotNull(path);

        var sum = 0;
        var from = mover.Position;

        foreach (var step in path!.Steps)
        {
            sum += MovementRules.StepCostFeet(field, mover, from, step, [mover, enemy]);
            from = step;
        }

        Assert.Equal(path.CostFeet, sum);
        Assert.Equal(15, sum); // 5 clear + 10 double — matches FindPath_MayPassThroughADownedEnemyButNeverEndOnOne.
    }

    /// <summary>
    /// The shape that makes the <c>from</c> parameter load-bearing rather than a
    /// convenience: a multi-square mover whose <see cref="MovementRules.FindPath"/> search
    /// node differs from <c>mover.Position</c> past the first step (the search never calls
    /// <c>MoveTo</c>, so <c>mover.Position</c> stays pinned at the true start for the whole
    /// search). A trip-wire built only from a one-square walker cannot see a <c>from</c> bug —
    /// <c>entered</c> reduces to <c>{step}</c> either way for a single-square body — so this
    /// exercises the same difficult-terrain ogre <see cref="FootprintMovementTests"/> pins.
    /// </summary>
    [Fact]
    public void StepCostFeet_SumsToPathCostFeet_ForAMultiSquareMoverAcrossMultipleSteps()
    {
        var field = new Battlefield(6, 4, difficultTerrain: [new GridPosition(2, 1)]);
        var ogre = CombatTestData.Combatant("ogre", stats: CombatTestData.Stats(size: CreatureSize.Large));

        var path = MovementRules.FindPath(field, ogre, new GridPosition(2, 0), 30, [ogre]);

        Assert.NotNull(path);
        Assert.Equal(2, path!.Steps.Count);
        Assert.Equal(15, path.CostFeet);

        var sum = 0;
        var from = ogre.Position;

        foreach (var step in path.Steps)
        {
            sum += MovementRules.StepCostFeet(field, ogre, from, step, [ogre]);
            from = step;
        }

        Assert.Equal(path.CostFeet, sum);
    }

    /// <summary>
    /// The public <c>StepCostFeet</c> overload builds its own occupants lookup — a second
    /// place, alongside <see cref="FindPath"/>'s, that must apply the same two exclusions
    /// (self, dead) or the two routes to a step's price stop agreeing <em>by construction</em>
    /// and agree only <em>by test</em>. A dead creature is the one this trip-wire can actually
    /// catch going missing: unlike excluding the mover itself — which turns out to be inert
    /// for pricing, since a creature's <c>SideId</c> trivially equals its own, so a
    /// self-occupied square can never fail the "not an ally" check — a genuinely dead
    /// creature (as opposed to merely Incapacitated, <see
    /// cref="StepCostFeet_SumsToPathCostFeet_AcrossADoubleCostOccupiedSquare"/>'s shape) is
    /// excluded from occupancy entirely by both <see cref="FindPath"/> and the correct
    /// wrapper: its square costs the plain rate, not double. Drop that exclusion from the
    /// wrapper alone and this square silently doubles while <see cref="FindPath"/>'s own
    /// <see cref="MovementPath.CostFeet"/> — computed by the untouched private core, which
    /// never rebuilds this lookup — does not, so the two disagree.
    /// </summary>
    [Fact]
    public void StepCostFeet_SumsToPathCostFeet_ThroughAGenuinelyDeadCreaturesSquare()
    {
        var field = new Battlefield(3, 1);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);
        var corpse = CombatTestData.Combatant("corpse", sideId: CombatTestData.Monsters, x: 1, y: 0);

        DamageRules.Apply(corpse, corpse.Stats.MaximumHitPoints, DamageType.Bludgeoning);
        Assert.True(corpse.IsDead);

        var path = MovementRules.FindPath(field, mover, new GridPosition(2, 0), 30, [mover, corpse]);
        Assert.NotNull(path);

        var sum = 0;
        var from = mover.Position;

        foreach (var step in path!.Steps)
        {
            sum += MovementRules.StepCostFeet(field, mover, from, step, [mover, corpse]);
            from = step;
        }

        Assert.Equal(path.CostFeet, sum);
        Assert.Equal(10, sum); // 5 + 5 — a corpse costs nothing extra, unlike the merely-downed shape above.
    }
}
