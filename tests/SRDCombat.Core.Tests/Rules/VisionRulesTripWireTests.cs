using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;
using SRDCombat.Core.Tests.Combat;

namespace SRDCombat.Core.Tests.Rules;

/// <summary>
/// T1 and T2, two of the four #672 trip-wires: the geometric invariants the sight
/// rewire of Ranged Attacks in Close Combat (C1) and Dodge (C2) rest on, asserted so
/// the first counter-example forces a decision rather than silently mis-resolving (the
/// #412 pattern). T3 (the source invariant) and T4 (the special-senses invariant) live
/// in <c>SRDCombat.Content.Tests.RealMonsterCombatTests</c> — both need a real corpus
/// monster (T3's executable Frightened riders; T4's Blindsight sense) that this
/// <c>Core</c>-only project has no way to load.
/// </summary>
/// <remarks>
/// <b>What each one actually catches, read narrowly (Codex round 1, #672).</b> No
/// single trip-wire here proves the suite-wide premise "sight is not universal" by
/// itself — each catches one specific counter-example, and the premise is only as
/// covered as the union of them plus <c>VisionRulesTests</c>' own
/// <c>AWallBlocksTheLine</c>/<c>ADisqualifiedViewerSeesNoSquare</c>. T1 alone would
/// stay green under a <c>VisionRules.CanSee</c> that always returned <c>true</c> — it
/// pins only that corner-touching walls specifically do not block adjacency, not that
/// walls block anything at all; that half is <c>VisionRulesTests.AWallBlocksTheLine</c>
/// and T2's own <c>AWallOnTheLine_IsTotalCoverAndAlsoBlocksSight</c>. Read each
/// trip-wire's own remarks for what it, specifically, would catch.
/// </remarks>
public class VisionRulesTripWireTests
{
    // ── T1: within 5 feet always has an unblocked line, however the corners sit ─────

    public static IEnumerable<object[]> AllNeighborOffsets()
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0)
                {
                    yield return [dx, dy];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllNeighborOffsets))]
    public void T1_AnyTwoSquaresWithinFiveFeet_SeeEachOtherHoweverTheCornersAreWalled(int dx, int dy)
    {
        // Ranged Attacks in Close Combat and Dodge's sight-gated halves rest on this:
        // however the eight squares around an adjacent viewer/target pair are walled,
        // the direct centre-to-centre line between them is never blocked — the same
        // "a diagonal threads between corner-touching obstacles" reading CoverRules
        // states for #429. If this ever goes red, RACC and Dodge have silently become
        // geometric under #672's rewire, and Sees(...) needs revisiting rather than
        // regenerating the frozen transcript.
        var origin = new GridPosition(2, 2);
        var target = new GridPosition(2 + dx, 2 + dy);

        var blocked = new List<GridPosition>();

        for (var x = 1; x <= 3; x++)
        {
            for (var y = 1; y <= 3; y++)
            {
                var square = new GridPosition(x, y);

                if (square != origin && square != target)
                {
                    blocked.Add(square);
                }
            }
        }

        var field = new Battlefield(5, 5, blocked: blocked);
        var viewer = CombatTestData.Combatant("viewer", x: origin.X, y: origin.Y);

        Assert.True(VisionRules.CanSee(field, viewer, target));
    }

    [Fact]
    public void T1_ALargeViewersCornerSquareThreadsADiagonalLikeAnyOther()
    {
        // T1 composes with the whole-space reading: the Large viewer's south-east
        // square, (1,1), is diagonally adjacent to the target at (2,2) exactly like
        // the Theory case above, and the same two flanking squares — (2,1) and
        // (1,2) — being walled does not block it.
        var field = new Battlefield(6, 6, blocked: [new(2, 1), new(1, 2)]);
        var large = CombatTestData.Combatant(
            "ogre",
            stats: CombatTestData.Stats(size: CreatureSize.Large),
            x: 0,
            y: 0);

        Assert.True(VisionRules.CanSee(field, large, new GridPosition(2, 2)));
    }

    // ── T2: not-Total cover implies the target sees the attacker ────────────────────

    [Fact]
    public void T2_OpenGround_TheTargetSeesTheAttacker()
    {
        var field = new Battlefield(9, 5);
        var attacker = new GridPosition(0, 2);
        var target = new GridPosition(4, 2);

        Assert.Equal(CoverDegree.None, CoverRules.Between(field, attacker, target));

        var dodger = CombatTestData.Combatant("dodger", x: target.X, y: target.Y);
        Assert.True(VisionRules.CanSee(field, dodger, attacker));
    }

    [Fact]
    public void T2_ALowObstacle_HalfCoverStillLetsTheTargetSee()
    {
        var field = new Battlefield(9, 5, lowObstacles: [new GridPosition(2, 2)]);
        var attacker = new GridPosition(0, 2);
        var target = new GridPosition(4, 2);

        Assert.Equal(CoverDegree.Half, CoverRules.Between(field, attacker, target));

        var dodger = CombatTestData.Combatant("dodger", x: target.X, y: target.Y);
        Assert.True(VisionRules.CanSee(field, dodger, attacker));
    }

    [Fact]
    public void T2_TwoLowObstacles_ThreeQuartersCoverStillLetsTheTargetSee()
    {
        var field = new Battlefield(9, 5, lowObstacles: [new GridPosition(1, 2), new GridPosition(2, 2)]);
        var attacker = new GridPosition(0, 2);
        var target = new GridPosition(4, 2);

        Assert.Equal(CoverDegree.ThreeQuarters, CoverRules.Between(field, attacker, target));

        var dodger = CombatTestData.Combatant("dodger", x: target.X, y: target.Y);
        Assert.True(VisionRules.CanSee(field, dodger, attacker));
    }

    [Fact]
    public void T2_ALivingCreatureOnTheLine_HalfCoverDoesNotBlockSight()
    {
        // Creatures grant Half Cover against an attack but never block sight —
        // VisionRules asks CoverRules.LineBlocked with no creature collection at all.
        var field = new Battlefield(9, 3);
        var attacker = new GridPosition(0, 1);
        var target = new GridPosition(4, 1);
        var bystander = CombatTestData.Combatant("bystander", x: 2, y: 1);

        Assert.Equal(CoverDegree.Half, CoverRules.Between(field, attacker, target, [bystander]));

        var dodger = CombatTestData.Combatant("dodger", x: target.X, y: target.Y);
        Assert.True(VisionRules.CanSee(field, dodger, attacker));
    }

    [Fact]
    public void T2_AWallOnTheLine_IsTotalCoverAndAlsoBlocksSight()
    {
        // T2's converse, as a sanity companion rather than part of the invariant
        // itself: when Total cover holds, sight fails too, for the same wall — the
        // hypothesis excludes this case, it does not contradict it.
        var field = new Battlefield(9, 5, blocked: [new GridPosition(2, 2)]);
        var attacker = new GridPosition(0, 2);
        var target = new GridPosition(4, 2);

        Assert.Equal(CoverDegree.Total, CoverRules.Between(field, attacker, target));

        var dodger = CombatTestData.Combatant("dodger", x: target.X, y: target.Y);
        Assert.False(VisionRules.CanSee(field, dodger, attacker));
    }
}
