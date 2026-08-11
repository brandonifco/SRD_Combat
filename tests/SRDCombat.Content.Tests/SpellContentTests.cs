using SRDCombat.Content.Validation;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Covers the extracted Spell Descriptions against what the SRD prints.
/// </summary>
public class SpellContentTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void TheWholeSpellListIsExtracted()
    {
        Assert.True(Content.Spells.Count >= 300, $"Only {Content.Spells.Count} spells were extracted.");

        // Levels 0-9 are all represented, which is a cheap check that no band was missed.
        Assert.Equal(
            Enumerable.Range(0, 10),
            Content.Spells.Select(spell => spell.Level).Distinct().Order());

        Assert.All(Content.Spells, spell => Assert.NotEmpty(spell.Classes));
    }

    [Fact]
    public void ACantripIsLevelZero()
    {
        var fireBolt = Content.SpellsById["spell.fire-bolt"];

        Assert.True(fireBolt.IsCantrip);
        Assert.Equal(0, fireBolt.Level);
        Assert.Equal(MagicSchool.Evocation, fireBolt.School);
        Assert.Equal(["Sorcerer", "Wizard"], fireBolt.Classes);
    }

    [Fact]
    public void ALevelledSpellMatchesItsPrintedHeader()
    {
        // "Level 3 Evocation (Sorcerer, Wizard) / Casting Time: Action / Range: 150 feet
        //  / Components: V, S, M (a ball of bat guano and sulfur) / Duration: Instantaneous"
        var fireball = Content.SpellsById["spell.fireball"];

        Assert.Equal(3, fireball.Level);
        Assert.Equal(MagicSchool.Evocation, fireball.School);
        Assert.Equal(SpellCastingTime.Action, fireball.CastingTime);
        Assert.Equal(150, fireball.RangeFeet);
        Assert.False(fireball.RequiresConcentration);

        Assert.True(fireball.Components.HasFlag(SpellComponents.Verbal));
        Assert.True(fireball.Components.HasFlag(SpellComponents.Somatic));
        Assert.True(fireball.Components.HasFlag(SpellComponents.Material));
        Assert.NotNull(fireball.MaterialComponent);
    }

    [Fact]
    public void SavingThrowSpellsAreStructured()
    {
        // The spell grammar differs from the stat block one in substance: no printed DC,
        // because it comes from the caster, and no precomputed average.
        var fireball = Content.SpellsById["spell.fireball"];
        var save = Assert.IsType<SaveEffect>(fireball.Save);

        Assert.Equal(Ability.Dexterity, save.Ability);
        Assert.Null(save.DifficultyClass);
        Assert.Equal(SaveSuccessOutcome.HalfDamage, save.SuccessOutcome);

        var area = Assert.IsType<EffectArea>(save.Area);
        Assert.Equal(AreaShape.Sphere, area.Shape);
        Assert.Equal(20, area.SizeFeet);

        Assert.Equal("8d6", save.FailureDamage[0].Amount.ToString());
        Assert.Equal(DamageType.Fire, save.FailureDamage[0].Type);
    }

    [Fact]
    public void AConeSpellReadsItsArea()
    {
        var save = Assert.IsType<SaveEffect>(Content.SpellsById["spell.burning-hands"].Save);
        var area = Assert.IsType<EffectArea>(save.Area);

        Assert.Equal(AreaShape.Cone, area.Shape);
        Assert.Equal(15, area.SizeFeet);
    }

    [Fact]
    public void AttackSpellsAreDistinguishedFromSaveSpells()
    {
        var fireBolt = Content.SpellsById["spell.fire-bolt"];

        Assert.True(fireBolt.IsSpellAttack);
        Assert.Null(fireBolt.Save);
        Assert.Equal(EntryMechanics.Attack, fireBolt.Mechanics);

        // The damage is captured for attack spells too, not just save spells.
        Assert.Equal("1d10", Assert.Single(fireBolt.Damage).Amount.ToString());
        Assert.Equal(DamageType.Fire, fireBolt.Damage[0].Type);

        Assert.False(Content.SpellsById["spell.fireball"].IsSpellAttack);
    }

    [Fact]
    public void ConcentrationIsReadFromTheDuration()
    {
        // Over a hundred spells need Concentration; it is stated only in the duration.
        Assert.True(Content.Spells.Count(spell => spell.RequiresConcentration) > 100);

        Assert.All(
            Content.Spells.Where(spell => spell.RequiresConcentration),
            spell => Assert.Contains("Concentration", spell.DurationText, StringComparison.OrdinalIgnoreCase));

        Assert.All(
            Content.Spells.Where(spell => !spell.RequiresConcentration),
            spell => Assert.DoesNotContain("Concentration", spell.DurationText, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CastingTimesAreClassifiedForTheActionEconomy()
    {
        // Most spells cost an Action; a handful are Bonus Actions or Reactions, and the
        // rest take a minute or more and cannot be cast in a fight at all.
        var usable = Content.Spells.Count(spell => spell.IsUsableInCombat);

        Assert.True(usable > 200, $"Only {usable} spells are castable in combat.");
        Assert.Contains(Content.Spells, spell => spell.CastingTime == SpellCastingTime.BonusAction);
        Assert.Contains(Content.Spells, spell => spell.CastingTime == SpellCastingTime.Reaction);
        Assert.Contains(Content.Spells, spell => !spell.IsUsableInCombat);
    }

    [Fact]
    public void RangeIsReadAsFeetOnlyWhereItIsADistance()
    {
        // "Self" and "Touch" have no numeric range, and "Self (15-foot Cone)" must not
        // report 15 feet — that is the area's size, not how far it reaches.
        Assert.Null(Content.SpellsById["spell.burning-hands"].RangeFeet);
        Assert.Equal(120, Content.SpellsById["spell.fire-bolt"].RangeFeet);

        Assert.All(
            Content.Spells.Where(spell => spell.RangeText.StartsWith("Self", StringComparison.Ordinal)),
            spell => Assert.Null(spell.RangeFeet));
    }

    [Fact]
    public void UpcastingIsKeptAsTextRatherThanStructured()
    {
        // Upcasting is not implemented, and structuring it would imply it is.
        var fireball = Content.SpellsById["spell.fireball"];

        Assert.NotNull(fireball.ScalingText);
        Assert.Contains("Higher-Level Spell Slot", fireball.ScalingText, StringComparison.Ordinal);

        // The scaling clause is held apart from the spell's own description, so its dice
        // do not leak into the spell's damage.
        Assert.DoesNotContain("Higher-Level", fireball.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySpellIsClassified()
    {
        Assert.All(Content.Spells, spell => Assert.True(Enum.IsDefined(spell.Mechanics)));

        Assert.All(
            Content.Spells.Where(spell => spell.Mechanics == EntryMechanics.Unmodelled),
            spell => Assert.NotEmpty(spell.UnmodelledClauses));
    }

    [Fact]
    public void EveryClassSpellListNamesAKnownClass()
    {
        var known = Content.Classes.Select(definition => definition.Name).ToHashSet(StringComparer.Ordinal);

        var unknown = Content.Spells
            .SelectMany(spell => spell.Classes)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !known.Contains(name))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void ASpellMissingItsComponentsIsAWarningRatherThanAnError()
    {
        // Six spells sit at the foot of a column and have their Components and Duration
        // lines missing from the PDF's text layer entirely — a defect in the source, not
        // in the parser, confirmed with two independent extractors. Inventing plausible
        // values would be worse than shipping the gap visibly.
        var result = SpellValidator.Validate(Content.Spells);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, issue => issue.Code == "spell.components.none");
    }
}
