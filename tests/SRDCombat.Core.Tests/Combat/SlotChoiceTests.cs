using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Phase 7 polish: a deliberate upcast. <c>CastSpell</c>'s <c>slotLevel</c> burns the
/// slot the caller names instead of the lowest that will do, and every way the choice
/// can be wrong is refused before anything is spent.
/// </summary>
public class SlotChoiceTests
{
    [Fact]
    public void AChosenHigherSlotIsBurnedExactly()
    {
        var (encounter, caster) = Stage();

        var refusal = encounter.CastSpell("spell.mend", caster, slotLevel: 2);

        Assert.Null(refusal);
        Assert.Equal(1, caster.Features.SpellSlotsRemaining[1]);
        Assert.Equal(0, caster.Features.SpellSlotsRemaining[2]);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("(level 2 slot)", StringComparison.Ordinal));
    }

    [Fact]
    public void ASlotBelowTheSpellCannotHoldIt()
    {
        // The printed rule: "you must use a spell slot of the spell's level or higher."
        var (encounter, caster) = Stage();

        var refusal = encounter.CastSpell("spell.deep-mend", caster, slotLevel: 1);

        Assert.Equal("spell.slot_below_spell", refusal?.Code);
        Assert.Equal(1, caster.Features.SpellSlotsRemaining[1]);
        Assert.True(caster.Turn.HasAction);
    }

    [Fact]
    public void AChosenSlotAlreadyEmptyIsRefusedNotSubstituted()
    {
        // The engine must not quietly spend a different slot than the one named — a
        // substitution would be the automatic rule wearing the deliberate one's label.
        var (encounter, caster) = Stage();

        var refusal = encounter.CastSpell("spell.mend", caster, slotLevel: 3);

        Assert.Equal("spell.no_slot", refusal?.Code);
        Assert.Equal(1, caster.Features.SpellSlotsRemaining[1]);
        Assert.True(caster.Turn.HasAction);
    }

    [Fact]
    public void ACantripIsCastWithoutASlot()
    {
        var (encounter, caster) = Stage();
        var enemy = encounter.Combatants.Single(combatant => combatant.Id == "enemy");

        var refusal = encounter.CastSpell("spell.spark", enemy, slotLevel: 1);

        Assert.Equal("spell.cantrip_needs_no_slot", refusal?.Code);
        Assert.Equal(1, caster.Features.SpellSlotsRemaining[1]);
    }

    [Fact]
    public void AnUpcastSaveSpellGrowsTheFailureDamage()
    {
        // #402: this file covers a deliberate upcast's slot bookkeeping, and
        // SilentSpellTests covers a save spell's shape — but nothing cast a save
        // spell (e.g. Burning Hands) from a chosen higher slot, so ApplyScaling's
        // Grown never had a test exercising both the slot-choice path and the
        // Save.FailureDamage growth in the same cast. Grown's own doc comment records
        // that growing only Damage (and missing Save.FailureDamage) was its first bug.
        var (encounter, caster, target) = SaveSpellStage();

        var refusal = encounter.CastSpell("spell.test-scorch", target, slotLevel: 2);

        Assert.Null(refusal);
        // The level 1 slot is untouched — the chosen slot is burned exactly, not the
        // lowest that would do.
        Assert.Equal(1, caster.Features.SpellSlotsRemaining[1]);
        Assert.Equal(0, caster.Features.SpellSlotsRemaining[2]);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("(level 2 slot)", StringComparison.Ordinal));

        // Base 2d6 plus one upcast step's 1d6 (slot 2 minus the spell's own level 1)
        // is 3d6, scripted as three 4s: 12 damage on a save rolled to fail.
        Assert.Equal(target.Stats.MaximumHitPoints - 12, target.CurrentHitPoints);
    }

    /// <summary>
    /// A caster holding one slot each at levels 1 and 2, with a save spell that grows
    /// its failure damage by 1d6 per slot level above its own (level 1), and one
    /// target across the field.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Target) SaveSpellStage()
    {
        var scorch = Bare("spell.test-scorch", level: 1) with
        {
            Mechanics = EntryMechanics.SavingThrow,
            Save = new SaveEffect(
                Ability.Dexterity,
                DifficultyClass: null,
                Area: null,
                FailureDamage: [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Fire, 7)],
                SuccessOutcome: SaveSuccessOutcome.HalfDamage,
                AppliedConditions: []),
            UpcastDicePerSlotLevel = DiceExpression.Parse("1d6"),
        };

        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            InitiativeBonus = 10,
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 5,
                Spells: [scorch],
                SpellSlots: new Dictionary<int, int> { [1] = 1, [2] = 1 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 14,
                SpellAttackBonus: 6),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, stats, new GridPosition(0, 0));

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(maximumHitPoints: 40, initiativeBonus: -10),
            x: 1);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [caster, target],
            // Initiatives (20, 1), the save roll (5 — a +2 Dexterity save totals 7,
            // still short of DC 14), then three d6 damage dice for the grown 3d6.
            new ScriptedRandomSource(20, 1, 5, 4, 4, 4));

        return (encounter, caster, target);
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A caster holding one slot each at levels 1 and 2, with a level 1 heal, a level
    /// 2 heal, and a damaging cantrip; one enemy across the field.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster) Stage()
    {
        var mend = Bare("spell.mend", level: 1) with
        {
            Mechanics = EntryMechanics.Healing,
            Heal = new SpellHeal(DiceExpression.Parse("2d8"), AddsSpellcastingModifier: false),
        };

        var deepMend = Bare("spell.deep-mend", level: 2) with
        {
            Mechanics = EntryMechanics.Healing,
            Heal = new SpellHeal(DiceExpression.Parse("4d8"), AddsSpellcastingModifier: false),
        };

        var spark = Bare("spell.spark", level: 0) with
        {
            Mechanics = EntryMechanics.Attack,
            IsSpellAttack = true,
            Damage = [new AttackDamage(DiceExpression.Parse("1d10"), DamageType.Lightning, 5)],
        };

        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            InitiativeBonus = 10,
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3,
                Spells: [mend, deepMend, spark],
                SpellSlots: new Dictionary<int, int> { [1] = 1, [2] = 1 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, stats, new GridPosition(0, 2));

        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            x: 8,
            y: 2);

        var encounter = Encounter.Start(
            new Battlefield(12, 5),
            [caster, enemy],
            // Initiatives, then the successful upcast's 2d8 heal.
            new ScriptedRandomSource(15, 1, 4, 4));

        return (encounter, caster);
    }

    private static SpellDefinition Bare(string id, int level) => new()
    {
        Id = id,
        Name = id,
        Level = level,
        School = MagicSchool.Evocation,
        Classes = ["Cleric"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.Healing,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test spell.",
    };
}
