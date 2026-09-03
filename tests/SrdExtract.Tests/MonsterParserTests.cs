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

    [Fact]
    public void LegendaryActionsPreambleGoesOnTheMonsterNotTheLastActionEntry()
    {
        // Modelled on the Aboleth's own block (#423): the "Legendary Actions" header is
        // followed by an unheaded preamble paragraph, split across two visual lines,
        // before the first named legendary action. It must not fall through onto
        // Dominate Mind, the last Action entry open when the header appeared.
        var result = MonsterParser.Parse(
        [
            Line(StatBlockFonts.Name, "Aboleth", height: 10),
            Line(StatBlockFonts.Italic, "Large Aberration, Lawful Evil"),
            Line(StatBlockFonts.Stat, "AC 17"),
            Line(StatBlockFonts.Stat, "HP 150 (20d10 + 40)"),
            Line(StatBlockFonts.Stat, "Speed 10 ft., Swim 40 ft."),
            Line(StatBlockFonts.AbilityTable, "21 +5 +5 9 -1 +3 15 +2 +6"),
            Line(StatBlockFonts.AbilityTable, "18 +4 +8 15 +2 +6 18 +4 +4"),
            Line(StatBlockFonts.Stat, "CR 10 (XP 5,900; PB +4)"),
            Line(StatBlockFonts.SectionHeader, "Actions"),
            Entry("Dominate Mind.", "Wisdom Saving Throw: DC 16, one creature the aboleth can see."),
            Line(StatBlockFonts.SectionHeader, "Legendary Actions"),
            Line(StatBlockFonts.Body, "Legendary Action Uses: 3 (4 in Lair). Immediately after"),
            Line(StatBlockFonts.Body, "another creature's turn, the aboleth can expend a use to"),
            Line(StatBlockFonts.Body, "take one of the following actions. The aboleth regains all"),
            Line(StatBlockFonts.Body, "expended uses at the start of each of its turns."),
            Entry("Lash.", "The aboleth makes one Tentacle attack."),
        ]);

        var aboleth = Assert.Single(result.Monsters);
        Assert.Empty(result.Diagnostics);

        var dominateMind = aboleth.Entries.Single(entry => entry.Name == "Dominate Mind");
        Assert.DoesNotContain("Legendary Action Uses", dominateMind.Text, StringComparison.Ordinal);
        Assert.Equal(
            "Wisdom Saving Throw: DC 16, one creature the aboleth can see.",
            dominateMind.Text);

        var lash = aboleth.Entries.Single(entry => entry.Name == "Lash");
        Assert.Equal(MonsterEntrySection.LegendaryAction, lash.Section);
        Assert.Equal("The aboleth makes one Tentacle attack.", lash.Text);

        Assert.Equal(3, aboleth.LegendaryActionUses);
        Assert.Equal(4, aboleth.LegendaryActionUsesInLair);
    }

    [Fact]
    public void LegendaryActionsPreambleWithNoLairFigureLeavesUsesInLairNull()
    {
        // The Solar/Tarrasque/Unicorn shape: no lair, so no parenthetical.
        var result = MonsterParser.Parse(
        [
            Line(StatBlockFonts.Name, "Test Solar", height: 10),
            Line(StatBlockFonts.Italic, "Large Celestial, Lawful Good"),
            Line(StatBlockFonts.Stat, "AC 21"),
            Line(StatBlockFonts.Stat, "HP 297 (22d10 + 176)"),
            Line(StatBlockFonts.Stat, "Speed 50 ft., Fly 150 ft."),
            Line(StatBlockFonts.AbilityTable, "26 +8 +8 22 +6 +6 26 +8 +8"),
            Line(StatBlockFonts.AbilityTable, "25 +7 +12 25 +7 +12 30 +10 +10"),
            Line(StatBlockFonts.Stat, "CR 21 (XP 33,000; PB +7)"),
            Line(StatBlockFonts.SectionHeader, "Legendary Actions"),
            Line(StatBlockFonts.Body, "Legendary Action Uses: 3. Immediately after another creature's turn, " +
                "the solar can expend a use to take one of the following actions. The solar regains all " +
                "expended uses at the start of each of its turns."),
            Entry("Radiant Strike.", "The solar makes one Radiant Sword attack."),
        ]);

        var solar = Assert.Single(result.Monsters);

        Assert.Equal(3, solar.LegendaryActionUses);
        Assert.Null(solar.LegendaryActionUsesInLair);
    }

    [Fact]
    public void AMalformedLegendaryActionsPreambleFailsTheMonsterRatherThanBeingDropped()
    {
        // A grammar the closed, corpus-verified pattern does not recognise must be
        // surfaced, not silently discarded (#423).
        var result = MonsterParser.Parse(
        [
            Line(StatBlockFonts.Name, "Test Dragon", height: 10),
            Line(StatBlockFonts.Italic, "Huge Dragon, Chaotic Evil"),
            Line(StatBlockFonts.Stat, "AC 19"),
            Line(StatBlockFonts.Stat, "HP 200 (16d12 + 96)"),
            Line(StatBlockFonts.Stat, "Speed 40 ft., Fly 80 ft."),
            Line(StatBlockFonts.AbilityTable, "23 +6 +6 14 +2 +2 21 +5 +5"),
            Line(StatBlockFonts.AbilityTable, "14 +2 +6 13 +1 +5 19 +4 +4"),
            Line(StatBlockFonts.Stat, "CR 17 (XP 18,000; PB +6)"),
            Line(StatBlockFonts.SectionHeader, "Legendary Actions"),
            Line(StatBlockFonts.Body, "This dragon can take 3 legendary actions, choosing from below."),
            Entry("Tail Attack.", "The dragon makes one Tail attack."),
        ]);

        Assert.Empty(result.Monsters);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Subject == "Test Dragon" && diagnostic.Message.Contains("did not match", StringComparison.Ordinal));
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
