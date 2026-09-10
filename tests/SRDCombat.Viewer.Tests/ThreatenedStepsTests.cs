using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="PlayMode.ThreatenedSteps"/> (#301) — the plain-value seam behind the
/// board's Opportunity-Attack threat marking. Like <see cref="PlayMode.HoverPreviewPath"/>
/// before it, the decision this issue is actually about — which squares of a previewed
/// route provoke — touches no live Godot node: it is
/// <see cref="MovementRules.FindOpportunityAttackers"/> called with the real from/to pair
/// for each step of the route in turn, filtered to whichever enemies are passed in as
/// visible, so it is pinned here directly.
/// </summary>
public class ThreatenedStepsTests
{
    private static readonly IReadOnlySet<GridPosition> NoFog = new HashSet<GridPosition>();

    /// <summary>
    /// A melee attacker with a chosen reach, standing off the mover's row so distance
    /// varies with the mover's position along it (Chebyshev distance, five feet a
    /// square — <c>CreatureSpace.DistanceFeetTo</c>'s own remarks).
    /// </summary>
    private static Combatant Reacher(string id, int x, int y, int reachFeet) =>
        new(
            id,
            id,
            FightTestData.Monsters,
            FightTestData.Stats(attacks:
            [
                new CombatAttack(
                    "Weapon",
                    AttackKind.Melee,
                    AttackBonus: 4,
                    ReachFeet: reachFeet,
                    NormalRangeFeet: null,
                    LongRangeFeet: null,
                    [new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Slashing, 5)]),
            ]),
            new GridPosition(x, y));

    /// <summary>
    /// A straight ten-square walk along <c>y = 0</c>, past a five-foot reacher at
    /// <c>x = 3</c> (threatens <c>x</c> 2-4, so leaving at the step to <c>x = 5</c>
    /// provokes) and a ten-foot reacher at <c>x = 7</c> (threatens <c>x</c> 5-9, so
    /// leaving at the step to <c>x = 10</c> provokes) — two different reaches, two
    /// different steps, each fired only once, worked out by hand in the PR and then
    /// checked here against the engine's own predicate rather than trusted.
    /// </summary>
    private static (Battlefield Field, Combatant Mover, Combatant Near, Combatant Far, IReadOnlyList<GridPosition> Path)
        Fixture()
    {
        var field = new Battlefield(12, 3);
        var mover = FightTestData.Combatant("Mover", sideId: FightTestData.Heroes, x: 0);
        var near = Reacher("Near", x: 3, y: 1, reachFeet: 5);
        var far = Reacher("Far", x: 7, y: 1, reachFeet: 10);
        var path = Enumerable.Range(1, 10).Select(x => new GridPosition(x, 0)).ToList();

        return (field, mover, near, far, path);
    }

    /// <summary>
    /// The ground truth every assertion below is checked against: walking
    /// <paramref name="path"/> from <paramref name="mover"/>'s own position and asking
    /// <see cref="MovementRules.FindOpportunityAttackers"/> for each step directly,
    /// independently of <see cref="PlayMode.ThreatenedSteps"/>'s own loop — so this test
    /// cannot pass merely because the two loops share a typo. Mirrors the production
    /// function's own fog rule: the walk is judged on the full, unfiltered
    /// <paramref name="path"/>, and only the *reported* square is dropped when it sits
    /// in <paramref name="unseen"/>.
    /// </summary>
    private static List<GridPosition> ExpectedThreatened(
        Battlefield field,
        Combatant mover,
        IReadOnlyList<GridPosition> path,
        IReadOnlyCollection<Combatant> enemies,
        IReadOnlySet<GridPosition> unseen)
    {
        var expected = new List<GridPosition>();
        var from = mover.Position;

        foreach (var step in path)
        {
            if (!unseen.Contains(step)
                && MovementRules.FindOpportunityAttackers(field, mover, from, step, enemies).Count > 0)
            {
                expected.Add(step);
            }

            from = step;
        }

        return expected;
    }

    [Fact]
    public void EqualsFindOpportunityAttackersFiredForEachStepLeft()
    {
        var (field, mover, near, far, path) = Fixture();
        var visibleEnemies = new[] { near, far };

        var expected = ExpectedThreatened(field, mover, path, visibleEnemies, NoFog);
        var threatened = PlayMode.ThreatenedSteps(field, mover, path, visibleEnemies, NoFog);

        // Pin the hand-worked squares from the fixture's own doc comment, so a future
        // edit to the fixture geometry cannot silently agree with a broken
        // ThreatenedSteps by coincidence of both loops drifting the same way.
        Assert.Equal([new GridPosition(5, 0), new GridPosition(10, 0)], expected);
        Assert.Equal(expected, threatened);
    }

    [Fact]
    public void AHiddenEnemyMarksNothing()
    {
        var (field, mover, near, far, path) = Fixture();

        // Far is not passed as visible — the fog rule (#301, #732's other side): an
        // enemy the party cannot presently see contributes no threat mark at all.
        var visibleEnemies = new[] { near };

        var expected = ExpectedThreatened(field, mover, path, visibleEnemies, NoFog);
        var threatened = PlayMode.ThreatenedSteps(field, mover, path, visibleEnemies, NoFog);

        Assert.Equal([new GridPosition(5, 0)], expected);
        Assert.Equal(expected, threatened);
        Assert.DoesNotContain(new GridPosition(10, 0), threatened);

        // And restoring Far to the visible list is what puts x=10 back — proving the
        // absence above is the fog filter's doing, not a fixture that never threatened
        // there in the first place.
        var withFarVisible = PlayMode.ThreatenedSteps(field, mover, path, [near, far], NoFog);
        Assert.Contains(new GridPosition(10, 0), withFarVisible);
    }

    /// <summary>
    /// The bug #734's review round 1 found: walking the already fog-filtered squares
    /// (this method's first version) can silently drop a real provoke. A reacher at
    /// <c>(2, -1)</c> with five feet of reach threatens only <c>B = (2, 0)</c> among
    /// this zig-zag path's three squares — <c>A = (1, 1)</c> and <c>C = (3, 1)</c> are
    /// each ten feet away (Chebyshev <c>max(1, 2)</c>), out of reach — so the walk
    /// <c>A → B → C</c> enters reach at <c>B</c> (no provoke, entering never provokes)
    /// and leaves it at the step to <c>C</c> (provokes). <c>B</c> is fogged: dropping it
    /// from the *path* before ever asking the question (the old shape) leaves a direct
    /// <c>A → C</c> check that sees no change of reach at all and marks nothing;
    /// dropping only the *reported* square when it is fogged — which never happens
    /// here, since the reported square is <c>C</c>, which is visible — still reports
    /// the real provoke at <c>C</c>.
    /// </summary>
    [Fact]
    public void AFoggedSquareStillProvokesLeavingIt()
    {
        var field = new Battlefield(6, 4);
        var mover = new Combatant(
            "Mover", "Mover", FightTestData.Heroes, FightTestData.Stats(), new GridPosition(0, 2));
        var reacher = Reacher("Corner", x: 2, y: -1, reachFeet: 5);

        var a = new GridPosition(1, 1);
        var b = new GridPosition(2, 0);
        var c = new GridPosition(3, 1);
        var path = new List<GridPosition> { a, b, c };
        var unseen = new HashSet<GridPosition> { b };

        var expected = ExpectedThreatened(field, mover, path, [reacher], unseen);
        var threatened = PlayMode.ThreatenedSteps(field, mover, path, [reacher], unseen);

        // Confirms the geometry actually isolates B the way the doc comment claims,
        // before trusting either loop's answer about it.
        Assert.True(MovementRules.FindOpportunityAttackers(field, mover, a, b, [reacher]).Count == 0);
        Assert.True(MovementRules.FindOpportunityAttackers(field, mover, b, c, [reacher]).Count > 0);

        Assert.Equal([c], expected);
        Assert.Equal([c], threatened);
    }

    [Fact]
    public void EveryThreatenedSquareIsOneOfThePathsOwnSquares()
    {
        var (field, mover, near, far, path) = Fixture();

        var threatened = PlayMode.ThreatenedSteps(field, mover, path, [near, far], NoFog);

        Assert.NotEmpty(threatened);
        Assert.All(threatened, square => Assert.Contains(square, path));
    }

    [Fact]
    public void NothingWhileNoActorIsCommanded()
    {
        var (field, _, near, far, path) = Fixture();

        var threatened = PlayMode.ThreatenedSteps(field, mover: null, path, [near, far], NoFog);

        Assert.Empty(threatened);
    }
}
