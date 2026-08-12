using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Tests.Combat;

namespace SRDCombat.Core.Tests.Characters;

/// <summary>
/// Healing spells: the third effect shape, after an attack roll and a saving throw.
/// </summary>
/// <remarks>
/// <para>
/// The printed rule these pin: "A creature you touch regains a number of Hit Points equal
/// to 2d8 plus your spellcasting ability modifier."
/// </para>
/// <para>
/// The case that matters most for a run is the one at the bottom — <b>healing a character
/// at 0 hit points brings them back into the fight</b>. Without it a dropped character
/// was gone for good, and a gauntlet died out within a few fights however easy they were.
/// </para>
/// </remarks>
public class HealingTests
{
    [Fact]
    public void HealingRestoresTheRolledDicePlusTheCastingModifier()
    {
        // 2d8 rolling 3 and 4, plus a +3 Wisdom modifier, is 10 hit points.
        var (encounter, caster, wounded) = Fight(new ScriptedRandomSource(20, 1, 3, 4));

        DamageTo(wounded, 12);
        var before = wounded.CurrentHitPoints;

        Assert.Null(encounter.CastSpell("spell.cure", wounded));

        Assert.Equal(before + 10, wounded.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("regains 10 hit points", StringComparison.Ordinal));
        _ = caster;
    }

    [Fact]
    public void HealingNeverExceedsTheMaximum()
    {
        var (encounter, _, wounded) = Fight(new ScriptedRandomSource(20, 1, 8, 8));

        DamageTo(wounded, 2);

        Assert.Null(encounter.CastSpell("spell.cure", wounded));

        Assert.Equal(wounded.Stats.MaximumHitPoints, wounded.CurrentHitPoints);
    }

    [Fact]
    public void HealingACharacterAtZeroBringsThemBackIntoTheFight()
    {
        // The whole reason this was worth building. A downed character is Unconscious and
        // rolling Death Saving Throws; healing clears all of it.
        var (encounter, _, wounded) = Fight(new ScriptedRandomSource(20, 1, 3, 4));

        DamageTo(wounded, wounded.Stats.MaximumHitPoints);

        Assert.Equal(0, wounded.CurrentHitPoints);
        Assert.True(wounded.HasCondition(ConditionType.Unconscious));
        Assert.False(wounded.CanAct);

        Assert.Null(encounter.CastSpell("spell.cure", wounded));

        Assert.True(wounded.CurrentHitPoints > 0);
        Assert.False(wounded.HasCondition(ConditionType.Unconscious));
        Assert.True(wounded.CanAct);
        Assert.Equal(0, wounded.DeathSaveFailures);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("back on their feet", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDeadCannotBeHealed()
    {
        // Healing spells restore hit points and say nothing about a creature that has
        // died; raising the dead is its own spell and its own work.
        var (encounter, _, target) = Fight(new ScriptedRandomSource(20, 1), diesAtZero: true);

        DamageTo(target, target.Stats.MaximumHitPoints);
        Assert.True(target.IsDead);

        Assert.Null(encounter.CastSpell("spell.cure", target));

        Assert.Equal(0, target.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("who is dead", StringComparison.Ordinal));
    }

    [Fact]
    public void AHealingSpellSpendsItsSlot()
    {
        var (encounter, caster, wounded) = Fight(new ScriptedRandomSource(20, 1, 3, 4));

        DamageTo(wounded, 12);
        var before = caster.Features.SpellSlotsRemaining[1];

        Assert.Null(encounter.CastSpell("spell.cure", wounded));

        Assert.Equal(before - 1, caster.Features.SpellSlotsRemaining[1]);
    }

    [Fact]
    public void AHealingSpellNeedsSomebodyToHeal()
    {
        var (encounter, _, _) = Fight(new ScriptedRandomSource(20, 1));

        Assert.Equal("spell.needs_target", encounter.CastSpell("spell.cure", new GridPosition(3, 3))?.Code);
    }

    /// <summary>A caster who knows one healing spell, and an ally to heal.</summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Ally) Fight(
        IRandomSource random,
        bool diesAtZero = false)
    {
        var cure = new SpellDefinition
        {
            Id = "spell.cure",
            Name = "Cure Wounds",
            Level = 1,
            School = MagicSchool.Abjuration,
            Classes = ["Cleric"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "Touch",
            RangeFeet = null,
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Text = "A creature you touch regains a number of Hit Points equal to 2d8 plus your spellcasting ability modifier.",
            Mechanics = EntryMechanics.Healing,
            Heal = new SpellHeal(DiceExpression.Parse("2d8"), AddsSpellcastingModifier: true),
            SourcePage = 1,
        };

        var casterStats = CombatTestData.Stats(initiativeBonus: 10, diesAtZeroHitPoints: false) with
        {
            // Wisdom 16 for a +3 modifier, so the test can tell whether the printed
            // "plus your spellcasting ability modifier" is actually being added — the
            // shared fixture's Wisdom 10 would have hidden it behind a +0.
            Abilities = new Dictionary<Ability, MonsterAbility>
            {
                [Ability.Strength] = new(14, 2),
                [Ability.Dexterity] = new(14, 2),
                [Ability.Constitution] = new(14, 2),
                [Ability.Intelligence] = new(10, 0),
                [Ability.Wisdom] = new(16, 3),
                [Ability.Charisma] = new(10, 0),
            },
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 5,
                Spells: [cure],
                SpellSlots: new Dictionary<int, int> { [1] = 3 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = CombatTestData.Combatant("caster", sideId: CombatTestData.Heroes, stats: casterStats);

        var ally = CombatTestData.Combatant(
            "ally",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(
                maximumHitPoints: 30,
                initiativeBonus: -10,
                diesAtZeroHitPoints: diesAtZero),
            x: 1);

        var encounter = Encounter.Start(new Battlefield(8, 8), [caster, ally], random);

        return (encounter, caster, ally);
    }

    /// <summary>Takes a combatant down by a given amount, through the engine's own path.</summary>
    private static void DamageTo(Combatant combatant, int amount) =>
        Core.Rules.DamageRules.Apply(combatant, amount, DamageType.Bludgeoning, fromCriticalHit: false);
}
