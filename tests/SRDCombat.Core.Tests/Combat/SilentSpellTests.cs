using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// A spell that forces a saving throw and has nothing to do on a failure is refused,
/// rather than spending a slot to print a failed save and change nothing.
/// </summary>
/// <remarks>
/// This is bug 1's shape at spell scale — the structured half (the save) hid the missing
/// half (the effect) — and it was live for 66 of the book's 339 spells.
/// </remarks>
public class SilentSpellTests
{
    [Fact]
    public void ASaveSpellWithNothingBehindTheSaveIsRefused()
    {
        var (encounter, caster, target) = Fight(SaveSpell("spell.bane", "Bane", damage: null));

        var refusal = encounter.CastSpell("spell.bane", target);

        Assert.Equal("spell.save_effect_not_modelled", refusal?.Code);

        // The slot is the point: a refusal must cost nothing.
        Assert.Equal(2, caster.Features.SpellSlotsRemaining[1]);
    }

    [Fact]
    public void ASaveSpellThatDealsDamageStillCasts()
    {
        var (encounter, caster, target) = Fight(SaveSpell("spell.test-bolt", "Test Bolt", damage: "2d6"));

        Assert.Null(encounter.CastSpell("spell.test-bolt", target));
        Assert.Equal(1, caster.Features.SpellSlotsRemaining[1]);
    }

    [Fact]
    public void ASaveSpellWhoseConditionTheEngineExecutesStillCasts()
    {
        // Prone is on the executable allowlist and carries no unmodelled requirement
        // here, so this one has something to do on a failure.
        var spell = SaveSpell("spell.test-topple", "Test Topple", damage: null) with
        {
            AppliedConditions = [new AppliedCondition(ConditionType.Prone)],
        };

        var (encounter, _, target) = Fight(spell);

        Assert.Null(encounter.CastSpell("spell.test-topple", target));
    }

    /// <summary>A save spell with an optional damage expression, otherwise minimal.</summary>
    private static SpellDefinition SaveSpell(string id, string name, string? damage) => new()
    {
        Id = id,
        Name = name,
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Cleric"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.SavingThrow,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test spell.",
        Save = new SaveEffect(
            Ability.Constitution,
            DifficultyClass: null,
            Area: null,
            FailureDamage: damage is null
                ? []
                : [new AttackDamage(DiceExpression.Parse(damage), DamageType.Radiant, 7)],
            SuccessOutcome: SaveSuccessOutcome.HalfDamage,
            AppliedConditions: []),
    };

    private static (Encounter Encounter, Combatant Caster, Combatant Target) Fight(SpellDefinition spell)
    {
        var caster = CombatTestData.Character("caster");

        var stats = caster.Stats with
        {
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 5,
                Spells: [spell],
                SpellSlots: new Dictionary<int, int> { [1] = 2 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 14,
                SpellAttackBonus: 6),
        };

        var withSpells = new Combatant("caster", "Caster", CombatTestData.Heroes, stats, new GridPosition(0, 0));

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            x: 1);

        return (
            Encounter.Start(new Battlefield(12, 12), [withSpells, target], new ScriptedRandomSource(20, 1, 5, 3, 3)),
            withSpells,
            target);
    }
}
