using Godot;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The combat log's colouring rules.
/// </summary>
/// <remarks>
/// <see cref="LogHighlighter"/>'s whole public surface is reachable without a Godot
/// runtime — <see cref="Color"/> is a plain managed struct — so nothing here needed a
/// seam. These are the rules its own doc comment states, each pinned so that removing
/// it turns a named test red.
/// </remarks>
public class LogHighlighterTests
{
    private static readonly Color Base = new(1, 1, 1);

    [Fact]
    public void None_ColoursNothing()
    {
        var spans = LogHighlighter.None.Spans("Sable hits the Goblin", Base);

        Assert.Equal([("Sable hits the Goblin", Base)], Painted(spans));
    }

    /// <summary>
    /// The invariant every other test here leans on: colouring never edits the line.
    /// </summary>
    [Fact]
    public void Spans_ReassembleTheLineExactly()
    {
        const string line = "Sable hits the Goblin Warrior with Scimitar — it takes 7 Slashing damage.";

        var highlighter = LogHighlighter.For(
            FightTestData.Fight(
                FightTestData.Combatant("Sable", FightTestData.Heroes),
                FightTestData.Combatant(
                    "Goblin Warrior",
                    x: 5,
                    stats: FightTestData.Stats(attacks: [FightTestData.Attack("Scimitar")]))),
            FightTestData.Heroes);

        var spans = highlighter.Spans(line, Base);

        Assert.Equal(line, string.Concat(spans.Select(span => span.Text)));
    }

    [Fact]
    public void Spans_ColourThePartyAndTheMonstersApart()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(
                FightTestData.Combatant("Sable", FightTestData.Heroes),
                FightTestData.Combatant("Goblin", x: 5)),
            FightTestData.Heroes);

        var spans = highlighter.Spans("Sable strikes Goblin", Base);

        Assert.Equal(LogHighlighter.PartyName, ColourOf(spans, "Sable"));
        Assert.Equal(LogHighlighter.MonsterName, ColourOf(spans, "Goblin"));
    }

    /// <summary>
    /// Longest first, so a shorter name that sits inside a longer one cannot break it up.
    /// </summary>
    /// <remarks>
    /// The shorter name belongs to the <em>party</em> deliberately. With both on the same
    /// side the two names paint the same colour, the split runs merge straight back
    /// together, and the test passes with the ordering reversed — which it did, until the
    /// knockout run for #190 caught it. A wolf in the party is what makes the failure
    /// visible.
    /// </remarks>
    [Fact]
    public void Spans_PaintTheLongestNameWhole()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(
                FightTestData.Combatant("Wolf", FightTestData.Heroes),
                FightTestData.Combatant("Giant Wolf Spider", x: 5)),
            FightTestData.Heroes);

        var spans = highlighter.Spans("The Giant Wolf Spider bites", Base);

        // One unbroken run, not "Giant ", "Wolf", " Spider".
        Assert.Equal(LogHighlighter.MonsterName, ColourOf(spans, "Giant Wolf Spider"));
    }

    /// <summary>Without the whole-word rule "Sable" lights up inside "Sabletooth".</summary>
    [Fact]
    public void Spans_LeaveANameSittingInsideALongerWordAlone()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(FightTestData.Combatant("Sable", FightTestData.Heroes)),
            FightTestData.Heroes);

        var spans = highlighter.Spans("A Sabletooth appears", Base);

        Assert.Equal([("A Sabletooth appears", Base)], Painted(spans));
    }

    /// <summary>
    /// The outcome outranks a name inside it: "Fire" is the creature everywhere else in
    /// the line, and the damage phrase's own colour inside "7 Fire damage".
    /// </summary>
    [Fact]
    public void Spans_LetDamageOutrankANameInsideIt()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(
                FightTestData.Combatant("Sable", FightTestData.Heroes),
                FightTestData.Combatant("Fire", x: 5)),
            FightTestData.Heroes);

        var spans = highlighter.Spans("Fire takes 7 Fire damage", Base);

        Assert.Equal(LogHighlighter.Damage, ColourOf(spans, "7 Fire damage"));

        // And the standalone name is still the creature's colour, so this is precedence
        // where they overlap rather than the damage pattern winning everywhere.
        Assert.Equal(LogHighlighter.MonsterName, Painted(spans)[0].Colour);
    }

    [Theory]
    [InlineData("Sable attacks — miss", true)]
    [InlineData("Sable misses", true)]
    [InlineData("Sable dismisses the idea", false)]
    public void Spans_ColourTheOutcomeWordAndNotOneInsideAnother(string line, bool coloured)
    {
        var spans = LogHighlighter.None.Spans(line, Base);

        Assert.Equal(coloured, Painted(spans).Any(span => span.Colour == LogHighlighter.Miss));
    }

    /// <summary>
    /// Combatants are collected before attacks, so a monster named after its own weapon
    /// stays a monster in the log.
    /// </summary>
    [Fact]
    public void For_LetsACombatantClaimItsNameBeforeAnAttackOfTheSameName()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(
                FightTestData.Combatant(
                    "Sable",
                    FightTestData.Heroes,
                    stats: FightTestData.Stats(attacks: [FightTestData.Attack("Shadow")])),
                FightTestData.Combatant("Shadow", x: 5)),
            FightTestData.Heroes);

        var spans = highlighter.Spans("Sable swings at Shadow", Base);

        Assert.Equal(LogHighlighter.MonsterName, ColourOf(spans, "Shadow"));
    }

    [Fact]
    public void For_ColoursAStatBlockEntryByName()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(
                FightTestData.Combatant(
                    "Goblin",
                    stats: FightTestData.Stats(entries: [FightTestData.Entry("Nimble Escape")]))),
            FightTestData.Heroes);

        var spans = highlighter.Spans("Goblin uses Nimble Escape", Base);

        Assert.Equal(LogHighlighter.ActionName, ColourOf(spans, "Nimble Escape"));
    }

    /// <summary>
    /// The enum name is not the printed name: the narration says "Second Wind", so the
    /// case is split back out before the term is looked for.
    /// </summary>
    [Fact]
    public void For_RecoversAFeaturesPrintedNameFromItsEnumName()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(
                FightTestData.Combatant(
                    "Sable",
                    FightTestData.Heroes,
                    stats: FightTestData.Stats(character: FightTestData.Character(ClassFeature.SecondWind)))),
            FightTestData.Heroes);

        var spans = highlighter.Spans("Sable uses Second Wind", Base);

        Assert.Equal(LogHighlighter.ActionName, ColourOf(spans, "Second Wind"));
    }

    [Fact]
    public void For_ColoursEveryWeaponMasteryWhetherOrNotAnyoneHasIt()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(FightTestData.Combatant("Goblin")),
            FightTestData.Heroes);

        foreach (var mastery in Enum.GetValues<WeaponMastery>())
        {
            var spans = highlighter.Spans($"Sable uses {mastery}", Base);

            Assert.Equal(LogHighlighter.ActionName, ColourOf(spans, mastery.ToString()));
        }
    }

    /// <summary>
    /// A two-letter term would light up half the log; short names go uncoloured rather
    /// than colouring the wrong thing.
    /// </summary>
    [Fact]
    public void For_SkipsTermsOfTwoCharactersOrFewer()
    {
        var highlighter = LogHighlighter.For(
            FightTestData.Fight(FightTestData.Combatant("Ox", FightTestData.Heroes)),
            FightTestData.Heroes);

        var spans = highlighter.Spans("Ox charges", Base);

        Assert.Equal([("Ox charges", Base)], Painted(spans));
    }

    /// <summary>The colour a term was actually drawn in, asserting it was one whole run.</summary>
    private static Color ColourOf(IReadOnlyList<LogHighlighter.Span> spans, string text)
    {
        var span = Assert.Single(spans, span => span.Text == text);
        return span.Colour;
    }

    private static IReadOnlyList<(string Text, Color Colour)> Painted(
        IReadOnlyList<LogHighlighter.Span> spans) =>
        [.. spans.Select(span => (span.Text, span.Colour))];
}
