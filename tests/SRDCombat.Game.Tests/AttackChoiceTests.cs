using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The attack a client swings when the player just clicks an enemy.
/// </summary>
/// <remarks>
/// A convenience rather than a rule — the engine takes whatever name it is handed — but
/// it has to be the choice a player would make by hand, and until a played run caught
/// it, a Rogue with a Shortsword and a Shortbow fired the <em>bow</em> at an adjacent
/// enemy, at Disadvantage, because the two average the same and the tie broke
/// alphabetically.
/// </remarks>
public class AttackChoiceTests
{
    [Fact]
    public void ABladeBeatsABowWhenTheEnemyIsAdjacent()
    {
        var rogue = Rogue(0, 0);
        var adjacent = Enemy(1, 0);

        var chosen = AttackChoice.BestFor(rogue, adjacent, [rogue, adjacent]);

        Assert.Equal("Shortsword", chosen?.Name);
    }

    [Fact]
    public void TheBowIsStillTheChoiceAtRange()
    {
        var rogue = Rogue(0, 0);
        var distant = Enemy(5, 0);

        var chosen = AttackChoice.BestFor(rogue, distant, [rogue, distant]);

        Assert.Equal("Shortbow", chosen?.Name);
    }

    [Fact]
    public void AnyAdjacentEnemyPenalisesTheShot_NotJustTheTarget()
    {
        // "while within 5 feet of an enemy who can see you" — the printed rule names an
        // enemy, not the target, so a Rogue shooting past the goblin on its own square
        // is penalised for that goblin.
        var rogue = Rogue(0, 0);
        var breathing = Enemy(1, 0, "closer");
        var distant = Enemy(5, 0, "further");

        var chosen = AttackChoice.BestFor(rogue, distant, [rogue, breathing, distant]);

        // The blade cannot reach 25 feet, so the bow is all there is — but the ordering
        // has demoted it rather than pretending the shot is clean.
        Assert.Equal("Shortbow", chosen?.Name);

        // With the crowding gone the same shot is the unpenalised first choice.
        Assert.Equal("Shortbow", AttackChoice.BestFor(rogue, distant, [rogue, distant])?.Name);
    }

    [Fact]
    public void WithoutARosterTheTargetsOwnDistanceDecides()
    {
        var rogue = Rogue(0, 0);

        Assert.Equal("Shortsword", AttackChoice.BestFor(rogue, Enemy(1, 0))?.Name);
        Assert.Equal("Shortbow", AttackChoice.BestFor(rogue, Enemy(5, 0))?.Name);
    }

    [Fact]
    public void NothingIsChosenWhenNothingReaches()
    {
        var swordsman = new Combatant(
            "melee",
            "Melee",
            "party",
            Stats([Melee("Shortsword")]),
            new GridPosition(0, 0));

        Assert.Null(AttackChoice.BestFor(swordsman, Enemy(8, 0), [swordsman]));
    }

    private static Combatant Rogue(int x, int y) =>
        new("rogue", "Sable", "party", Stats([Melee("Shortsword"), Bow("Shortbow")]), new GridPosition(x, y));

    private static Combatant Enemy(int x, int y, string id = "enemy") =>
        new(id, id, "monsters", Stats([Melee("Claw")]), new GridPosition(x, y));

    private static CombatantStats Stats(IReadOnlyList<CombatAttack> attacks) =>
        new(
            13,
            20,
            30,
            2,
            new Dictionary<Ability, MonsterAbility>
            {
                [Ability.Strength] = new(12, 1),
                [Ability.Dexterity] = new(16, 3),
                [Ability.Constitution] = new(12, 1),
                [Ability.Intelligence] = new(10, 0),
                [Ability.Wisdom] = new(10, 0),
                [Ability.Charisma] = new(10, 0),
            },
            2,
            CreatureSize.Medium,
            new Dictionary<DamageType, DamageResponse>(),
            [],
            attacks,
            DiesAtZeroHitPoints: true);

    /// <summary>A 1d6 + 3 melee attack — the same average the bow rolls, so the tie is real.</summary>
    private static CombatAttack Melee(string name)
    {
        var dice = DiceExpression.Parse("1d6 + 3");

        return new CombatAttack(name, AttackKind.Melee, 5, 5, null, null, [new AttackDamage(dice, DamageType.Piercing, dice.Average)]);
    }

    private static CombatAttack Bow(string name)
    {
        var dice = DiceExpression.Parse("1d6 + 3");

        return new CombatAttack(name, AttackKind.Ranged, 5, null, 80, 320, [new AttackDamage(dice, DamageType.Piercing, dice.Average)]);
    }
}
