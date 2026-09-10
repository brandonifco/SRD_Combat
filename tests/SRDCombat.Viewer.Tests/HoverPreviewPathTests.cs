using SRDCombat.Core.Combat;
using SRDCombat.Core.Rules;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="PlayMode.HoverPreviewPath"/> (#303) — the plain-value seam behind the
/// board's path preview. <c>PlayMode</c> itself needs a live Godot node (the pointer,
/// <c>QueueRedraw</c>, the draw call), but the decision this issue is actually about —
/// which squares light up for a given hover — touches none of that: it is
/// <see cref="MovementRules.FindPath"/> called with the same arguments
/// <see cref="Encounter.Move"/> passes it, filtered by fog, so it is pinned here exactly
/// the way <see cref="PlayMode.HoverDelayElapsed"/> already is.
/// </summary>
public class HoverPreviewPathTests
{
    private static readonly IReadOnlySet<GridPosition> NoFog = new HashSet<GridPosition>();

    [Fact]
    public void EqualsTheEnginesOwnPathForTheHoveredSquare()
    {
        var mover = FightTestData.Combatant("Mover", x: 0);
        var encounter = FightTestData.Fight(mover);
        var hovered = new GridPosition(2, 0);

        var expected = MovementRules.FindPath(
            encounter.Battlefield, mover, hovered, mover.Turn.MovementFeet, encounter.Combatants);

        var reachable = MovementRules.Reachable(
            encounter.Battlefield, mover, mover.Turn.MovementFeet, encounter.Combatants);

        var preview = PlayMode.HoverPreviewPath(
            encounter.Battlefield, mover, hovered, reachable, encounter.Combatants, NoFog);

        Assert.NotNull(expected);
        Assert.NotEmpty(preview);
        Assert.Equal(expected!.Steps, preview);
    }

    [Fact]
    public void NothingForASquareOutsideTheReachableSet()
    {
        var mover = FightTestData.Combatant("Mover", x: 0);
        var encounter = FightTestData.Fight(mover);

        // (2, 0) is close enough that FindPath would answer it on its own (the previous
        // test proves exactly that) — the empty reachable set passed here is the only
        // reason this comes back empty, which is the fact this test pins: a square
        // FindPath might still find a route to is not drawn unless the reachable set
        // (the same one the blue wash already lit) offers it too.
        var hovered = new GridPosition(2, 0);

        var preview = PlayMode.HoverPreviewPath(
            encounter.Battlefield, mover, hovered, reachable: [], encounter.Combatants, NoFog);

        Assert.Empty(preview);
    }

    [Fact]
    public void NothingWhileNoActorIsCommanded()
    {
        var mover = FightTestData.Combatant("Mover", x: 0);
        var encounter = FightTestData.Fight(mover);
        var hovered = new GridPosition(2, 0);

        var reachable = MovementRules.Reachable(
            encounter.Battlefield, mover, mover.Turn.MovementFeet, encounter.Combatants);

        var preview = PlayMode.HoverPreviewPath(
            encounter.Battlefield, mover: null, hovered, reachable, encounter.Combatants, NoFog);

        Assert.Empty(preview);
    }

    [Fact]
    public void FogHoldsASquareTheRouteWouldCrossIsDroppedFromWhatIsDrawn()
    {
        var mover = FightTestData.Combatant("Mover", x: 0);
        var encounter = FightTestData.Fight(mover);
        var hovered = new GridPosition(2, 0);

        var reachable = MovementRules.Reachable(
            encounter.Battlefield, mover, mover.Turn.MovementFeet, encounter.Combatants);

        var fullPath = MovementRules.FindPath(
            encounter.Battlefield, mover, hovered, mover.Turn.MovementFeet, encounter.Combatants);

        Assert.NotNull(fullPath);
        Assert.True(fullPath!.Steps.Count >= 2, "need at least one step besides the destination to hide");

        var hiddenStep = fullPath.Steps[0];
        var fog = new HashSet<GridPosition> { hiddenStep };

        var preview = PlayMode.HoverPreviewPath(
            encounter.Battlefield, mover, hovered, reachable, encounter.Combatants, fog);

        Assert.DoesNotContain(hiddenStep, preview);
        Assert.Equal(fullPath.Steps.Where(step => step != hiddenStep), preview);
    }
}
