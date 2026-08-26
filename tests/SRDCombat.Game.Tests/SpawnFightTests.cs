using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="EncounterFactory.BuildChosen"/> (#456): a hand-picked cast stands on
/// exactly the board a budgeted one would — placed, terrained, and honestly priced.
/// </summary>
public class SpawnFightTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    private static MonsterDefinition Named(string name) =>
        Content.Monsters.Single(monster => monster.Name == name);

    [Fact]
    public void FieldsExactlyTheChosenCast()
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var roster = new[] { Named("Ogre"), Named("Goblin Warrior"), Named("Goblin Warrior") };

        var fight = EncounterFactory.BuildChosen(party, roster, new SeededRandomSource(42));

        Assert.Equal(
            ["Ogre", "Goblin Warrior", "Goblin Warrior"],
            fight.Built.Monsters.Select(monster => monster.Name).ToArray());

        var monsters = fight.Encounter.Combatants
            .Where(combatant => combatant.SideId == EncounterFactory.MonsterSideId)
            .ToArray();

        Assert.Equal(3, monsters.Length);
        Assert.Equal(3, monsters.Select(combatant => combatant.Id).Distinct().Count());
        Assert.Equal(party.Count + 3, fight.Encounter.Combatants.Count);
    }

    [Fact]
    public void PricesTheCastAtItsPrintedExperienceWithNoPretendedHeadroom()
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var roster = new[] { Named("Ogre"), Named("Wolf") };

        var fight = EncounterFactory.BuildChosen(party, roster, new SeededRandomSource(7));

        var printed = roster.Sum(monster => monster.ExperiencePoints);
        Assert.Equal(printed, fight.Built.Budget);
        Assert.Equal(printed, fight.Built.Spent);
        Assert.Equal(0, fight.Built.Remaining);
    }

    [Fact]
    public void EveryBodyStandsOnTheBoard()
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var roster = Enumerable.Repeat(Named("Wolf"), 10).ToArray();

        var fight = EncounterFactory.BuildChosen(party, roster, new SeededRandomSource(3));
        var battlefield = fight.Encounter.Battlefield;

        Assert.All(fight.Encounter.Combatants, combatant =>
        {
            Assert.InRange(combatant.Position.X, 0, battlefield.Width - 1);
            Assert.InRange(combatant.Position.Y, 0, battlefield.Height - 1);
        });

        Assert.Equal(
            fight.Encounter.Combatants.Count,
            fight.Encounter.Combatants.Select(combatant => combatant.Position).Distinct().Count());
    }

    [Fact]
    public void ASingleMonsterAndALargeOneBothPlace()
    {
        var party = PregeneratedParty.Build(Content, level: 1);

        // Ogre is printed Large — under the interim single-square reading it still
        // spans one square (#429's dark scaffold), so this pins the call not crashing
        // and stays valid when the final slice turns real footprints on.
        var fight = EncounterFactory.BuildChosen(party, [Named("Ogre")], new SeededRandomSource(11));

        Assert.Single(fight.Built.Monsters);
        Assert.False(fight.Encounter.IsComplete);
    }
}
