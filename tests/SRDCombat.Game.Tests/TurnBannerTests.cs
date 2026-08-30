using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The banner both clients print for whoever is acting: name, class and level for a
/// character, AC, hit points, and each attack with its damage expression.
/// </summary>
public class TurnBannerTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void ACharacterBanner_NamesTheClassAndTheNumbers()
    {
        // Resolved through the real pipeline, so this also pins that FromCharacter
        // carries the sheet's class name onto the combatant — the banner has nowhere
        // else to read "Fighter" from.
        var brenna = PregeneratedParty.Build(Content, level: 3)
            .Single(member => member.Sheet.ClassName == "Fighter");

        var lines = TurnBanner.Lines(brenna.Combatant);

        Assert.Equal(2, lines.Count);
        Assert.Equal(
            $"{brenna.Sheet.Name} — Fighter 3 — AC {brenna.Sheet.ArmorClass} — " +
            $"{brenna.Sheet.MaximumHitPoints}/{brenna.Sheet.MaximumHitPoints} hp",
            lines[0]);

        foreach (var attack in brenna.Sheet.Attacks)
        {
            Assert.Contains(attack.Name, lines[1], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMonsterBanner_CarriesNoClass()
    {
        var abilities = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => new MonsterAbility(10, 0));

        var wolf = new Combatant(
            "wolf",
            "Wolf",
            "monsters",
            new CombatantStats(
                13, 11, 40, 2, abilities, 2, CreatureSize.Medium,
                new Dictionary<DamageType, DamageResponse>(), [],
                [new CombatAttack("Bite", AttackKind.Melee, 4, 5, null, null,
                    [new AttackDamage(DiceExpression.Parse("2d4 + 2"), DamageType.Piercing, 7)])],
                DiesAtZeroHitPoints: true),
            new GridPosition(0, 0));

        var lines = TurnBanner.Lines(wolf);

        Assert.Equal("Wolf — AC 13 — 11/11 hp", lines[0]);
        Assert.Equal("Bite 2d4 + 2 Piercing", lines[1]);
    }

    [Fact]
    public void AConditionalDamageComponent_SaysWhenItApplies()
    {
        // The Goblin Warrior's "plus 2 (1d4) damage if the attack roll had Advantage"
        // is the project's founding bug; the banner must not display that die as
        // certain either.
        var abilities = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => new MonsterAbility(10, 0));

        var goblin = new Combatant(
            "goblin",
            "Goblin Warrior",
            "monsters",
            new CombatantStats(
                15, 10, 30, 2, abilities, 2, CreatureSize.Small,
                new Dictionary<DamageType, DamageResponse>(), [],
                [new CombatAttack("Scimitar", AttackKind.Melee, 4, 5, null, null,
                    [
                        new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Slashing, 5),
                        new AttackDamage(
                            DiceExpression.Parse("1d4"),
                            DamageType.Slashing,
                            2,
                            AttackDamageCondition.AttackRollHadAdvantage),
                    ])],
                DiesAtZeroHitPoints: true),
            new GridPosition(0, 0));

        Assert.Equal(
            "Scimitar 1d6 + 2 Slashing (+1d4 Slashing with Advantage)",
            TurnBanner.Lines(goblin)[1]);
    }
}
