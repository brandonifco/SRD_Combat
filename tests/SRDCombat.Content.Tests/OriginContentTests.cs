using SRDCombat.Content.Validation;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Covers the extracted Character Origins content — the nine species and four
/// backgrounds — against what the SRD prints.
/// </summary>
public class OriginContentTests
{
    private static readonly SrdContent Content = TestContent.Srd;

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
    public void EverySpeciesTraitMapsToExactlyOneClassification()
    {
        // #291's curated-list invariant: SpeciesTraitRegistry.Resolve and .Implements
        // must always agree, for every one of the 33 printed trait instances (28
        // distinct names) across the nine real species — never "implemented" without
        // resolving, or vice versa, which would mean the two questions had drifted
        // apart.
        var names = Content.Species.SelectMany(species => species.Traits).Select(trait => trait.Name).ToList();

        Assert.Equal(33, names.Count);

        Assert.All(names, name =>
            Assert.Equal(SpeciesTraitRegistry.Implements(name), SpeciesTraitRegistry.Resolve(name) is not null));

        // None execute today — the point of the issue. When one does, this line is the
        // one to narrow.
        Assert.All(names, name => Assert.False(SpeciesTraitRegistry.Implements(name)));
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

    [Theory]
    [InlineData(
        "species.dragonborn", "Draconic Ancestry",
        "Your lineage stems from a dragon progenitor. Choose the kind of dragon from " +
        "the Draconic Ancestors table. Your choice affects your Breath Weapon and " +
        "Damage Resistance traits as well as your appearance.")]
    [InlineData(
        "species.elf", "Elven Lineage",
        "You are part of a lineage that grants you supernatural abilities. Choose a " +
        "lineage from the Elven Lineages table. You gain the level 1 benefit of that " +
        "lineage. When you reach character levels 3 and 5, you learn a higher-level " +
        "spell, as shown on the table. You always have that spell prepared. You can " +
        "cast it once without a spell slot, and you regain the ability to cast it in " +
        "that way when you finish a Long Rest. You can also cast the spell using any " +
        "spell slots you have of the appropriate level. Intelligence, Wisdom, or " +
        "Charisma is your spellcasting ability for the spells you cast with this " +
        "trait (choose the ability when you select the lineage).")]
    [InlineData(
        "species.gnome", "Gnomish Lineage",
        "You are part of a lineage that grants you supernatural abilities. Choose one " +
        "of the following options; whichever one you choose, Intelligence, Wisdom, or " +
        "Charisma is your spellcasting ability for the spells you cast with this " +
        "trait (choose the ability when you select the lineage): Forest Gnome. You " +
        "know the Minor Illusion cantrip. You also always have the Speak with Animals " +
        "spell prepared. You can cast it without a spell slot a number of times equal " +
        "to your Proficiency Bonus, and you regain all expended uses when you finish " +
        "a Long Rest. You can also use any spell slots you have to cast the spell. " +
        "Rock Gnome. You know the Mending and Prestidigitation cantrips. In addition, " +
        "you can spend 10 minutes casting Prestidigitation to create a Tiny " +
        "clockwork device (AC 5, 1 HP), such as a toy, fire starter, or music box. " +
        "When you create the device, you determine its function by choosing one " +
        "effect from Prestidigitation; the device produces that effect whenever you " +
        "or another creature takes a Bonus Action to activate it with a touch. If the " +
        "chosen effect has options within it, you choose one of those options for the " +
        "device when you create it. For example, if you choose the spell's " +
        "ignite-extinguish effect, you determine whether the device ignites or " +
        "extinguishes fire; the device doesn't do both. You can have three such " +
        "devices in existence at a time, and each falls apart 8 hours after its " +
        "creation or when you dismantle it with a touch as a Utilize action.")]
    [InlineData(
        "species.human", "Versatile",
        "You gain an Origin feat of your choice (see \"Feats\"). Skilled is recommended.")]
    [InlineData(
        "species.tiefling", "Otherworldly Presence",
        "You know the Thaumaturgy cantrip. When you cast it with this trait, the " +
        "spell uses the same spellcasting ability you use for your Fiendish Legacy " +
        "trait.")]
    public void ATraitNextToAFullWidthTableReadsOnlyItsOwnText(string speciesId, string traitName, string expected)
    {
        // Five traits across five species sit next to one of the chapter's three
        // full-width sub-tables (Draconic Ancestors, Elven Lineages, Fiendish
        // Legacies) in the source PDF. The two-column pass used to slice those tables
        // at the column boundary and interleave the fragments into whichever trait
        // was open — including a neighbouring species' trait, when a wide column
        // crossed over (Elven Lineages into Gnome's Gnomish Lineage, Fiendish
        // Legacies into Human's Versatile). #374. Pinned word-for-word against the
        // printed SRD, pages 83-86.
        var species = Content.SpeciesById[speciesId];
        var trait = species.Traits.Single(candidate => candidate.Name == traitName);

        Assert.Equal(expected, trait.Text);
    }

    [Fact]
    public void NoSpeciesTraitCarriesTableNoise()
    {
        // The positive counterpart to the theory above: assert it holds for every
        // trait the chapter extracts, not just the five known-affected ones, so a
        // future table added to this chapter fails loudly rather than shipping
        // garbled text to a player choosing their species.
        var result = OriginValidator.ValidateSpecies(Content.Species);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "species.trait.table_noise");
    }

    [Fact]
    public void ATraitCarryingATableRowIsRejected()
    {
        // The shape the bug actually produced: a run of Title-Case column entries
        // with no connecting lowercase word, appended onto otherwise-legitimate
        // prose. Constructed rather than replayed from the fixed extraction, so this
        // keeps failing even after the extraction itself is clean.
        var dwarf = Content.SpeciesById["species.dwarf"];
        var polluted = dwarf.Traits.Select(trait => trait.Name == "Darkvision"
                ? trait with { Text = trait.Text + " Draconic Ancestors Dragon Damage Type Black Acid Gold Fire" }
                : trait)
            .ToArray();
        var broken = dwarf with { Traits = polluted };

        var result = OriginValidator.ValidateSpecies([broken]);

        Assert.Contains(result.Errors, issue => issue.Code == "species.trait.table_noise");
    }
}
