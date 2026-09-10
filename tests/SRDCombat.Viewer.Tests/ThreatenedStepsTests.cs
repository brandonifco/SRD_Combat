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
    /// <summary>
    /// A melee attacker with a chosen reach, standing off the mover's row so distance
    /// varies with the mover's position along it (Chebyshev distance, five feet a
    /// square — <c>CreatureSpace.DistanceFeetTo</c>'s own remarks).
    /// </summary>
    private static Combatant Reacher(string id, int x, int reachFeet) =>
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
            new GridPosition(x, 1));

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
        var near = Reacher("Near", x: 3, reachFeet: 5);
        var far = Reacher("Far", x: 7, reachFeet: 10);
        var path = Enumerable.Range(1, 10).Select(x => new GridPosition(x, 0)).ToList();

        return (field, mover, near, far, path);
    }

    /// <summary>
    /// The ground truth every assertion below is checked against: walking
    /// <paramref name="path"/> from <paramref name="mover"/>'s own position and asking
    /// <see cref="MovementRules.FindOpportunityAttackers"/> for each step directly,
    /// independently of <see cref="PlayMode.ThreatenedSteps"/>'s own loop — so this test
    /// cannot pass merely because the two loops share a typo.
    /// </summary>
    private static List<GridPosition> ExpectedThreatened(
        Battlefield field, Combatant mover, IReadOnlyList<GridPosition> path, IReadOnlyCollection<Combatant> enemies)
    {
        var expected = new List<GridPosition>();
        var from = mover.Position;

        foreach (var step in path)
        {
            if (MovementRules.FindOpportunityAttackers(field, mover, from, step, enemies).Count > 0)
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

        var expected = ExpectedThreatened(field, mover, path, visibleEnemies);
        var threatened = PlayMode.ThreatenedSteps(field, mover, path, visibleEnemies);

        // Pin the hand-worked squares from the fixture's own doc comment, so a future
        // edit to the fixture geometry cannot silently agree with a broken
        // ThreatenedSteps by coincidence of both loops drifting the same way.
        Assert.Equal([new GridPosition(5, 0), new GridPosition(10, 0)], expected);
        Assert.Equal(expected, threatened);
    }

    [Fact]
    public void AHiddenEnemyMarksNothing()
    {
        var (field, mover, near, far, _) = Fixture();
        var path = Enumerable.Range(1, 10).Select(x => new GridPosition(x, 0)).ToList();

        // Far is not passed as visible — the fog rule (#301, #732's other side): an
        // enemy the party cannot presently see contributes no threat mark at all.
        var visibleEnemies = new[] { near };

        var expected = ExpectedThreatened(field, mover, path, visibleEnemies);
        var threatened = PlayMode.ThreatenedSteps(field, mover, path, visibleEnemies);

        Assert.Equal([new GridPosition(5, 0)], expected);
        Assert.Equal(expected, threatened);
        Assert.DoesNotContain(new GridPosition(10, 0), threatened);

        // And restoring Far to the visible list is what puts x=10 back — proving the
        // absence above is the fog filter's doing, not a fixture that never threatened
        // there in the first place.
        var withFarVisible = PlayMode.ThreatenedSteps(field, mover, path, [near, far]);
        Assert.Contains(new GridPosition(10, 0), withFarVisible);
    }

    [Fact]
    public void EveryThreatenedSquareIsOneOfThePathsOwnSquares()
    {
        var (field, mover, near, far, path) = Fixture();

        var threatened = PlayMode.ThreatenedSteps(field, mover, path, [near, far]);

        Assert.NotEmpty(threatened);
        Assert.All(threatened, square => Assert.Contains(square, path));
    }

    [Fact]
    public void NothingWhileNoActorIsCommanded()
    {
        var (field, _, near, far, path) = Fixture();

        var threatened = PlayMode.ThreatenedSteps(field, mover: null, path, [near, far]);

        Assert.Empty(threatened);
    }

    /// <summary>
    /// The other half of "nothing marked while armed or over the chrome": that gate is
    /// <see cref="PlayMode.PreviewMayShow"/>'s own (already pinned in
    /// <c>PathPreviewWiringTests</c>), and <c>UpdatePreviewPath</c> clears
    /// <c>_previewPath</c> before ever computing a route whenever it answers false — so
    /// the path <see cref="PlayMode.ThreatenedSteps"/> is asked about is empty in
    /// exactly that case. This is the fact that makes that composition safe: an empty
    /// route marks nothing, regardless of what stands on the board.
    /// </summary>
    [Fact]
    public void NothingForAnEmptyPath()
    {
        var (field, mover, near, far, _) = Fixture();

        var threatened = PlayMode.ThreatenedSteps(field, mover, path: [], [near, far]);

        Assert.Empty(threatened);
    }
}
