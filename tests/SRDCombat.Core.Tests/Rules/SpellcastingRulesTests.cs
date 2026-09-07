using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Rules;

/// <summary>
/// <see cref="SpellcastingRules.AverageDamage"/> — the single-count damage estimate
/// AI pricing rests on (#376). A save spell's dice are extracted onto both
/// <see cref="SpellDefinition.Damage"/> and <c>Save.FailureDamage</c> (the same dice,
/// carried twice, per the extraction contract read in <see cref="SpellDefinition.Damage"/>'s
/// own doc comment), because <c>Encounter.Casting</c>'s scaling has to grow both. Before
/// this fix, two independent valuation call sites (<c>SimpleTacticsPolicy.SpellValue</c>
/// and <c>PartyDoctrine.RangedThreatPerRound</c>) summed both fields, pricing Fireball's
/// 8d6 (average 28) at 56.
/// </summary>
/// <remarks>
/// The first fix priced any spell with a non-null <c>Save</c> from
/// <c>Save.FailureDamage</c> — wrong for an attack-plus-save spell (Ice Knife, Arcane
/// Hand), where <c>Encounter.CastSpell</c> checks <c>IsSpellAttack</c> first and
/// <c>ResolveSpellAttack</c> rolls <c>Damage</c> regardless of the attached
/// <c>Save</c>. <see cref="AnAttackPlusSaveSpellPricesFromDamageNotFailureDamage"/>
/// pins the corrected precedence.
/// </remarks>
public class SpellcastingRulesTests
{
    [Fact]
    public void ASaveSpellsDamageCountsOnceNotTwice()
    {
        // Fireball's shape: 8d6 (average 28) carried on both Damage and
        // Save.FailureDamage, as the real extracted spell is. Before #376's fix this
        // summed to 56 — double the printed damage.
        var fireball = SaveSpell("spell.fireball", "Fireball", "8d6");

        Assert.Equal(28, SpellcastingRules.AverageDamage(fireball));
    }

    [Fact]
    public void AnAttackRollSpellsDamageStillCountsFromDamage()
    {
        // No Save at all — an attack-roll spell (Fire Bolt's shape) has its damage on
        // Damage alone, and must still be counted: this is the branch that guards
        // against "just take Save.FailureDamage always" as an equally wrong fix.
        var fireBolt = AttackSpell("spell.fire-bolt", "Fire Bolt", "1d10");

        // 1d10's printed average, rounded down like every other SRD average.
        Assert.Equal(5, SpellcastingRules.AverageDamage(fireBolt));
    }

    [Fact]
    public void AnAttackPlusSaveSpellPricesFromDamageNotFailureDamage()
    {
        // Ice Knife's shape: a ranged spell attack (1d10 Piercing on a hit) plus an
        // unconditional Dexterity save for separate damage (2d6 Cold) that lands
        // whether or not the attack hits. IsSpellAttack is true, so
        // Encounter.CastSpell's attack branch runs and ResolveSpellAttack rolls
        // Damage — Save.FailureDamage is never touched for this spell. The two dice
        // expressions are deliberately different (1d10 vs 2d6) so a helper that reads
        // the wrong field is caught by the number, not just by which field is null.
        var iceKnife = AttackPlusSaveSpell(
            "spell.ice-knife",
            "Ice Knife",
            attackDamage: "1d10",
            saveDamage: "2d6");

        // 1d10's printed average (5), not 2d6's (7).
        Assert.Equal(5, SpellcastingRules.AverageDamage(iceKnife));
    }

    [Fact]
    public void ASaveSpellWithNoDamageValuesAtZero()
    {
        // Hold Person's shape: forces a save, imposes a condition, deals no damage at
        // all. Damage is empty and so is Save.FailureDamage — the estimate must not
        // manufacture damage from nothing.
        var holdPerson = new SpellDefinition
        {
            Id = "spell.hold-person",
            Name = "Hold Person",
            Level = 2,
            School = MagicSchool.Enchantment,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "60 feet",
            RangeFeet = 60,
            Components = SpellComponents.Verbal,
            DurationText = "Concentration, up to 1 minute",
            RequiresConcentration = true,
            Text = "Hold Person",
            Mechanics = EntryMechanics.SavingThrow,
            Save = new SaveEffect(
                Ability.Wisdom,
                DifficultyClass: null,
                Area: null,
                FailureDamage: [],
                SuccessOutcome: SaveSuccessOutcome.NoEffect,
                AppliedConditions: []),
            SourcePage = 1,
        };

        Assert.Equal(0, SpellcastingRules.AverageDamage(holdPerson));
    }

    private static SpellDefinition SaveSpell(string id, string name, string damage)
    {
        var dice = DiceExpression.Parse(damage);
        var components = new[] { new AttackDamage(dice, DamageType.Fire, dice.Average) };

        return new SpellDefinition
        {
            Id = id,
            Name = name,
            Level = 3,
            School = MagicSchool.Evocation,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "150 feet",
            RangeFeet = 150,
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Text = name,
            Mechanics = EntryMechanics.SavingThrow,
            IsSpellAttack = false,
            // The extraction contract: a save spell's damage sits on both fields, the
            // same dice.
            Damage = components,
            Save = new SaveEffect(
                Ability.Dexterity,
                DifficultyClass: null,
                Area: new EffectArea(AreaShape.Sphere, 20),
                FailureDamage: components,
                SuccessOutcome: SaveSuccessOutcome.HalfDamage,
                AppliedConditions: []),
            SourcePage = 1,
        };
    }

    private static SpellDefinition AttackPlusSaveSpell(
        string id,
        string name,
        string attackDamage,
        string saveDamage)
    {
        var attackDice = DiceExpression.Parse(attackDamage);
        var attackComponents = new[] { new AttackDamage(attackDice, DamageType.Piercing, attackDice.Average) };

        var saveDice = DiceExpression.Parse(saveDamage);
        var saveComponents = new[] { new AttackDamage(saveDice, DamageType.Cold, saveDice.Average) };

        return new SpellDefinition
        {
            Id = id,
            Name = name,
            Level = 1,
            School = MagicSchool.Conjuration,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "60 feet",
            RangeFeet = 60,
            Components = SpellComponents.Somatic,
            DurationText = "Instantaneous",
            Text = name,
            Mechanics = EntryMechanics.SavingThrow,
            IsSpellAttack = true,
            Damage = attackComponents,
            Save = new SaveEffect(
                Ability.Dexterity,
                DifficultyClass: null,
                Area: null,
                FailureDamage: saveComponents,
                SuccessOutcome: SaveSuccessOutcome.NoEffect,
                AppliedConditions: []),
            SourcePage = 1,
        };
    }

    private static SpellDefinition AttackSpell(string id, string name, string damage)
    {
        var dice = DiceExpression.Parse(damage);
        var components = new[] { new AttackDamage(dice, DamageType.Fire, dice.Average) };

        return new SpellDefinition
        {
            Id = id,
            Name = name,
            Level = 0,
            School = MagicSchool.Evocation,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "120 feet",
            RangeFeet = 120,
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Text = name,
            Mechanics = EntryMechanics.Attack,
            IsSpellAttack = true,
            Damage = components,
            Save = null,
            SourcePage = 1,
        };
    }
}
