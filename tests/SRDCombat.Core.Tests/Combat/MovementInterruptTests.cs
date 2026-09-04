using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// #493: the stop-on-reveal seam. <c>Encounter.Move</c>'s <c>interrupt</c> parameter is a
/// caller's chance to halt a multi-square walk mid-route; these pin the engine's half of the
/// contract — the stop, the partial spend, the narration, and that a null interrupt — what
/// every caller passes today, since #495 has not yet wired the party's clicked-move closure —
/// leaves the walk exactly as it was before the seam existed. The visibility judgement itself
/// (what counts as a "reveal") is the client's, landing in #495 — these tests supply the
/// delegate by hand.
/// </summary>
public class MovementInterruptTests
{
    [Fact]
    public void AnInterrupt_StopsTheWalkAndSpendsOnlyTheFeetActuallyWalked()
    {
        // A one-row corridor (no detour is possible), a downed enemy sitting astride the
        // route at (2,0) — passable but double-cost, since "Incapacitated" permits passing
        // through but the double-cost occupancy rule only exempts allies — and a second
        // hostile far enough down the same row that it can never provoke an Opportunity
        // Attack of its own. The interrupt fires the moment the mover enters (3,0), one
        // step past the body, so the walked prefix pays 5 + 10 + 5 = 20 ft, not the 15 ft a
        // terrain-only accumulator would have charged (the bug #493's cost fix corrects).
        var mover = CombatTestData.Combatant("mover", x: 0, y: 0);
        var downedEnemy = CombatTestData.Combatant("body", sideId: CombatTestData.Monsters, x: 2, y: 0);
        var hostile = CombatTestData.Combatant("hostile", sideId: CombatTestData.Monsters, x: 9, y: 0);

        downedEnemy.AddCondition(ConditionType.Unconscious);

        var encounter = Encounter.Start(
            new Battlefield(10, 1),
            [mover, downedEnemy, hostile],
            new ScriptedRandomSource(20, 1, 1));

        var revealAt = new GridPosition(3, 0);
        MovementInterrupt interrupt = step => step.At == revealAt ? hostile : null;

        var refusal = encounter.Move(new GridPosition(5, 0), interrupt);

        Assert.Null(refusal);
        Assert.Equal(revealAt, mover.Position);

        // 30 ft. Speed - (5 clear + 10 double [the body's square] + 5 clear) = 10 left.
        Assert.Equal(10, mover.Turn.MovementFeet);

        var moveStep = encounter.Log.Last(step => step.Kind == CombatStepKind.Move);
        Assert.Equal(
            [new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(2, 0), revealAt],
            moveStep.Path);
        Assert.Equal("mover stops at (3,0): hostile comes into view.", moveStep.Narration);

        // The unspent movement stays in hand: a second Move from the stopping square
        // reaches the original destination on exactly the budget left.
        var continued = encounter.Move(new GridPosition(5, 0), interrupt);

        Assert.Null(continued);
        Assert.Equal(new GridPosition(5, 0), mover.Position);
        Assert.Equal(0, mover.Turn.MovementFeet);

        var secondMoveStep = encounter.Log.Last(step => step.Kind == CombatStepKind.Move);
        Assert.Equal("mover moves from (3,0) to (5,0) (10 ft.).", secondMoveStep.Narration);
    }

    [Fact]
    public void ANullInterrupt_WalksTheWholePathExactlyAsBeforeTheSeamExisted()
    {
        var mover = CombatTestData.Combatant("mover", x: 0, y: 0);
        var encounter = Encounter.Start(new Battlefield(10, 1), [mover], new ScriptedRandomSource(20));

        // interrupt omitted entirely — the monster AI, the console and a keyboard step all
        // call Move this way.
        var refusal = encounter.Move(new GridPosition(5, 0));

        Assert.Null(refusal);
        Assert.Equal(new GridPosition(5, 0), mover.Position);
        Assert.Equal(5, mover.Turn.MovementFeet);

        var moveStep = encounter.Log.Last(step => step.Kind == CombatStepKind.Move);
        Assert.Equal("mover moves from (0,0) to (5,0) (25 ft.).", moveStep.Narration);
    }

    [Fact]
    public void AnInterruptThatWouldFireOnArrival_NeverStopsTheFinalStep()
    {
        // An arrived move has nothing left to interrupt: the loop only consults the
        // delegate for the Count-1 non-final steps. Proven two ways — the delegate's call
        // count, and that the walk completes with the ordinary "moves from...to..."
        // narration rather than the stop narration it would carry had it fired.
        var mover = CombatTestData.Combatant("mover", x: 0, y: 0);
        var hostile = CombatTestData.Combatant("hostile", sideId: CombatTestData.Monsters, x: 9, y: 0);
        var encounter = Encounter.Start(
            new Battlefield(10, 1),
            [mover, hostile],
            new ScriptedRandomSource(20, 1));

        var destination = new GridPosition(3, 0);
        var calls = 0;

        MovementInterrupt interrupt = step =>
        {
            calls++;
            return step.At == destination ? hostile : null;
        };

        var refusal = encounter.Move(destination, interrupt);

        Assert.Null(refusal);
        Assert.Equal(2, calls); // a 3-step path is consulted after steps 1 and 2, never step 3.
        Assert.Equal(destination, mover.Position);
        Assert.Equal(15, mover.Turn.MovementFeet); // the full 15 ft. route was spent, not a partial one.

        var moveStep = encounter.Log.Last(step => step.Kind == CombatStepKind.Move);
        Assert.Equal("mover moves from (0,0) to (3,0) (15 ft.).", moveStep.Narration);
    }

    /// <summary>
    /// qc round 1 on PR #622: the delegate is caller-supplied advisory code (a fog query),
    /// so a fault in it must not corrupt the walk. Fails open — the planned move completes
    /// exactly as a null interrupt would — rather than leaving the mover half-relocated with
    /// nothing spent, logged or cleaned up.
    /// </summary>
    [Fact]
    public void AThrowingInterrupt_CompletesTheWalkAndIsNotConsultedAgain()
    {
        var mover = CombatTestData.Combatant("mover", x: 0, y: 0);
        var encounter = Encounter.Start(new Battlefield(10, 1), [mover], new ScriptedRandomSource(20));

        var faultAt = new GridPosition(2, 0);
        var calls = 0;

        MovementInterrupt interrupt = step =>
        {
            calls++;
            return step.At == faultAt ? throw new InvalidOperationException("a broken visibility query") : null;
        };

        var refusal = encounter.Move(new GridPosition(5, 0), interrupt);

        Assert.Null(refusal);
        Assert.Equal(new GridPosition(5, 0), mover.Position);
        Assert.Equal(5, mover.Turn.MovementFeet); // the full 25 ft. route was spent, exactly like a null interrupt.
        Assert.Equal(2, calls); // consulted at (1,0) [no fault], then at (2,0) [faults] -- never again after that.

        var moveStep = encounter.Log.Last(step => step.Kind == CombatStepKind.Move);
        Assert.Equal("mover moves from (0,0) to (5,0) (25 ft.).", moveStep.Narration);
    }
}
