using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The stuck turn's last resort: a downed enemy is attacked only when there is nothing
/// else to do. Found as a fight that could not end — generated walls formed a pocket
/// whose one doorway was plugged by an unconscious character, a Giant Vulture stood
/// beside the body for fifty rounds without swinging, and both sides idled to the
/// round limit.
/// </summary>
/// <remarks>
/// <para>
/// <b>The doorway half of that diagnosis was a movement bug, and it is fixed at the
/// rule now.</b> The printed <em>Moving around Other Creatures</em> clause lets anyone
/// pass through the space of "a creature that has the Incapacitated condition",
/// naming a condition rather than a side, and <see cref="MovementRules"/> only ever
/// exempted allies — so a body really did wall a corridor off, and the last resort was
/// carrying a workaround for a gap that belonged in the pathfinder.
/// <see cref="ABodyInADoorwayNoLongerStalematesTheFight"/> pins the new behaviour.
/// </para>
/// <para>
/// The last resort is still needed and still tested: walls alone can seal a pocket,
/// an able enemy can hold a corridor, and the size clauses that would let a creature
/// squeeze past are not modelled. What changed is that a fallen body is no longer one
/// of the ways a fight can wedge itself shut, so the scenario below seals its pocket
/// with stone instead.
/// </para>
/// </remarks>
public class StalemateTests
{
    [Fact]
    public void AStuckCreatureFinishesADownedEnemyWhenWallsLeaveNothingElse()
    {
        // A dead-end cell at (1,1) walled on every side but one, and that one square
        // holds a downed character. The monster can move nowhere — passing through the
        // body is legal now, but there is only stone on the far side of it, and ending
        // on it never was — and the hero is across the map. Nothing to do but swing.
        //
        // Sealing the pocket with stone alone was not enough to wedge a turn: with a
        // whole side of the field still open the monster simply repositioned, which is
        // the policy working. Stuck has to mean stuck.
        var gate = CombatTestData.Character("gate", x: 2, y: 1);
        var hero = CombatTestData.Character("hero", x: 8, y: 2);

        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: 10),
            x: 1,
            y: 1);

        var encounter = Encounter.Start(
            new Battlefield(10, 5, blocked:
            [
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(2, 0),
                new GridPosition(0, 1),
                new GridPosition(0, 2),
                new GridPosition(1, 2),
                new GridPosition(2, 2),
                // The far side of the body, diagonals included — a diagonal step is a
                // step, so leaving one of these open is an escape route.
                new GridPosition(3, 0),
                new GridPosition(3, 1),
                new GridPosition(3, 2),
            ]),
            [gate, hero, monster],
            // Three initiative rolls; the monster's swing at the body — Advantage
            // (Unconscious, and Prone within reach) is two d20s, and the hit is a
            // Critical within 5 feet, doubling the d8 — and then the gate's own Death
            // Saving Throw, rolled as the turn passes through it.
            new ScriptedRandomSource(1, 1, 15, 15, 15, 4, 4, 10));

        DamageRules.Apply(gate, 20, DamageType.Slashing);
        Assert.True(gate.IsDying);

        SimpleTacticsPolicy.TakeTurn(encounter);

        // A Critical Hit on a creature already at 0 hit points is two failed Death
        // Saving Throws, and the monster never left its square because it could not.
        Assert.Equal(new GridPosition(1, 1), monster.Position);
        Assert.Equal(2, gate.DeathSaveFailures);
    }

    [Fact]
    public void ACreatureMidApproachNeverDivertsToTheFallen()
    {
        // Open field, a live enemy ahead and a downed one at the monster's feet: the
        // turn moves toward the fight, so the gate never opens and the fallen is left
        // alone. This is the boundary that keeps the last resort from changing every
        // ordinary fight into an execution.
        var fallen = CombatTestData.Character("fallen", x: 13, y: 2);
        var hero = CombatTestData.Character("hero", x: 0, y: 2);

        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: 10),
            x: 12,
            y: 2);

        var encounter = Encounter.Start(
            new Battlefield(20, 5),
            [fallen, hero, monster],
            // Initiatives, then the fallen's Death Saving Throw as the turn passes.
            new ScriptedRandomSource(1, 1, 15, 10));

        DamageRules.Apply(fallen, 20, DamageType.Slashing);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.True(monster.Position.X < 12);
        Assert.Equal(0, fallen.DeathSaveFailures);
    }

    [Fact]
    public void ABodyInADoorwayNoLongerStalematesTheFight()
    {
        // The original stalemate, exactly as it was found: a wall with one gap at
        // (4,2) and an unconscious character lying in the gap. It used to wall the
        // monster out, because only allies could be walked through.
        //
        // The printed clause covers "a creature that has the Incapacitated condition"
        // whatever side it is on, so the body is now a doorway with a cost rather than
        // a door. The monster walks it and the fight goes on being a fight.
        var gate = CombatTestData.Character("gate", x: 4, y: 2);

        // Slowed to a crawl so it stays west of the wall and the monster has to come
        // through the gap to find it.
        var hero = CombatTestData.Combatant(
            "hero",
            stats: CombatTestData.Stats(speedFeet: 5, diesAtZeroHitPoints: false),
            x: 0,
            y: 2);

        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: 10),
            x: 8,
            y: 2);

        var encounter = Encounter.Start(
            new Battlefield(10, 5, blocked:
            [
                new GridPosition(4, 0),
                new GridPosition(4, 1),
                new GridPosition(4, 3),
                new GridPosition(4, 4),
            ]),
            [gate, hero, monster],
            new ScriptedRandomSource(1, 1, 15));

        DamageRules.Apply(gate, 20, DamageType.Slashing);
        Assert.True(gate.IsDying);

        // The route, not the policy's taste in squares: which square this monster
        // prefers to stand on is a cover judgement that predates this rule and is
        // tested elsewhere. What the doorway bug broke was whether a route existed at
        // all, and that is what is asserted here.
        var route = MovementRules.FindPath(
            encounter.Battlefield,
            monster,
            new GridPosition(3, 2),
            budgetFeet: 60,
            encounter.Combatants);

        Assert.NotNull(route);

        // It really does go through the body's square rather than round it — there is
        // no way round, which is what made this a stalemate.
        Assert.Contains(new GridPosition(4, 2), route.Steps);

        // And the body's square is Difficult Terrain, because the printed exemption is
        // for allies and a downed enemy is not one: four clear squares at five feet
        // and the doorway at ten.
        Assert.Equal(30, route.CostFeet);

        // Ending on the body stays refused, which is what keeps a healed creature from
        // waking up underneath somebody.
        Assert.Null(MovementRules.FindPath(
            encounter.Battlefield,
            monster,
            new GridPosition(4, 2),
            budgetFeet: 60,
            encounter.Combatants));
    }
}
