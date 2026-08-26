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
}
