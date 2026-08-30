using SRDCombat.Content.Validation;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Covers the extracted Classes chapter against what the SRD prints.
/// </summary>
/// <remarks>
/// The level tables are checked at level 5 throughout, because that is the top of the
/// band this game supports and the row where the most columns have interesting values.
/// </remarks>
public class ClassContentTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    /// <summary>The six the game launches with. The extractor deliberately reads all twelve.</summary>
    private static readonly string[] LaunchClasses =
        ["Barbarian", "Cleric", "Fighter", "Ranger", "Rogue", "Wizard"];

    [Fact]
    public void AllTwelveClassesAreExtracted()
    {
        Assert.Equal(12, Content.Classes.Count);

        Assert.All(
            LaunchClasses,
            name => Assert.Contains(Content.Classes, definition => definition.Name == name));
    }

    [Fact]
    public void EveryClassTableRunsOneToTwentyWithTheRightProficiencyBonus()
    {
        // The proficiency bonus is fixed by the Character Advancement table independently
        // of any class, which makes it the best available check that a level table's
        // columns were read correctly.
        Assert.All(Content.Classes, definition =>
        {
            Assert.Equal(20, definition.Levels.Count);

            Assert.All(definition.Levels, level =>
                Assert.Equal(
                    AdvancementRules.ProficiencyBonusForLevel(level.Level),
                    level.ProficiencyBonus));
        });
    }

    [Fact]
    public void CoreTraitsMatchThePrintedTable()
    {
        var barbarian = Content.ClassesById["class.barbarian"];

        Assert.Equal([Ability.Strength], barbarian.PrimaryAbilities);
        Assert.Equal(12, barbarian.HitDieSides);
        Assert.Equal([Ability.Strength, Ability.Constitution], barbarian.SavingThrowProficiencies);
        Assert.Equal("Simple and Martial weapons", barbarian.WeaponProficiencies);
        Assert.Equal("Light and Medium armor and Shields", barbarian.ArmorTraining);
        Assert.Contains("Greataxe", barbarian.StartingEquipment, StringComparison.Ordinal);
    }

    [Fact]
    public void AClassWithAChoiceOfPrimaryAbilityKeepsBoth()
    {
        // "Primary Ability: Strength or Dexterity"
        Assert.Equal(
            [Ability.Strength, Ability.Dexterity],
            Content.ClassesById["class.fighter"].PrimaryAbilities);
    }

    [Fact]
    public void SkillChoiceListsAreCompleteRatherThanTruncated()
    {
        // The skill list wraps across several lines in a lighter font weight. An early
        // version matched only the bold face and silently kept the first line, leaving
        // the Barbarian choosing 2 skills from a list of 1.
        var barbarian = Content.ClassesById["class.barbarian"];

        Assert.Equal(2, barbarian.SkillChoiceCount);
        Assert.Equal(
            ["Animal Handling", "Athletics", "Intimidation", "Nature", "Perception", "Survival"],
            barbarian.SkillChoices);

        // The Rogue's is the longest list in the book, and it chooses the most.
        var rogue = Content.ClassesById["class.rogue"];
        Assert.Equal(4, rogue.SkillChoiceCount);
        Assert.Equal(10, rogue.SkillChoices.Count);
    }

    [Fact]
    public void AClassThatChoosesFromAnySkillSaysSoRatherThanHavingAnEmptyList()
    {
        // The Bard's "Choose any 3 skills" is not a parse failure, and the two have to be
        // distinguishable — an empty list on its own would look identical to a bad read.
        var bard = Content.ClassesById["class.bard"];

        Assert.True(bard.ChoosesAnySkill);
        Assert.Equal(3, bard.SkillChoiceCount);
        Assert.Empty(bard.SkillChoices);

        Assert.All(
            Content.Classes.Where(definition => !definition.ChoosesAnySkill),
            definition => Assert.NotEmpty(definition.SkillChoices));
    }

    [Fact]
    public void SpellSlotsMatchThePrintedCasterTables()
    {
        // Full casters at level 5: four 1st, three 2nd, two 3rd.
        foreach (var id in new[] { "class.cleric", "class.wizard", "class.druid", "class.bard", "class.sorcerer" })
        {
            var level5 = Content.ClassesById[id].AtLevel(5);

            Assert.NotNull(level5);
            Assert.Equal(new Dictionary<int, int> { [1] = 4, [2] = 3, [3] = 2 }, level5.SpellSlots);
        }

        // Half casters reach only 2nd-level spells by 5.
        foreach (var id in new[] { "class.ranger", "class.paladin" })
        {
            Assert.Equal(
                new Dictionary<int, int> { [1] = 4, [2] = 2 },
                Content.ClassesById[id].AtLevel(5)!.SpellSlots);
        }
    }

    [Fact]
    public void NonCastersHaveNoSpellSlots()
    {
        foreach (var id in new[] { "class.barbarian", "class.fighter", "class.rogue", "class.monk" })
        {
            Assert.False(Content.ClassesById[id].IsSpellcaster);
        }
    }

    [Fact]
    public void TheWarlockKeepsPactMagicRatherThanBeingForcedIntoSpellSlotColumns()
    {
        // The Warlock's table has "Spell Slots" and "Slot Level" columns instead of the
        // nine per-level ones. Reading it as an ordinary caster would be wrong, so it
        // correctly reports no spell slots and carries the real columns as resources.
        var warlock = Content.ClassesById["class.warlock"].AtLevel(5)!;

        Assert.Empty(warlock.SpellSlots);
        Assert.Equal("2", warlock.Resources["Spell Slots"]);
        Assert.Equal("3", warlock.Resources["Slot Level"]);
    }

    [Fact]
    public void ClassSpecificResourceColumnsAreNamedCorrectly()
    {
        // Every one of these is a stacked two-word header reassembled from two lines,
        // except the Rogue's, which is printed side by side with no stacked row at all.
        var level5 = LaunchClasses.ToDictionary(
            name => name,
            name => Content.Classes.Single(definition => definition.Name == name).AtLevel(5)!);

        Assert.Equal("3", level5["Barbarian"].Resources["Rages"]);
        Assert.Equal("+2", level5["Barbarian"].Resources["Rage Damage"]);
        Assert.Equal("4", level5["Fighter"].Resources["Weapon Mastery"]);
        Assert.Equal("2", level5["Cleric"].Resources["Channel Divinity"]);
        Assert.Equal("4", level5["Cleric"].Resources["Cantrips"]);
        Assert.Equal("3d6", level5["Rogue"].Resources["Sneak Attack"]);
    }

    [Fact]
    public void FeatureNamesAppearOnTheRightLevels()
    {
        var fighter = Content.ClassesById["class.fighter"];

        Assert.Contains("Fighting Style", fighter.AtLevel(1)!.FeatureNames);
        Assert.Contains("Second Wind", fighter.AtLevel(1)!.FeatureNames);
        Assert.Contains("Extra Attack", fighter.AtLevel(5)!.FeatureNames);

        Assert.Contains("Rage", Content.ClassesById["class.barbarian"].AtLevel(1)!.FeatureNames);
        Assert.Contains("Sneak Attack", Content.ClassesById["class.rogue"].AtLevel(1)!.FeatureNames);
    }

    [Fact]
    public void EveryClassFeatureIsClassified()
    {
        // Same rule as everything else: a feature may be Unmodelled, never unexamined.
        var features = Content.Classes.SelectMany(definition => definition.Features).ToList();

        Assert.NotEmpty(features);
        Assert.All(features, feature => Assert.True(Enum.IsDefined(feature.Mechanics)));

        Assert.All(
            features.Where(feature => feature.Mechanics == EntryMechanics.Unmodelled),
            feature => Assert.NotEmpty(feature.UnmodelledClauses));
    }

    [Fact]
    public void AClassTableMissingRowsIsRejected()
    {
        var broken = Content.ClassesById["class.fighter"] with
        {
            Levels = Content.ClassesById["class.fighter"].Levels.Take(5).ToArray(),
        };

        Assert.Contains(
            ClassValidator.Validate([broken]).Errors,
            issue => issue.Code == "class.levels.wrong_count");
    }

    [Fact]
    public void AClassTableWithTheWrongProficiencyBonusIsRejected()
    {
        // The check that would catch a misaligned column, which is the characteristic
        // way a wide table goes wrong.
        var fighter = Content.ClassesById["class.fighter"];
        var levels = fighter.Levels.ToList();
        levels[0] = levels[0] with { ProficiencyBonus = 5 };

        Assert.Contains(
            ClassValidator.Validate([fighter with { Levels = levels }]).Errors,
            issue => issue.Code == "class.level.proficiency_bonus_wrong");
    }
}
