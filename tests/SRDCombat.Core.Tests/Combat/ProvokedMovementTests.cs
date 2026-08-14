using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Squad AI slice 1 (#122): the policy's walks know what an Opportunity Attack costs.
/// The watched bug: a caster stepping out of two enemies' reach to clear an ally's Half
/// Cover, eating both swings for a +2.
/// </summary>
public class ProvokedMovementTests
{
    [Fact]
    public void ASidestepThatProvokes_IsNotTaken()
    {
        // A one-row corridor: melee enemy, archer, ally, then the target. The only
        // squares that clear the ally's Half Cover lie past the ally, and walking to
        // any of them leaves the enemy's reach. The shot from here is legal at +2, so
        // the policy keeps its feet and shoots through the ally rather than paying a
        // swing for the sidestep.
        var (encounter, archer, enemy) = Corridor(withEnemy: true);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(new GridPosition(1, 0), archer.Position);
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
        Assert.True(enemy!.Turn.HasReaction);

        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.Contains("(Half Cover)", swing.Narration);
    }

    [Fact]
    public void TheSameSidestepIsTaken_WhenItIsFree()
    {
        // The identical corridor without the enemy: now the walk past the ally costs
        // nothing, and the policy takes the clean shot it refused to bleed for above.
        var (encounter, archer, _) = Corridor(withEnemy: false);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(new GridPosition(3, 0), archer.Position);

        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.DoesNotContain("Cover", swing.Narration);
        Assert.Contains("hit", swing.Narration);
    }

    /// <summary>
    /// M(0,0) A(1,0) L(2,0) . . T(5,0) on a 7×1 strip; the enemy is omitted when
    /// <paramref name="withEnemy"/> is false. Dice: initiative in list order, then the
    /// archer's shot — two d20s beside the enemy (close combat), one without — and a
    /// damage die on the hit.
    /// </summary>
    private static (Encounter Encounter, Combatant Archer, Combatant? Enemy) Corridor(bool withEnemy)
    {
        var archer = CombatTestData.Combatant(
            "archer",
            stats: CombatTestData.Stats(
                initiativeBonus: 10,
                attacks: [CombatTestData.RangedAttack(bonus: 4)],
                // Dumb on purpose: at the default INT 10 the monster doctrine (#127)
                // would converge this hand-authored shooter onto the melee enemy, and
                // this scenario is about what a walk costs, not who to shoot.
                intelligence: 3),
            x: 1,
            y: 0);

        var ally = CombatTestData.Combatant(
            "ally",
            stats: CombatTestData.Stats(initiativeBonus: -5, attacks: []),
            x: 2,
            y: 0);

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(maximumHitPoints: 5, initiativeBonus: -10, attacks: []),
            x: 5,
            y: 0);

        var enemy = withEnemy
            ? CombatTestData.Combatant(
                "enemy",
                sideId: CombatTestData.Monsters,
                stats: CombatTestData.Stats(
                    maximumHitPoints: 40,
                    initiativeBonus: -8,
                    attacks: [CombatTestData.MeleeAttack(bonus: 4)]),
                x: 0,
                y: 0)
            : null;

        var combatants = enemy is null
            ? new[] { archer, ally, target }
            : [archer, ally, target, enemy];

        var dice = withEnemy
            ? new ScriptedRandomSource(20, 1, 1, 1, 3, 4)
            : new ScriptedRandomSource(20, 1, 1, 14, 4);

        return (Encounter.Start(new Battlefield(7, 1), combatants, dice), archer, enemy);
    }
}
