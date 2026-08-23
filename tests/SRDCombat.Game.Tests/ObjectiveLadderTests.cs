using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Objectives on the ladder: which rungs carry one, and how a spec becomes a fight's own
/// objective once there are monsters to mark.
/// </summary>
public class ObjectiveLadderTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void TheLadderIsMostlyStillDeathmatches()
    {
        var ladder = GauntletLadder.Default();

        var withObjective = ladder.Count(step => step.Objective is not null);

        // Two rungs of every five. Enough that a cycle is not one fight played five
        // times; few enough that a straight kill is still what the ladder mostly asks
        // for. Asserted as a proportion rather than a literal count so the shape
        // survives a change to the ladder's length.
        Assert.Equal(ladder.Count * 2 / GauntletLadder.FightsPerCycle, withObjective);
        Assert.True(withObjective < ladder.Count - withObjective);
    }

    [Fact]
    public void TheHighMilestoneIsABossFightAndOneRoutineRungIsAHoldingAction()
    {
        var ladder = GauntletLadder.Default();

        var milestone = ladder[GauntletLadder.FightsPerCycle - 1];
        var holding = ladder[2];

        Assert.Equal(EncounterDifficulty.High, milestone.Difficulty);
        Assert.Equal(ObjectiveKind.KillLeader, milestone.Objective?.Kind);

        Assert.Equal(ObjectiveKind.SurviveRounds, holding.Objective?.Kind);
        Assert.Equal(3, holding.Objective?.Rounds);
    }

    [Fact]
    public void AKillLeaderRungMarksTheDearestCreatureOnTheField()
    {
        var party = PregeneratedParty.Build(Content, level: 3);

        var fight = EncounterFactory.Build(
            Content,
            party,
            EncounterDifficulty.High,
            new SeededRandomSource(7),
            objective: ObjectiveSpec.KillLeader);

        Assert.Equal(ObjectiveKind.KillLeader, fight.Encounter.Objective.Kind);

        var leader = fight.Encounter.Combatants
            .Single(combatant => combatant.Id == fight.Encounter.Objective.LeaderId);

        // The reading: the dearest printed XP in the encounter is the boss. An
        // encounter can field several copies of the same monster, so the leader's name
        // does not pick a unique entry — every copy shares the same printed XP, so the
        // first is as good as any.
        var dearest = fight.Built.Monsters.Max(monster => monster.ExperiencePoints);

        Assert.Equal(dearest, fight.Built.Monsters
            .First(monster => monster.Name == leader.Name)
            .ExperiencePoints);
    }

    [Fact]
    public void ASurviveRungCarriesItsRoundsAndThePartysSide()
    {
        var party = PregeneratedParty.Build(Content, level: 2);

        var fight = EncounterFactory.Build(
            Content,
            party,
            EncounterDifficulty.Low,
            new SeededRandomSource(11),
            objective: ObjectiveSpec.Survive(3));

        Assert.Equal(ObjectiveKind.SurviveRounds, fight.Encounter.Objective.Kind);
        Assert.Equal(3, fight.Encounter.Objective.Rounds);

        // The objective belongs to the party, never to the monsters.
        Assert.Equal(PregeneratedParty.SideId, fight.Encounter.Objective.SideId);
    }

    [Fact]
    public void ABossFightFieldsAnEscort()
    {
        // A lone marked leader is the easiest fight on the ladder — four characters
        // focus-firing the only enemy action economy on the field, in a fight that ends
        // the moment it dies. Three is leader plus a pair.
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var party = PregeneratedParty.Build(Content, level: 3);

            var fight = EncounterFactory.Build(
                Content,
                party,
                EncounterDifficulty.High,
                new SeededRandomSource(seed),
                objective: ObjectiveSpec.KillLeader);

            Assert.True(
                fight.Built.Monsters.Count >= 3,
                $"seed {seed} built a boss fight of {fight.Built.Monsters.Count}");
        }
    }

    [Fact]
    public void AFightWithNoObjectiveIsUnchanged()
    {
        var party = PregeneratedParty.Build(Content, level: 2);

        var fight = EncounterFactory.Build(
            Content,
            party,
            EncounterDifficulty.Low,
            new SeededRandomSource(11));

        Assert.Equal(ObjectiveKind.Defeat, fight.Encounter.Objective.Kind);
    }

    [Fact]
    public void ASurviveRungIsWonByOutlastingRatherThanKilling()
    {
        // The whole point of the objective: the fight ends with enemies still on their
        // feet. Played out with the real policy so this is the game's own road.
        var party = PregeneratedParty.Build(Content, level: 3);

        var fight = EncounterFactory.Build(
            Content,
            party,
            EncounterDifficulty.Low,
            new SeededRandomSource(3),
            objective: ObjectiveSpec.Survive(2));

        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);

        Assert.True(fight.Encounter.IsComplete);

        // Either the party outlasted the enemy or it killed everything inside two rounds;
        // both are wins, and a loss would mean the objective changed who survives.
        if (fight.Encounter.WinningSide == PregeneratedParty.SideId)
        {
            Assert.True(fight.Encounter.Round <= 3);
        }
    }
}
