using SRDCombat.Core.Definitions;
using SrdExtract.Parsing;
using SrdExtract.Pdf;

namespace SrdExtract.Tests;

public sealed class MonsterParserTests
{
    [Fact]
    public void SpellcastingKeepsBodyUsageTiersAndRejectsInterleavedItalicMetadata()
    {
        var result = MonsterParser.Parse(
        [
            Line(StatBlockFonts.Name, "Test Priest", height: 10),
            Line(StatBlockFonts.Italic, "Medium Humanoid, Neutral"),
            Line(StatBlockFonts.Stat, "AC 12"),
            Line(StatBlockFonts.Stat, "HP 11 (2d8 + 2)"),
            Line(StatBlockFonts.Stat, "Speed 30 ft."),
            Line(StatBlockFonts.AbilityTable, "10 +0 +0 10 +0 +0 10 +0 +0"),
            Line(StatBlockFonts.AbilityTable, "10 +0 +0 10 +0 +0 10 +0 +0"),
            Line(StatBlockFonts.Stat, "CR 1/8 (XP 25; PB +2)"),
            Entry("Spellcasting.", "The priest casts one of the following spells:"),
            Line(StatBlockFonts.Italic, "Large Elemental, Neutral"),
            Line(StatBlockFonts.Stat, "At Will: Light, Thaumaturgy"),
            Line(StatBlockFonts.Stat, "1/Day Each: Bless,"),
            Line(StatBlockFonts.Italic, "Healing Word"),
        ]);

        var priest = Assert.Single(result.Monsters);
        var spellcasting = Assert.Single(priest.Entries);

        Assert.Equal("Spellcasting", spellcasting.Name);
        Assert.Contains("At Will: Light, Thaumaturgy", spellcasting.Text, StringComparison.Ordinal);
        Assert.Contains("1/Day Each: Bless, Healing Word", spellcasting.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Large Elemental, Neutral", spellcasting.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageTierWithoutAnOpenSpellcastingEntryFallsThroughToStatParsing()
    {
        var result = MonsterParser.Parse(
        [
            Line(StatBlockFonts.Name, "Test Priest", height: 10),
            Line(StatBlockFonts.Italic, "Medium Humanoid, Neutral"),
            Line(StatBlockFonts.Stat, "AC 12"),
            Line(StatBlockFonts.Stat, "HP 11 (2d8 + 2)"),
            Line(StatBlockFonts.Stat, "Speed 30 ft."),
            Line(StatBlockFonts.AbilityTable, "10 +0 +0 10 +0 +0 10 +0 +0"),
            Line(StatBlockFonts.AbilityTable, "10 +0 +0 10 +0 +0 10 +0 +0"),
            Line(StatBlockFonts.Stat, "At Will: Light"),
            Line(StatBlockFonts.Stat, "CR 1/8 (XP 25; PB +2)"),
        ]);

        var priest = Assert.Single(result.Monsters);

        Assert.Empty(priest.Entries);
        Assert.Equal(0.125m, priest.ChallengeRating);
        Assert.Equal(25, priest.ExperiencePoints);
    }

    private static SourceLine Line(string font, string text, double height = 8) =>
        new(1, 0, 500, [new SourceWord(text, font, 60, 100, height, 500)]);

    private static SourceLine Entry(string heading, string body) =>
        new(
            1,
            0,
            500,
            [
                new SourceWord(heading, StatBlockFonts.EntryName, 60, 100, 8, 500),
                new SourceWord(body, StatBlockFonts.Body, 105, 300, 8, 500),
            ]);
}
