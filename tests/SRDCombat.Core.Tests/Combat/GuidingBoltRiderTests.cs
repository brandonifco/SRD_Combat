using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Guiding Bolt's rider: a hit marks the target so that the next attack roll made
/// against it — anyone's — has Advantage, until the end of the caster's next turn.
/// </summary>
/// <remarks>
/// Hand-authored spell rather than the extracted Guiding Bolt, the frozen transcript's
/// own reason: these fail when the engine changes, not when content regenerates. The
/// content half — that the real Guiding Bolt carries the flag, and nothing else does —
/// is a content test.
/// </remarks>
public class GuidingBoltRiderTests
{
    [Fact]
    public void AHitLightsTheTargetAndTheAlliesNextRollHasAdvantage()
    {
        // The script is exact: three initiatives, the bolt's attack roll and 1d1
        // damage, then the ally's Advantage pair and damage — a surplus or missing die
        // throws, which is the proof the ally rolled twice.
        var (encounter, _, ally, monster) = Fight(
            new ScriptedRandomSource(20, 10, 1, 15, 1, 4, 17, 1));

        Assert.Null(encounter.CastSpell("spell.test-bolt", monster));
        Assert.Equal("caster", monster.Features.GuidedBy);

        encounter.EndTurn();

        Assert.Null(encounter.Attack(ally.Stats.Attacks[0].Name, monster));

        // Spent: the light is one roll's worth, not a lasting mark.
        Assert.Null(monster.Features.GuidedBy);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("with Advantage", StringComparison.Ordinal)
                && step.Narration.Contains(ally.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void AMissLightsNothing()
    {
        // The bolt rolls a 2: 2 + 5 misses AC 12, and no rider lands.
        var (encounter, _, _, monster) = Fight(new ScriptedRandomSource(20, 10, 1, 2));

        Assert.Null(encounter.CastSpell("spell.test-bolt", monster));

        Assert.Null(monster.Features.GuidedBy);
    }

    [Fact]
    public void AnUnspentLightDiesAtTheEndOfTheCastersNextTurn()
    {
        var (encounter, _, _, monster) = Fight(new SeededSequence(20, 10, 1, 15, 1));

        Assert.Null(encounter.CastSpell("spell.test-bolt", monster));
        Assert.Equal("caster", monster.Features.GuidedBy);

        // The casting turn ends: the light survives, "your next turn" is not this one.
        encounter.EndTurn();
        Assert.Equal("caster", monster.Features.GuidedBy);

        // The ally's and the monster's turns pass, then the caster's next turn ends
        // without anyone spending it: the light dies there.
        encounter.EndTurn();
        encounter.EndTurn();
        Assert.Equal("caster", monster.Features.GuidedBy);
        encounter.EndTurn();
        Assert.Null(monster.Features.GuidedBy);
    }

    /// <summary>
    /// A caster with a bolt-shaped attack spell, an ally with a sword, and a monster.
    /// Initiative order: caster, ally, monster.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Ally, Combatant Monster) Fight(
        IRandomSource random)
    {
        var dice = DiceExpression.Parse("1d1");

        var bolt = new SpellDefinition
        {
            Id = "spell.test-bolt",
            Name = "Test Bolt",
            Level = 1,
            School = MagicSchool.Evocation,
            Classes = ["Cleric"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "120 feet",
            RangeFeet = 120,
            Components = SpellComponents.Verbal,
            DurationText = "1 round",
            Text = "Test Bolt",
            Mechanics = EntryMechanics.Attack,
            IsSpellAttack = true,
            GrantsAdvantageAgainstTargetOnHit = true,
            Damage = [new AttackDamage(dice, DamageType.Radiant, dice.Average)],
            SourcePage = 1,
        };

        var caster = CombatTestData.Combatant(
            "caster",
            stats: CombatTestData.Stats(initiativeBonus: 10, diesAtZeroHitPoints: false) with
            {
                Character = new CombatantFeatures(
                    [],
                    AttacksPerAction: 1,
                    SneakAttackDamage: null,
                    RageDamageBonus: 0,
                    RageUses: 0,
                    SecondWindUses: 0,
                    ActionSurgeUses: 0,
                    Level: 3,
                    Spells: [bolt],
                    SpellSlots: new Dictionary<int, int> { [1] = 2 },
                    SpellcastingAbility: Ability.Wisdom,
                    SpellSaveDifficultyClass: 12,
                    SpellAttackBonus: 5),
            });

        var ally = CombatTestData.Combatant(
            "ally",
            stats: CombatTestData.Stats(
                initiativeBonus: 0,
                diesAtZeroHitPoints: false,
                attacks: [CombatTestData.MeleeAttack(bonus: 5, damage: "1d1")]),
            x: 1);

        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                armorClass: 12,
                maximumHitPoints: 30,
                initiativeBonus: -10,
                attacks: [CombatTestData.MeleeAttack(bonus: 5, damage: "1d1")]),
            x: 2);

        return (
            Encounter.Start(new Battlefield(12, 12), [caster, ally, monster], random),
            caster,
            ally,
            monster);
    }
}
