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
        var field = new Battlefield(8, 8);
        var mover = CombatTestData.Combatant("m", x: 1, y: 0);
        var enemy = CombatTestData.Combatant("e", sideId: CombatTestData.Monsters, x: 0, y: 0);
        mover.Turn.BeginTurn(30);
        enemy.Turn.BeginTurn(30);

        // Stepping from adjacent to two squares away leaves reach.
        Assert.Single(MovementRules.FindOpportunityAttackers(
            field,
            mover,
            new GridPosition(1, 0),
            new GridPosition(2, 0),
            [mover, enemy]));

        // Sidestepping while staying adjacent does not.
        Assert.Empty(MovementRules.FindOpportunityAttackers(
            field,
            mover,
            new GridPosition(1, 0),
            new GridPosition(1, 1),
            [mover, enemy]));

        // Moving between two squares that were both already out of reach does not.
        Assert.Empty(MovementRules.FindOpportunityAttackers(
            field,
            mover,
            new GridPosition(5, 0),
            new GridPosition(6, 0),
            [mover, enemy]));
    }

    [Fact]
    public void FindOpportunityAttackers_IsAvoidedByDisengaging()
    {
        var field = new Battlefield(8, 8);
        var mover = CombatTestData.Combatant("m", x: 1, y: 0);
        var enemy = CombatTestData.Combatant("e", sideId: CombatTestData.Monsters, x: 0, y: 0);
        mover.Turn.BeginTurn(30);
        enemy.Turn.BeginTurn(30);
        mover.Turn.Disengage();

        Assert.Empty(MovementRules.FindOpportunityAttackers(
            field,
            mover,
            new GridPosition(1, 0),
            new GridPosition(2, 0),
            [mover, enemy]));
    }

    [Fact]
    public void FindOpportunityAttackers_NeedsAnAvailableReaction()
    {
        var field = new Battlefield(8, 8);
        var mover = CombatTestData.Combatant("m", x: 1, y: 0);
        var enemy = CombatTestData.Combatant("e", sideId: CombatTestData.Monsters, x: 0, y: 0);
        mover.Turn.BeginTurn(30);
        enemy.Turn.BeginTurn(30);
        enemy.Turn.SpendReaction();

        Assert.Empty(MovementRules.FindOpportunityAttackers(
            field,
            mover,
            new GridPosition(1, 0),
            new GridPosition(2, 0),
            [mover, enemy]));
    }

    [Fact]
    public void FindOpportunityAttackers_ABlindedEnemyMakesNone()
    {
        // #672: "a creature that you can see leaves your reach" — a Blinded enemy has
        // no line of sight to the mover's square, so it gets no Opportunity Attack even
        // though nothing else about the geometry changed.
        var field = new Battlefield(8, 8);
        var mover = CombatTestData.Combatant("m", x: 1, y: 0);
        var enemy = CombatTestData.Combatant("e", sideId: CombatTestData.Monsters, x: 0, y: 0);
        mover.Turn.BeginTurn(30);
        enemy.Turn.BeginTurn(30);
        enemy.AddCondition(ConditionType.Blinded);

        Assert.Empty(MovementRules.FindOpportunityAttackers(
            field,
            mover,
            new GridPosition(1, 0),
            new GridPosition(2, 0),
            [mover, enemy]));

        enemy.RemoveCondition(ConditionType.Blinded);

        Assert.Single(MovementRules.FindOpportunityAttackers(
            field,
            mover,
            new GridPosition(1, 0),
            new GridPosition(2, 0),
            [mover, enemy]));
    }

    [Fact]
    public void FindOpportunityAttackers_AWallBlockingSightMakesNone()
    {
        // The same trigger, gated by geometry rather than a condition: reach alone
        // (T1's invariant means an adjacent square can never be walled off, so this
        // needs a 10-foot reach to leave room for a wall in between) would fire here,
        // but a wall directly between the reacher and the square the mover is leaving
        // denies it sight of that square.
        var field = new Battlefield(8, 8, blocked: [new(1, 0)]);
        var reacher = CombatTestData.Combatant(
            "e",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack("Halberd", reachFeet: 10)]),
            x: 0,
            y: 0);
        var mover = CombatTestData.Combatant("m", x: 2, y: 0);
        mover.Turn.BeginTurn(30);
        reacher.Turn.BeginTurn(30);

        Assert.Empty(MovementRules.FindOpportunityAttackers(
            field,
            mover,
            new GridPosition(2, 0),
            new GridPosition(3, 0),
            [mover, reacher]));

        // Unblock the wall and the same step provokes — isolating the wall, not the
        // reach or distance, as what made the difference.
        var openField = new Battlefield(8, 8);

        Assert.Single(MovementRules.FindOpportunityAttackers(
            openField,
            mover,
            new GridPosition(2, 0),
            new GridPosition(3, 0),
            [mover, reacher]));
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

    /// <summary>
    /// The <see cref="MovementRules.Reachable"/> seam's pin (#726): the one drained
    /// search answers <em>exactly</em> the squares <see cref="MovementRules.FindPath"/>
    /// returns a route for, asked square by square over the whole board.
    /// </summary>
    /// <remarks>
    /// This is the whole safety argument for replacing a 504-<c>FindPath</c> loop with
    /// one search, and it is deliberately a comparison rather than a list of expected
    /// squares: a list can be wrong in the same direction as the code. Each caller below
    /// <em>also</em> asserts the concrete set, because two implementations that agree on
    /// "nothing is reachable" agree vacuously.
    /// </remarks>
    private static IReadOnlySet<GridPosition> AssertReachableMatchesFindPath(
        Battlefield field,
        Combatant mover,
        int budgetFeet,
        IReadOnlyCollection<Combatant> combatants)
    {
        var reachable = MovementRules.Reachable(field, mover, budgetFeet, combatants);

        foreach (var square in field.AllSquares())
        {
            var routed = MovementRules.FindPath(field, mover, square, budgetFeet, combatants) is not null;
            var included = reachable.Contains(square);

            Assert.True(
                routed == included,
                $"{square}: FindPath {(routed ? "routes there" : "refuses it")}, "
                + $"Reachable {(included ? "includes it" : "leaves it out")}.");
        }

        return reachable;
    }

    [Fact]
    public void Reachable_MatchesFindPath_OnOpenGround()
    {
        var field = new Battlefield(20, 20);
        var mover = CombatTestData.Combatant("m", x: 10, y: 10);

        var reachable = AssertReachableMatchesFindPath(field, mover, 30, [mover]);

        // Thirty feet is six squares, diagonals cost the same as straight steps, so the
        // reach is a 13 × 13 block centred on the mover — less the mover's own square,
        // which is not a destination.
        Assert.Equal((13 * 13) - 1, reachable.Count);
        Assert.DoesNotContain(mover.Position, reachable);
        Assert.Contains(new GridPosition(16, 16), reachable);
        Assert.DoesNotContain(new GridPosition(17, 16), reachable);
    }

    [Fact]
    public void Reachable_MatchesFindPath_AcrossDifficultTerrain()
    {
        // Three squares of rough ground east of the mover, at ten feet each: thirty feet
        // buys three squares here where it would buy six on open ground.
        var field = new Battlefield(
            9,
            1,
            difficultTerrain: [new GridPosition(1, 0), new GridPosition(2, 0), new GridPosition(3, 0)]);

        var mover = CombatTestData.Combatant("m", x: 0, y: 0);

        var reachable = AssertReachableMatchesFindPath(field, mover, 30, [mover]);

        Assert.Equal(
            [new GridPosition(1, 0), new GridPosition(2, 0), new GridPosition(3, 0)],
            reachable.OrderBy(square => square.X).ToArray());
    }

    [Fact]
    public void Reachable_MatchesFindPath_ThroughOccupiedSquares()
    {
        // A one-square corridor holding all three end-of-move answers at once: an able
        // ally (walk through, never stop), a downed ally (walk through and stop, the
        // engine's one house rule), and a downed enemy (walk through, never stop). The
        // set that comes back is deliberately not contiguous.
        var field = new Battlefield(7, 1);
        var mover = CombatTestData.Combatant("m", x: 0, y: 0);
        var ally = CombatTestData.Combatant("ally", x: 1, y: 0);
        var downedAlly = CombatTestData.Combatant("downed-ally", x: 3, y: 0);
        var downedEnemy = CombatTestData.Combatant("downed-enemy", sideId: CombatTestData.Monsters, x: 5, y: 0);

        downedAlly.AddCondition(ConditionType.Unconscious);
        downedEnemy.AddCondition(ConditionType.Unconscious);

        Combatant[] everyone = [mover, ally, downedAlly, downedEnemy];

        var reachable = AssertReachableMatchesFindPath(field, mover, 60, everyone);

        Assert.Equal(
            [
                new GridPosition(2, 0),
                new GridPosition(3, 0),
                new GridPosition(4, 0),
                new GridPosition(6, 0),
            ],
            reachable.OrderBy(square => square.X).ToArray());
    }

    [Fact]
    public void Reachable_MatchesFindPath_ForALargeBody()
    {
        // FootprintMovementTests' gap fixture: a wall down column 2 with a single open
        // square at (2,2). A Large body needs two adjacent open squares in that column
        // and never gets them, so the whole eastern half is unreachable however much
        // movement it has — and the answer must be the same set whichever way it is
        // asked.
        var wall = Enumerable.Range(0, 5)
            .Where(y => y != 2)
            .Select(y => new GridPosition(2, y))
            .ToArray();

        var field = new Battlefield(6, 5, blocked: wall);

        var ogre = CombatTestData.Combatant(
            "ogre",
            stats: CombatTestData.Stats(size: CreatureSize.Large),
            x: 0,
            y: 2);

        var reachable = AssertReachableMatchesFindPath(field, ogre, 60, [ogre]);

        // Only anchors in column 0 keep the whole 2 × 2 body clear of the wall, and the
        // southernmost anchor is y = 3 because y = 4 would hang the body off the board.
        Assert.Equal(
            [new GridPosition(0, 0), new GridPosition(0, 1), new GridPosition(0, 3)],
            reachable.OrderBy(square => square.Y).ToArray());
    }

    [Fact]
    public void Reachable_MatchesFindPath_AtTheBoardEdge()
    {
        // Nothing blocks this board; the edge does. A Large creature's anchor is its
        // north-west square, so the last column and the last row hold no legal anchor
        // at all, and a reachability answer that forgot the footprint would light them.
        var field = new Battlefield(5, 5);

        var ogre = CombatTestData.Combatant(
            "ogre",
            stats: CombatTestData.Stats(size: CreatureSize.Large),
            x: 0,
            y: 0);

        var reachable = AssertReachableMatchesFindPath(field, ogre, 60, [ogre]);

        Assert.Equal((4 * 4) - 1, reachable.Count);
        Assert.DoesNotContain(new GridPosition(4, 1), reachable);
        Assert.DoesNotContain(new GridPosition(1, 4), reachable);
        Assert.Contains(new GridPosition(3, 3), reachable);
    }

    [Fact]
    public void Reachable_IsEmptyWithNothingLeftToSpend()
    {
        // A turn with its movement spent reaches nowhere — and in particular does not
        // reach the square the mover is standing on, which is not a destination.
        var field = new Battlefield(5, 5);
        var mover = CombatTestData.Combatant("m", x: 2, y: 2);

        Assert.Empty(AssertReachableMatchesFindPath(field, mover, 0, [mover]));
    }
}
