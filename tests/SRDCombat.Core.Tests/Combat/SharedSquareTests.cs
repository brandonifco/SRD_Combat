using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// What happens when a fallen ally is healed with somebody standing in their square.
/// </summary>
/// <remarks>
/// <para>
/// This is the price of the house rule that lets a move finish on a fallen comrade. Two
/// able creatures in one square is the exact state that took down two of sixty seeded
/// runs the last time occupancy was read as "active" — the downed creature stood up
/// inside somebody else and the next path find threw on two combatants in one square.
/// </para>
/// <para>
/// The rule used to prevent that by refusing the move. It is now prevented by moving
/// somebody, which is what the 2026-08-16 play session asked for: "if the fallen comrade
/// regains consciousness, the character standing on top should just be moved to the
/// nearest viable location."
/// </para>
/// </remarks>
public class SharedSquareTests
{
    [Fact]
    public void HealingAFallenAllyDisplacesWhoeverIsStandingOnThem()
    {
        // They start apart on purpose. Two able creatures may not share a square, and
        // the sweep runs from Start, so stacking them in the constructor would simply be
        // undone before the test body began — which is itself worth knowing about. The
        // stacking has to happen the way a player does it: by walking onto the body.
        var standing = CombatTestData.Combatant("standing", x: 3, y: 4);
        var fallen = CombatTestData.Combatant(
            "fallen",
            stats: CombatTestData.Stats(diesAtZeroHitPoints: false),
            x: 3,
            y: 3);

        var foe = CombatTestData.Combatant("foe", sideId: CombatTestData.Monsters, x: 7, y: 7);

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [standing, fallen, foe],
            new ScriptedRandomSource(15, 10, 1));

        DamageRules.Apply(fallen, fallen.Stats.MaximumHitPoints, DamageType.Bludgeoning);
        Assert.True(fallen.IsDying);

        // The house rule itself: a move may finish on a fallen comrade.
        Assert.Null(encounter.Move(fallen.Position));
        Assert.Equal(fallen.Position, standing.Position);

        // Back on their feet, underneath somebody.
        DamageRules.Heal(fallen, 5);
        encounter.EndTurn();

        // The invariant that matters: no two able creatures share a square.
        var able = encounter.Combatants
            .Where(c => !c.IsDead && !c.HasCondition(ConditionType.Incapacitated))
            .ToArray();

        Assert.Equal(able.Length, able.Select(c => c.Position).Distinct().Count());

        // And the one displaced is the one who chose to stand there, not the casualty —
        // the fewest-hit-points reading, which in practice is always the reviver's
        // patient.
        Assert.Equal(new GridPosition(3, 3), fallen.Position);
        Assert.NotEqual(new GridPosition(3, 3), standing.Position);

        // Nearest viable, so they step one square rather than teleport across the field.
        Assert.Equal(5, standing.Position.DistanceFeetTo(fallen.Position));
    }

    [Fact]
    public void APathCanStillBeFoundAfterSomebodyComesRound()
    {
        // The crash itself, reproduced end to end: stand on a fallen ally, heal them,
        // then ask the pathfinder to do the thing that used to throw.
        var standing = CombatTestData.Combatant("standing", x: 4, y: 5);
        var fallen = CombatTestData.Combatant(
            "fallen",
            stats: CombatTestData.Stats(diesAtZeroHitPoints: false),
            x: 4,
            y: 4);

        var foe = CombatTestData.Combatant("foe", sideId: CombatTestData.Monsters, x: 8, y: 8);

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [standing, fallen, foe],
            new ScriptedRandomSource(15, 10, 1));

        DamageRules.Apply(fallen, fallen.Stats.MaximumHitPoints, DamageType.Bludgeoning);
        Assert.Null(encounter.Move(fallen.Position));
        DamageRules.Heal(fallen, 5);
        encounter.EndTurn();

        var route = MovementRules.FindPath(
            encounter.Battlefield,
            foe,
            new GridPosition(0, 0),
            budgetFeet: 120,
            encounter.Combatants);

        Assert.NotNull(route);
    }
}
