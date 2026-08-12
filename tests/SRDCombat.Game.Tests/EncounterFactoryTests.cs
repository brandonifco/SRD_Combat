using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Building a whole fight: budget, monsters, battlefield and placement, from real content.
/// </summary>
/// <remarks>
/// The unit tests cover the published table and the spending rule against invented
/// monsters. What these add is that the real bestiary can actually satisfy a budget at
/// every difficulty this game uses — a table implemented perfectly against a pool with
/// nothing affordable in it would still produce empty fights.
/// </remarks>
public class EncounterFactoryTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Theory]
    [InlineData(EncounterDifficulty.Low)]
    [InlineData(EncounterDifficulty.Moderate)]
    [InlineData(EncounterDifficulty.High)]
    public void EveryDifficultyBuysSomethingAtEveryLevelThisGameSupports(EncounterDifficulty difficulty)
    {
        for (var level = 1; level <= 5; level++)
        {
            var party = PregeneratedParty.Build(Content, level);
            var fight = EncounterFactory.Build(Content, party, difficulty, new SeededRandomSource(level * 100));

            Assert.NotEmpty(fight.Built.Monsters);
            Assert.True(
                fight.Built.Spent <= fight.Built.Budget,
                $"Level {level} {difficulty} overspent {fight.Built.Spent} of {fight.Built.Budget}.");
        }
    }

    [Fact]
    public void TheBudgetMatchesThePrintedTableForTheDefaultParty()
    {
        // Four level 1 characters at low difficulty: the book's own first example, all
        // the way through the factory rather than just the table.
        var party = PregeneratedParty.Build(Content, level: 1);
        var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.Low, new SeededRandomSource(1));

        Assert.Equal(4, party.Count);
        Assert.Equal(200, fight.Built.Budget);
    }

    [Fact]
    public void HarderDifficultiesBuyMore()
    {
        var party = PregeneratedParty.Build(Content, level: 3);

        var low = EncounterFactory.Build(Content, party, EncounterDifficulty.Low, new SeededRandomSource(5));
        var high = EncounterFactory.Build(Content, party, EncounterDifficulty.High, new SeededRandomSource(5));

        Assert.True(high.Built.Budget > low.Built.Budget);
        Assert.True(high.Built.Spent > low.Built.Spent);
    }

    [Fact]
    public void TheSidesStartApartAndNobodyShareASquare()
    {
        var party = PregeneratedParty.Build(Content, level: 2);
        var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(9));

        var squares = fight.Encounter.Combatants.Select(combatant => combatant.Position).ToArray();
        Assert.Equal(squares.Length, squares.Distinct().Count());

        var heroes = fight.Encounter.Combatants
            .Where(combatant => combatant.SideId == PregeneratedParty.SideId)
            .ToArray();
        var monsters = fight.Encounter.Combatants
            .Where(combatant => combatant.SideId == EncounterFactory.MonsterSideId)
            .ToArray();

        Assert.NotEmpty(monsters);

        // The stated placement: far enough that closing costs a turn, so a 30-foot move
        // does not start the fight already in melee.
        var closest = heroes.Min(hero => monsters.Min(monster => hero.Position.DistanceFeetTo(monster.Position)));

        Assert.Equal(EncounterFactory.StartingSeparationFeet, closest);
    }

    [Fact]
    public void EveryoneFitsOnTheBattlefield()
    {
        var party = PregeneratedParty.Build(Content, level: 5);
        var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.High, new SeededRandomSource(11));

        Assert.All(
            fight.Encounter.Combatants,
            combatant => Assert.True(
                fight.Encounter.Battlefield.IsPassable(combatant.Position),
                $"{combatant.Name} was placed off the battlefield at {combatant.Position}."));
    }

    [Fact]
    public void ARepeatedMonsterGetsItsOwnIdentity()
    {
        // "2 Giant Wasps" is a legal encounter, and two combatants sharing an id would
        // make the engine's per-combatant state collide.
        foreach (var seed in Enumerable.Range(1, 25))
        {
            var party = PregeneratedParty.Build(Content, level: 4);
            var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.High, new SeededRandomSource(seed));

            var ids = fight.Encounter.Combatants.Select(combatant => combatant.Id).ToArray();

            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void AnEncounterOnlyContainsMonstersThePoolAdmits()
    {
        // The separation the pool's own doc states: it decides what is fit to use, the
        // budget decides how much. A creature whose turn the engine cannot execute in
        // full must never reach a built fight.
        var party = PregeneratedParty.Build(Content, level: 3);
        var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.High, new SeededRandomSource(13));

        Assert.All(fight.Built.Monsters, monster => Assert.True(MonsterPool.Admits(monster)));
    }

    [Fact]
    public void ABuiltFightRunsToAConclusion()
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.Low, new SeededRandomSource(20250812));

        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);

        Assert.True(fight.Encounter.IsComplete);
        Assert.NotNull(fight.Encounter.WinningSide);
    }
}
