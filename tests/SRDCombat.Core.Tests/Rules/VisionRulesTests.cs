using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;
using SRDCombat.Core.Tests.Combat;

namespace SRDCombat.Core.Tests.Rules;

/// <summary>
/// Line of sight as a <c>Core</c> rule (#671): can a qualifying viewer see a square, or
/// another creature? Geometry plus open eyes, reusing <c>CoverRules.LineBlocked</c>.
/// </summary>
/// <remarks>
/// The geometry itself is <c>CoverRules</c>' and is pinned there; these tests pin the
/// sight readings layered on top — walls block, creatures and distance do not, a viewer
/// looks out of its whole space and always sees its own square, and the two eye-closing
/// conditions plus death take a viewer out entirely. This slice is additive, so no test
/// here asserts any combat outcome moves; that the seam moves none is the frozen
/// transcript's job.
/// </remarks>
public class VisionRulesTests
{
    // ── HasOpenEyes: the qualifying-viewer test ─────────────────────────────────

    [Fact]
    public void AHealthyViewerHasOpenEyes() =>
        Assert.True(VisionRules.HasOpenEyes(CombatTestData.Combatant("viewer")));

    [Fact]
    public void BlindedClosesTheEyes()
    {
        var viewer = CombatTestData.Combatant("viewer");
        viewer.AddCondition(ConditionType.Blinded);
        Assert.False(VisionRules.HasOpenEyes(viewer));
    }

    [Fact]
    public void UnconsciousClosesTheEyes()
    {
        var viewer = CombatTestData.Combatant("viewer");
        viewer.AddCondition(ConditionType.Unconscious);
        Assert.False(VisionRules.HasOpenEyes(viewer));
    }

    [Fact]
    public void DeathClosesTheEyes()
    {
        var viewer = CombatTestData.Combatant("viewer", sideId: CombatTestData.Monsters);
        DamageRules.Apply(viewer, 1_000, DamageType.Slashing);
        Assert.True(viewer.IsDead);
        Assert.False(VisionRules.HasOpenEyes(viewer));
    }

    // ── CanSee a square: geometry ───────────────────────────────────────────────

    [Fact]
    public void AClearLineIsSeen()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);

        Assert.True(VisionRules.CanSee(field, viewer, new GridPosition(4, 1)));
    }

    [Fact]
    public void AWallBlocksTheLine()
    {
        // A wall column at x=2 between the viewer at (0,1) and the square at (4,1).
        var field = new Battlefield(6, 6, blocked: [new(2, 0), new(2, 1), new(2, 2)]);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);

        Assert.False(VisionRules.CanSee(field, viewer, new GridPosition(4, 1)));
        // The near side of the wall is still seen.
        Assert.True(VisionRules.CanSee(field, viewer, new GridPosition(1, 1)));
    }

    [Fact]
    public void AViewerAlwaysSeesItsOwnSquare()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant("viewer", x: 3, y: 3);

        Assert.True(VisionRules.CanSee(field, viewer, viewer.Position));
    }

    // "Creatures never block sight" is structural here, not a case to exercise:
    // VisionRules' signatures accept no creature collection, so occupancy cannot enter
    // the judgement however the field is populated. Its behavioural pin — a line of
    // bodies leaving a square seen — lives one layer up, at the caller that does pass
    // combatants (SRDCombat.Game.Tests.PartyVisionTests.CreaturesAreNotWalls). A test
    // here would have to discard the bodies it built, asserting nothing the signature
    // did not already guarantee (Codex #671 P3).

    [Fact]
    public void ADisqualifiedViewerSeesNoSquare()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);
        viewer.AddCondition(ConditionType.Blinded);

        // Not even its own square, once the eyes are closed.
        Assert.False(VisionRules.CanSee(field, viewer, new GridPosition(4, 1)));
        Assert.False(VisionRules.CanSee(field, viewer, viewer.Position));
    }

    // ── The whole-space reading ─────────────────────────────────────────────────

    [Fact]
    public void AViewerLooksOutOfItsWholeSpace()
    {
        // A single wall at (2,3) sits on the horizontal line along row 3. A Medium
        // viewer anchored at (0,3) is blocked from (4,3); a Large viewer anchored at
        // (0,2) — occupying (0,2),(1,2),(0,3),(1,3) — looks out of its row-2 squares,
        // whose lines to (4,3) clear the wall. Same anchor row would block both; the
        // extra body is the whole difference, which is the reading under test.
        var field = new Battlefield(8, 8, blocked: [new(2, 3)]);

        var medium = CombatTestData.Combatant("scout", x: 0, y: 3);
        Assert.False(VisionRules.CanSee(field, medium, new GridPosition(4, 3)));

        var large = CombatTestData.Combatant(
            "ogre",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(size: CreatureSize.Large),
            x: 0,
            y: 2);
        Assert.True(VisionRules.CanSee(field, large, new GridPosition(4, 3)));
    }

    // ── CanSee a creature: any square of its space ──────────────────────────────

    [Fact]
    public void ACreatureInTheOpenIsSeen()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);

        Assert.True(VisionRules.CanSee(field, viewer, target));
    }

    [Fact]
    public void ACreatureFullyBehindAWallIsNotSeen()
    {
        var field = new Battlefield(6, 6, blocked: [new(2, 0), new(2, 1), new(2, 2)]);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);

        Assert.False(VisionRules.CanSee(field, viewer, target));
    }

    [Fact]
    public void ACreatureIsSeenWhenAnySquareOfItsSpaceIs()
    {
        // A wall hides one square of a Large target but not the other; seeing any square
        // is seeing the creature.
        var field = new Battlefield(8, 8, blocked: [new(2, 2)]);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 0);
        var large = CombatTestData.Combatant(
            "ogre",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(size: CreatureSize.Large),
            x: 3,
            y: 2);

        Assert.True(VisionRules.CanSee(field, viewer, large));
    }

    [Fact]
    public void ADisqualifiedViewerSeesNoCreature()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);
        viewer.AddCondition(ConditionType.Unconscious);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);

        Assert.False(VisionRules.CanSee(field, viewer, target));
    }

    // ── Blindsight's printed exception (#673): open eyes despite Blinded, in range ──

    [Fact]
    public void ABlindedViewerWithBlindsightInRangeStillSeesASquare()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant(
            "viewer", stats: CombatTestData.Stats() with { BlindsightFeet = 30 }, x: 0, y: 1);
        viewer.AddCondition(ConditionType.Blinded);

        // 20 ft away — within the printed 30-foot Blindsight range.
        Assert.True(VisionRules.CanSee(field, viewer, new GridPosition(4, 1)));
    }

    [Fact]
    public void ABlindedViewerWithBlindsightOutOfRangeStillSeesNothing()
    {
        var field = new Battlefield(10, 6);
        var viewer = CombatTestData.Combatant(
            "viewer", stats: CombatTestData.Stats() with { BlindsightFeet = 10 }, x: 0, y: 1);
        viewer.AddCondition(ConditionType.Blinded);

        // 20 ft away — beyond the printed 10-foot Blindsight range.
        Assert.False(VisionRules.CanSee(field, viewer, new GridPosition(4, 1)));
    }

    [Fact]
    public void BlindsightStillRespectsTotalCover()
    {
        // "You can see anything that isn't behind Total Cover" (p.177) — the exception
        // is to Blinded, not to geometry.
        var field = new Battlefield(6, 6, blocked: [new(2, 0), new(2, 1), new(2, 2)]);
        var viewer = CombatTestData.Combatant(
            "viewer", stats: CombatTestData.Stats() with { BlindsightFeet = 60 }, x: 0, y: 1);
        viewer.AddCondition(ConditionType.Blinded);

        Assert.False(VisionRules.CanSee(field, viewer, new GridPosition(4, 1)));
    }

    [Fact]
    public void TruesightGrantsNoBlindedException()
    {
        // Only Blindsight's own printed sentence mentions Blinded (p.177); Truesight's
        // (p.190) does not.
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant(
            "viewer", stats: CombatTestData.Stats() with { TruesightFeet = 60 }, x: 0, y: 1);
        viewer.AddCondition(ConditionType.Blinded);

        Assert.False(VisionRules.CanSee(field, viewer, new GridPosition(4, 1)));
    }

    // ── The Invisible-defeat clause (#673) ───────────────────────────────────────

    [Fact]
    public void AnInvisibleCreatureIsNotSeenByAnOrdinaryViewer()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);
        target.AddCondition(ConditionType.Invisible);

        Assert.False(VisionRules.CanSee(field, viewer, target));
    }

    [Fact]
    public void BlindsightInRangeSeesAnInvisibleCreature()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant(
            "viewer", stats: CombatTestData.Stats() with { BlindsightFeet = 30 }, x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);
        target.AddCondition(ConditionType.Invisible);

        Assert.True(VisionRules.CanSee(field, viewer, target));
    }

    [Fact]
    public void TruesightInRangeSeesAnInvisibleCreature()
    {
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant(
            "viewer", stats: CombatTestData.Stats() with { TruesightFeet = 30 }, x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);
        target.AddCondition(ConditionType.Invisible);

        Assert.True(VisionRules.CanSee(field, viewer, target));
    }

    [Fact]
    public void BlindsightOutOfRangeDoesNotSeeAnInvisibleCreature()
    {
        var field = new Battlefield(10, 6);
        var viewer = CombatTestData.Combatant(
            "viewer", stats: CombatTestData.Stats() with { BlindsightFeet = 10 }, x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);
        target.AddCondition(ConditionType.Invisible);

        Assert.False(VisionRules.CanSee(field, viewer, target));
    }

    [Fact]
    public void AnOrdinaryTargetIsUnaffectedByTheInvisibleClause()
    {
        // The clause only ever narrows the answer for an Invisible target — an ordinary
        // one is seen exactly as ACreatureInTheOpenIsSeen already pins, which this test
        // guards against a regression to "always defeated" or similar.
        var field = new Battlefield(6, 6);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);

        Assert.True(VisionRules.CanSee(field, viewer, target));
    }

    // ── SideCanSee: the side-neutral aggregate ───────────────────────────────────

    [Fact]
    public void SideCanSee_TrueWhenAnyoneOnTheSideSeesTheTarget()
    {
        // The blind viewer proves this is an Any, not an All: an erroneous
        // All(...) implementation would report false here, since blindFarViewer alone
        // cannot see the target.
        var field = new Battlefield(6, 6);
        var blindFarViewer = CombatTestData.Combatant("far", x: 0, y: 5);
        blindFarViewer.AddCondition(ConditionType.Blinded);
        var nearViewer = CombatTestData.Combatant("near", x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);

        Assert.False(VisionRules.CanSee(field, blindFarViewer, target));
        Assert.True(VisionRules.SideCanSee(field, CombatTestData.Heroes, target, [blindFarViewer, nearViewer, target]));
    }

    [Fact]
    public void SideCanSee_FalseWhenNoQualifyingViewerSeesTheTarget()
    {
        var field = new Battlefield(6, 6, blocked: [new(2, 0), new(2, 1), new(2, 2)]);
        var viewer = CombatTestData.Combatant("viewer", x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);

        Assert.False(VisionRules.SideCanSee(field, CombatTestData.Heroes, target, [viewer, target]));
    }

    [Fact]
    public void SideCanSee_AnInvisibleTargetDefeatsEveryOrdinaryViewerOnTheSide()
    {
        var field = new Battlefield(6, 6);
        var viewerA = CombatTestData.Combatant("a", x: 0, y: 0);
        var viewerB = CombatTestData.Combatant("b", x: 0, y: 5);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);
        target.AddCondition(ConditionType.Invisible);

        Assert.False(VisionRules.SideCanSee(field, CombatTestData.Heroes, target, [viewerA, viewerB, target]));
    }
}
