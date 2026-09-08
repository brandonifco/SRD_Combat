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
    public void NoSpeciesTraitReachesNarrativeExceptFromACuratedList()
    {
        // Mirrors the spells' invariant (#357). EntryMechanicsParser.KnownInertEntries
        // is curated about stat block and species/class trait text, so — unlike
        // spells — a species trait consulting it is the intended reading, not a
        // collision (ClassifyTrait's consultInertList defaults to true for exactly
        // this shape). But the list holds bestiary names today (Amphibious, Water
        // Breathing, Illumination), never a species/class one, so nothing here has
        // actually been judged inert for this shape yet. "Brave" is a real Halfling
        // rule that shares nothing with the bestiary list today — but the guard
        // should exist before a future name collision smuggles a species rule into
        // Narrative unexamined (#377). Assert zero until this project curates a
        // species/class-specific list; if this ever needs to allow a name, it should
        // do so through a named, reasoned exception, not by going silent.
        var traits = Content.Species.SelectMany(species => species.Traits).ToList();

        Assert.DoesNotContain(traits, trait => trait.Mechanics == EntryMechanics.Narrative);
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
    public void AnImplementedTraitNameCarriesOnePrintedTextAcrossEverySpecies()
    {
        // #346: SpeciesTraitRegistry keys on the bare printed name, so once any name
        // is registered as implemented, *every* species instance of that name
        // resolves to the same SpeciesTrait -- even a variant with genuinely
        // different printed rules. Darkvision is the concrete case: 60 feet for four
        // species, 120 feet for the Dwarf and Orc. This is a trip-wire, not a
        // feature test: it is vacuously true today (nothing is implemented, so the
        // Where below drops every group), and it must start failing the day
        // "Darkvision" -- or any other name whose printed texts differ -- is
        // registered without every variant sharing identical text. See
        // SpeciesTraitRegistry's remarks for the contract this enforces.
        //
        // Darkvision is the concrete case: 60 feet for four species (Dragonborn,
        // Elf, Gnome, Tiefling) and 120 feet for two (Dwarf, Orc). Grouping is
        // case-insensitive to match the registry's OrdinalIgnoreCase lookup, so a
        // case-only name variant with differing text cannot resolve to one entry
        // while slipping past this guard.
        var traits = Content.Species.SelectMany(species => species.Traits).ToList();

        var implementedGroups = traits
            .GroupBy(trait => trait.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => SpeciesTraitRegistry.Implements(group.Key));

        Assert.All(implementedGroups, group =>
        {
            var distinctTexts = group.Select(trait => trait.Text).Distinct(StringComparer.Ordinal).ToList();
            Assert.True(
                distinctTexts.Count == 1,
                $"'{group.Key}' is registered as implemented but carries {distinctTexts.Count} " +
                "different printed texts across species -- a variant would silently execute " +
                "under the wrong rule. See #346.");
        });
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
    public void DraconicAncestorsIsCapturedAsTenRows()
    {
        // #381: the table Draconic Ancestry names, captured rather than dropped.
        // Verified against SRD 5.2.1 p. 84 — five printed rows of two side-by-side
        // (Dragon, Damage Type) pairs, read here as ten rows of one pair each.
        var table = Assert.Single(Content.SpeciesById["species.dragonborn"].Tables);

        Assert.Equal("Draconic Ancestors", table.Name);
        Assert.Equal(["Dragon", "Damage Type"], table.Columns);
        Assert.Equal(
            [
                ["Black", "Acid"],
                ["Gold", "Fire"],
                ["Blue", "Lightning"],
                ["Green", "Poison"],
                ["Brass", "Fire"],
                ["Red", "Fire"],
                ["Bronze", "Lightning"],
                ["Silver", "Cold"],
                ["Copper", "Acid"],
                ["White", "Cold"],
            ],
            table.Rows);
    }

    [Fact]
    public void ElvenLineagesIsCapturedWithAllThreeColumns()
    {
        // #381. Verified against SRD 5.2.1 p. 85 — the table Elven Lineage names,
        // whose Level 3 and Level 5 columns are the ones the two-column pass alone
        // cannot keep with the row (#374): they sit past the column boundary.
        var table = Assert.Single(Content.SpeciesById["species.elf"].Tables);

        Assert.Equal("Elven Lineages", table.Name);
        Assert.Equal(["Lineage", "Level 1", "Level 3", "Level 5"], table.Columns);
        Assert.Equal(3, table.Rows.Count);

        Assert.Equal(
            [
                "Drow",
                "The range of your Darkvision increases to 120 feet. You also know " +
                "the Dancing Lights cantrip.",
                "Faerie Fire",
                "Darkness",
            ],
            table.Rows[0]);

        Assert.Equal(
            [
                "High Elf",
                "You know the Prestidigitation cantrip. Whenever you finish a Long " +
                "Rest, you can replace that cantrip with a different cantrip from " +
                "the Wizard spell list.",
                "Detect Magic",
                "Misty Step",
            ],
            table.Rows[1]);

        Assert.Equal(
            [
                "Wood Elf",
                "Your Speed increases to 35 feet. You also know the Druidcraft cantrip.",
                "Longstrider",
                "Pass without Trace",
            ],
            table.Rows[2]);
    }

    [Fact]
    public void FiendishLegaciesIsCapturedWithAllThreeColumns()
    {
        // #381. Verified against SRD 5.2.1 p. 86 — the table Fiendish Legacy names.
        var table = Assert.Single(Content.SpeciesById["species.tiefling"].Tables);

        Assert.Equal("Fiendish Legacies", table.Name);
        Assert.Equal(["Legacy", "Level 1", "Level 3", "Level 5"], table.Columns);

        Assert.Equal(
            [
                "Abyssal",
                "You have Resistance to Poison damage. You also know the Poison " +
                "Spray cantrip.",
                "Ray of Sickness",
                "Hold Person",
            ],
            table.Rows[0]);

        Assert.Equal(
            [
                "Chthonic",
                "You have Resistance to Necrotic damage. You also know the Chill " +
                "Touch cantrip.",
                "False Life",
                "Ray of Enfeeblement",
            ],
            table.Rows[1]);

        Assert.Equal(
            [
                "Infernal",
                "You have Resistance to Fire damage. You also know the Fire Bolt cantrip.",
                "Hellish Rebuke",
                "Darkness",
            ],
            table.Rows[2]);
    }

    [Fact]
    public void OnlyTheThreeNamedSpeciesCarryATable()
    {
        // The positive counterpart to the three tests above: the other six species —
        // whose pages carry no full-width table — extract with none.
        var withTables = Content.Species.Where(species => species.Tables.Count > 0)
            .Select(species => species.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Dragonborn", "Elf", "Tiefling"], withTables);
    }

    [Fact]
    public void EveryOriginTableFollowsTheSrdsOwnRowAndColumnCounts()
    {
        // The general lesson (docs/guides/extraction.md): assert the shape of what
        // should have been found. Every row also has exactly as many cells as the
        // table has columns, and no cell is blank.
        var result = OriginValidator.ValidateSpecies(Content.Species);

        Assert.DoesNotContain(result.Issues, issue => issue.Code.StartsWith("species.table.", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingTableIsRejected()
    {
        var dragonborn = Content.SpeciesById["species.dragonborn"];
        var broken = dragonborn with { Tables = [] };

        var result = OriginValidator.ValidateSpecies(
            Content.Species.Where(species => species.Id != dragonborn.Id).Append(broken).ToArray());

        Assert.Contains(result.Errors, issue => issue.Code == "species.table.missing" && issue.Subject == "Draconic Ancestors");
    }

    [Fact]
    public void ATableWithTheWrongRowCountIsRejected()
    {
        var elf = Content.SpeciesById["species.elf"];
        var table = elf.Tables.Single();
        var broken = elf with { Tables = [table with { Rows = table.Rows.Take(2).ToArray() }] };

        var result = OriginValidator.ValidateSpecies([broken]);

        Assert.Contains(result.Errors, issue => issue.Code == "species.table.row_count");
    }

    [Fact]
    public void ATableRowWithTheWrongCellCountIsRejected()
    {
        var tiefling = Content.SpeciesById["species.tiefling"];
        var table = tiefling.Tables.Single();
        var polluted = table.Rows.Select((row, index) => index == 0 ? (IReadOnlyList<string>)[.. row, "extra"] : row).ToArray();
        var broken = tiefling with { Tables = [table with { Rows = polluted }] };

        var result = OriginValidator.ValidateSpecies([broken]);

        Assert.Contains(result.Errors, issue => issue.Code == "species.table.row_shape");
    }

    [Fact]
    public void ATableWithABlankCellIsRejected()
    {
        var dragonborn = Content.SpeciesById["species.dragonborn"];
        var table = dragonborn.Tables.Single();
        var polluted = table.Rows.Select((row, index) => index == 0 ? (IReadOnlyList<string>)[row[0], " "] : row).ToArray();
        var broken = dragonborn with { Tables = [table with { Rows = polluted }] };

        var result = OriginValidator.ValidateSpecies([broken]);

        Assert.Contains(result.Errors, issue => issue.Code == "species.table.cell_empty");
    }

    [Fact]
    public void ATableWithMergedColumnsIsRejected()
    {
        // The shape a boundary miscount would actually produce: Level 3 and Level 5
        // silently merge into one column, so both the row count and each row's own
        // cell count still agree with the (now three-column) table — a row-shape
        // check alone cannot see this. Only comparing the printed column list can.
        var elf = Content.SpeciesById["species.elf"];
        var table = elf.Tables.Single();
        var merged = table with
        {
            Columns = ["Lineage", "Level 1", "Level 3/5"],
            Rows = table.Rows
                .Select(row => (IReadOnlyList<string>)[row[0], row[1], $"{row[2]} / {row[3]}"])
                .ToArray(),
        };
        var broken = elf with { Tables = [merged] };

        var result = OriginValidator.ValidateSpecies([broken]);

        Assert.Contains(result.Errors, issue => issue.Code == "species.table.columns_mismatch");
    }

    [Fact]
    public void AnUnknownTableNameIsRejected()
    {
        var dragonborn = Content.SpeciesById["species.dragonborn"];
        var table = dragonborn.Tables.Single();
        var broken = dragonborn with { Tables = [table with { Name = "Draconic Ancestors Extended" }] };

        var result = OriginValidator.ValidateSpecies(
            Content.Species.Where(species => species.Id != dragonborn.Id).Append(broken).ToArray());

        Assert.Contains(result.Errors, issue => issue.Code == "species.table.unknown");
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
