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
public class StalemateTests
{
    [Fact]
    public void AStuckCreatureFinishesTheDownedEnemyPluggingTheDoorway()
    {
        // A sealed wall with one gap at (4,2), the gap occupied by a downed character:
        // the monster east of it cannot reach the hero west of it, cannot get closer,
        // and used to stand there for the rest of the fight.
        var gate = CombatTestData.Character("gate", x: 4, y: 2);
        var hero = CombatTestData.Character("hero", x: 0, y: 2);

        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: 10),
            x: 5,
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
            // Three initiative rolls; the monster's swing at the body — Advantage
            // (Unconscious, and Prone within reach) is two d20s, and the hit is a
            // Critical within 5 feet, doubling the d8 — and then the gate's own Death
            // Saving Throw, rolled as the turn passes through it.
            new ScriptedRandomSource(1, 1, 15, 15, 15, 4, 4, 10));

        DamageRules.Apply(gate, 20, DamageType.Slashing);
        Assert.True(gate.IsDying);

        SimpleTacticsPolicy.TakeTurn(encounter);

        // A Critical Hit on a creature already at 0 hit points is two failed Death
        // Saving Throws — the doorway is two such swings from clearing.
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
    public void AnApproachingCreatureReachesTheDoorwayAndThenClearsIt()
    {
        // The same pocket with the monster a stretch of corridor away. The ordinary
        // approach walks it to the wall — and to a *sheltered* corner square, which is
        // why the last resort needs its own walk: the stuck creature is standing one
        // square off the body it needs to clear, steps beside it on its next stuck
        // turn, and swings on the one after.
        var gate = CombatTestData.Character("gate", x: 4, y: 2);

        // Slowed to a crawl so it stays west of the wall: a creature may pass through
        // a downed ally's square, so a full-speed hero would walk the doorway itself
        // and hand the monster an ordinary fight instead of a stalemate.
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
            // Initiatives; a Death Saving Throw for the gate at each round's turning;
            // each swing is two d20s at Advantage and two crit d8s. The gate's own
            // saves keep succeeding — it even stabilizes once — and the swings still
            // finish it, because a Stable creature hit while down is dying again.
            new ScriptedRandomSource(
                1, 1, 15, 10, 10, 15, 15, 4, 4, 10, 15, 15, 4, 4, 10, 15, 15, 4, 4));

        DamageRules.Apply(gate, 20, DamageType.Slashing);

        // First turn: the approach ends at the wall, sheltered but out of reach.
        SimpleTacticsPolicy.TakeTurn(encounter);
        Assert.Equal(5, monster.Position.X);
        Assert.Equal(0, gate.DeathSaveFailures);

        // Play on: the stuck turns walk to the body and then swing until the doorway
        // clears — which is the whole point, a fight that can end again.
        var guard = 0;

        while (!gate.IsDead && guard++ < 20)
        {
            SimpleTacticsPolicy.TakeTurn(encounter);
        }

        Assert.True(gate.IsDead);
    }
}
