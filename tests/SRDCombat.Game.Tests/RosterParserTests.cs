using SRDCombat.Content;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The <c>--spawn</c> roster grammar (#456): counts, case, and — most of all — that
/// nothing unparseable is ever silently dropped.
/// </summary>
public class RosterParserTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void ParsesCountsNamesAndCaseInAskedOrder()
    {
        var roster = RosterParser.Parse("ogre, 2 goblin warrior ,Wolf", Content.Monsters);

        Assert.Empty(roster.Errors);
        Assert.Equal(
            ["Ogre", "Goblin Warrior", "Goblin Warrior", "Wolf"],
            roster.Monsters.Select(monster => monster.Name).ToArray());
    }

    [Fact]
    public void EveryUnknownNameIsReportedAndNothingIsDropped()
    {
        var roster = RosterParser.Parse("Ogre, Gobblin Worrier, 3 Made-Up Beast", Content.Monsters);

        Assert.Equal(2, roster.Errors.Count);
        Assert.Contains(roster.Errors, error => error.Contains("Gobblin Worrier"));
        Assert.Contains(roster.Errors, error => error.Contains("Made-Up Beast"));

        // The parse still reports what it did understand, so the refusal can name
        // exactly what failed rather than discarding the whole ask.
        Assert.Equal(["Ogre"], roster.Monsters.Select(monster => monster.Name).ToArray());
    }

    [Theory]
    [InlineData("0 Wolf")]
    [InlineData("21 Wolf")]
    [InlineData("-3 Wolf")]
    public void CountsOutsideTheCapAreRefused(string entry)
    {
        var roster = RosterParser.Parse(entry, Content.Monsters);

        Assert.Empty(roster.Monsters);
        Assert.Single(roster.Errors);
        Assert.Contains($"1–{RosterParser.MaximumCount}", roster.Errors[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public void AnEmptyRosterIsAnErrorNotAnEmptyFight(string text)
    {
        var roster = RosterParser.Parse(text, Content.Monsters);

        Assert.Empty(roster.Monsters);
        Assert.Single(roster.Errors);
    }

    /// <summary>
    /// <see cref="RosterParser.ToRoster"/> (#474) is exactly reversible: expanding the
    /// entries it returns reproduces the cast it was given, in order. That is the whole
    /// contract — the cast's order decides which creature takes which spawn square and
    /// which index its combatant id carries, so a conversion that grouped by name would
    /// quietly rearrange the fight a <c>--spawn</c> line asked for.
    /// </summary>
    [Theory]
    [InlineData("Ogre, 2 Goblin Warrior, Wolf")]
    [InlineData("Goblin Warrior, Ogre, Goblin Warrior")]
    [InlineData("Wolf")]
    [InlineData("20 Wolf, 20 Wolf")]
    public void ACastSurvivesTheRoundTripThroughScenarioEntries(string text)
    {
        var cast = RosterParser.Parse(text, Content.Monsters).Monsters;

        var expanded = RosterParser.ToRoster(cast)
            .SelectMany(entry => Enumerable.Repeat(entry.MonsterId, entry.Count))
            .ToArray();

        Assert.Equal(cast.Select(monster => monster.Id).ToArray(), expanded);
    }

    /// <summary>
    /// Adjacent heads fold into one entry, and a run longer than the per-entry ceiling is
    /// split rather than clamped — forty wolves are two entries of twenty, never twenty
    /// wolves and a silently lost twenty.
    /// </summary>
    [Fact]
    public void RunsFoldToTheCeilingAndThenStartAgain()
    {
        var cast = RosterParser.Parse("20 Wolf, 20 Wolf", Content.Monsters).Monsters;

        Assert.Equal(
            [RosterParser.MaximumCount, RosterParser.MaximumCount],
            RosterParser.ToRoster(cast).Select(entry => entry.Count).ToArray());
    }

    /// <summary>Only adjacent equal ids fold; a repeat after another creature is its own entry.</summary>
    [Fact]
    public void ASeparatedRepeatIsItsOwnEntry()
    {
        var cast = RosterParser.Parse("Goblin Warrior, Ogre, Goblin Warrior", Content.Monsters).Monsters;

        Assert.Equal(3, RosterParser.ToRoster(cast).Count);
    }
}
