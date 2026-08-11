using SRDCombat.Content.Validation;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Covers the extracted Character Origins content — the nine species and four
/// backgrounds — against what the SRD prints.
/// </summary>
public class OriginContentTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void EverySpeciesAndBackgroundIsExtracted()
    {
        // Both are closed sets in the SRD, so exact counts are a real regression check.
        Assert.Equal(9, Content.Species.Count);
        Assert.Equal(4, Content.Backgrounds.Count);

        Assert.Equal(
            ["Dragonborn", "Dwarf", "Elf", "Gnome", "Goliath", "Halfling", "Human", "Orc", "Tiefling"],
            Content.Species.Select(species => species.Name).Order(StringComparer.Ordinal));

        Assert.Equal(
            ["Acolyte", "Criminal", "Sage", "Soldier"],
            Content.Backgrounds.Select(background => background.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASpeciesMatchesItsPrintedTraits()
    {
        var dwarf = Content.SpeciesById["species.dwarf"];

        Assert.Equal(CreatureType.Humanoid, dwarf.CreatureType);
        Assert.Equal([CreatureSize.Medium], dwarf.Sizes);
        Assert.Equal(30, dwarf.SpeedFeet);

        Assert.Equal(
            ["Darkvision", "Dwarven Resilience", "Dwarven Toughness", "Stonecunning"],
            dwarf.Traits.Select(trait => trait.Name));

        var resilience = dwarf.Traits.Single(trait => trait.Name == "Dwarven Resilience");
        Assert.Contains("Resistance to Poison damage", resilience.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOneSpeciesWithAnUnusualSpeedIsReadCorrectly()
    {
        // Every SRD species walks 30 feet except the Goliath, which walks 35. A parser
        // that defaulted the speed rather than reading it would pass on all the others.
        Assert.Equal(35, Content.SpeciesById["species.goliath"].SpeedFeet);

        Assert.All(
            Content.Species.Where(species => species.Id != "species.goliath"),
            species => Assert.Equal(30, species.SpeedFeet));
    }

    [Fact]
    public void SpeciesThatLetThePlayerChooseASizeKeepBoth()
    {
        // Humans and Tieflings choose Small or Medium; the rest are fixed.
        Assert.Equal([CreatureSize.Medium, CreatureSize.Small], Content.SpeciesById["species.human"].Sizes);
        Assert.Equal([CreatureSize.Medium, CreatureSize.Small], Content.SpeciesById["species.tiefling"].Sizes);
        Assert.Equal([CreatureSize.Small], Content.SpeciesById["species.gnome"].Sizes);
    }

    [Fact]
    public void ABackgroundMatchesItsPrintedBlock()
    {
        var soldier = Content.BackgroundsById["background.soldier"];

        Assert.Equal(
            [Ability.Strength, Ability.Dexterity, Ability.Constitution],
            soldier.AbilityScores);
        Assert.Equal("Savage Attacker", soldier.FeatName);
        Assert.Equal(["Athletics", "Intimidation"], soldier.SkillProficiencies);
        Assert.Equal("Choose one kind of Gaming Set", soldier.ToolProficiency);
        Assert.Contains("Spear", soldier.Equipment, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundFeatsDropTheCrossReferenceButKeepTheirQualifier()
    {
        // "Magic Initiate (Wizard) (see "Feats")" — the parenthesised class matters and
        // the cross-reference does not.
        Assert.Equal("Magic Initiate (Wizard)", Content.BackgroundsById["background.sage"].FeatName);
        Assert.Equal("Magic Initiate (Cleric)", Content.BackgroundsById["background.acolyte"].FeatName);
        Assert.Equal("Alert", Content.BackgroundsById["background.criminal"].FeatName);
    }

    [Fact]
    public void EveryBackgroundFollowsTheSrdsOwnRules()
    {
        // The SRD states these as universal: three ability scores, two skills, one feat,
        // one tool. Anything else means a block was misread.
        Assert.All(Content.Backgrounds, background =>
        {
            Assert.Equal(3, background.AbilityScores.Count);
            Assert.Equal(2, background.SkillProficiencies.Count);
            Assert.NotEmpty(background.FeatName);
            Assert.NotEmpty(background.ToolProficiency);
        });
    }

    [Fact]
    public void EverySpeciesTraitIsClassified()
    {
        // The same rule as stat block entries: a trait may be Unmodelled, but it can
        // never be unexamined. Species traits are mechanics — Dwarven Resilience grants
        // Poison resistance — so they are held to the same standard.
        var traits = Content.Species.SelectMany(species => species.Traits).ToList();

        Assert.NotEmpty(traits);
        Assert.All(traits, trait => Assert.True(Enum.IsDefined(trait.Mechanics)));

        // Nothing may be silently dismissed: an Unmodelled trait must say what it could
        // not express.
        Assert.All(
            traits.Where(trait => trait.Mechanics == EntryMechanics.Unmodelled),
            trait => Assert.NotEmpty(trait.UnmodelledClauses));
    }

    [Fact]
    public void ABackgroundMissingAnAbilityScoreIsRejected()
    {
        var broken = Content.BackgroundsById["background.soldier"] with
        {
            AbilityScores = [Ability.Strength, Ability.Dexterity],
        };

        var result = OriginValidator.ValidateBackgrounds([broken]);

        Assert.Contains(result.Errors, issue => issue.Code == "background.abilities.wrong_count");
    }

    [Fact]
    public void ASpeciesWithNoTraitsIsRejected()
    {
        // The check that caught the real bug during extraction: the player-facing
        // chapters set trait names in Cambria rather than the bestiary's Optima, so the
        // first run produced nine species with zero traits between them.
        var broken = Content.SpeciesById["species.dwarf"] with { Traits = [] };

        var result = OriginValidator.ValidateSpecies([broken]);

        Assert.Contains(result.Errors, issue => issue.Code == "species.traits.missing");
    }
}
