using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The objectives that end a fight in something other than a last-creature-standing.
/// </summary>
/// <remarks>
/// The boundaries worth pinning are the ones where an objective could quietly become a
/// win button: a side that has been wiped out must not collect an objective, and the
/// default must stay exactly what it always was.
/// </remarks>
public class EncounterObjectiveTests
{
    [Fact]
    public void WithoutAnObjective_TheFightIsStillLastSideStanding()
    {
        var encounter = Encounter.Start(Field(), Two(), new SeededSequence(10, 5));

        Assert.Equal(ObjectiveKind.Defeat, encounter.Objective.Kind);
        Assert.False(encounter.IsComplete);

        Kill(encounter, "monster");

        Assert.True(encounter.IsComplete);
        Assert.Equal(CombatTestData.Heroes, encounter.WinningSide);
    }

    [Fact]
    public void SurviveRounds_EndsTheFightWhenTheRoundsArePlayedOut()
    {
        var encounter = Encounter.Start(
            Field(),
            Two(),
            new SeededSequence(10, 5),
            EncounterObjective.SurviveRounds(CombatTestData.Heroes, 2));

        // Round 1 and round 2 must both happen; the third must not begin.
        Assert.Equal(1, encounter.Round);

        EndRound(encounter);
        Assert.Equal(2, encounter.Round);
        Assert.False(encounter.IsComplete);

        EndRound(encounter);

        Assert.True(encounter.IsComplete);
        Assert.Equal(CombatTestData.Heroes, encounter.WinningSide);
    }

    [Fact]
    public void SurviveRounds_DoesNotSaveASideThatHasBeenWipedOut()
    {
        // The objective belongs to the heroes, and the monsters drop them before the clock
        // runs out. Being wiped loses whatever the objective says, and the clock running
        // out afterwards must not hand the fight back: a side with nobody standing cannot
        // meet an objective. The rounds are walked out rather than the state poked, so
        // this exercises the same road a real fight takes.
        var encounter = Encounter.Start(
            Field(),
            Two(),
            new SeededSequence(10, 5),
            EncounterObjective.SurviveRounds(CombatTestData.Heroes, 2));

        DamageRules.Apply(
            encounter.Combatants.Single(combatant => combatant.Id == "hero"),
            1_000,
            DamageType.Slashing);

        for (var round = 0; round < 4 && !encounter.IsComplete; round++)
        {
            EndRound(encounter);
        }

        Assert.True(encounter.IsComplete);
        Assert.Equal(CombatTestData.Monsters, encounter.WinningSide);
    }

    [Fact]
    public void KillLeader_EndsTheFightWhileTheRestStillStand()
    {
        var hero = CombatTestData.Combatant("hero", stats: CombatTestData.Stats(initiativeBonus: 10));
        var leader = CombatTestData.Combatant("monster0", sideId: CombatTestData.Monsters, x: 5);
        var mook = CombatTestData.Combatant("monster1", sideId: CombatTestData.Monsters, x: 6);

        var encounter = Encounter.Start(
            Field(),
            [hero, leader, mook],
            new SeededSequence(10, 5, 5),
            EncounterObjective.KillLeader(CombatTestData.Heroes, "monster0"));

        Kill(encounter, "monster0");

        Assert.True(encounter.IsComplete);
        Assert.Equal(CombatTestData.Heroes, encounter.WinningSide);

        // The point of the objective: the survivor is still alive and the fight is over.
        Assert.True(encounter.Combatants.Single(combatant => combatant.Id == "monster1").IsActive);
    }

    [Fact]
    public void KillLeader_KillingAnybodyElseDoesNotEndIt()
    {
        var hero = CombatTestData.Combatant("hero", stats: CombatTestData.Stats(initiativeBonus: 10));
        var leader = CombatTestData.Combatant("monster0", sideId: CombatTestData.Monsters, x: 5);
        var mook = CombatTestData.Combatant("monster1", sideId: CombatTestData.Monsters, x: 6);

        var encounter = Encounter.Start(
            Field(),
            [hero, leader, mook],
            new SeededSequence(10, 5, 5),
            EncounterObjective.KillLeader(CombatTestData.Heroes, "monster0"));

        Kill(encounter, "monster1");

        Assert.False(encounter.IsComplete);
    }

    [Fact]
    public void KillLeader_MakesTheLeaderTheSharedTarget()
    {
        // The doctrine would otherwise pick on threat per hit point, and the mook here is
        // the flimsier creature — so without the objective outranking the arithmetic the
        // party would win this fight only by accident.
        var hero = CombatTestData.Combatant("hero", stats: CombatTestData.Stats(initiativeBonus: 10));
        var leader = CombatTestData.Combatant(
            "monster0",
            sideId: CombatTestData.Monsters,
            x: 5,
            stats: CombatTestData.Stats(maximumHitPoints: 60));
        var mook = CombatTestData.Combatant(
            "monster1",
            sideId: CombatTestData.Monsters,
            x: 6,
            stats: CombatTestData.Stats(maximumHitPoints: 4));

        var plain = Encounter.Start(Field(), [hero, leader, mook], new SeededSequence(10, 5, 5));
        var marked = Encounter.Start(
            Field(),
            [hero, leader, mook],
            new SeededSequence(10, 5, 5),
            EncounterObjective.KillLeader(CombatTestData.Heroes, "monster0"));

        var actor = plain.Combatants.Single(combatant => combatant.Id == "hero");

        Assert.Equal("monster1", PartyDoctrine.FocusTarget(plain, actor)?.Id);
        Assert.Equal(
            "monster0",
            PartyDoctrine.FocusTarget(marked, marked.Combatants.Single(c => c.Id == "hero"))?.Id);
    }

    [Fact]
    public void AnObjectiveDescribesItselfForAClient()
    {
        Assert.Equal("Survive 3 rounds.", EncounterObjective.SurviveRounds("heroes", 3).Describe());
        Assert.Equal("Survive 1 round.", EncounterObjective.SurviveRounds("heroes", 1).Describe());
        Assert.Equal("Defeat every enemy.", EncounterObjective.Defeat.Describe());
        Assert.Equal(
            "Kill Bandit Captain — the rest will break off.",
            EncounterObjective.KillLeader("heroes", "monster0").Describe("Bandit Captain"));
    }

    [Fact]
    public void TheEncounterNamesTheLeaderForTheClients()
    {
        // Both clients print this one string, so the id-to-name lookup lives on the
        // encounter rather than in each of them.
        var hero = CombatTestData.Combatant("hero", stats: CombatTestData.Stats(initiativeBonus: 10));
        var leader = CombatTestData.Combatant("monster0", sideId: CombatTestData.Monsters, x: 5);

        var encounter = Encounter.Start(
            Field(),
            [hero, leader],
            new SeededSequence(10, 5),
            EncounterObjective.KillLeader(CombatTestData.Heroes, "monster0"));

        Assert.Contains(leader.Name, encounter.ObjectiveDescription, StringComparison.Ordinal);

        var plain = Encounter.Start(Field(), Two(), new SeededSequence(10, 5));

        Assert.Equal("Defeat every enemy.", plain.ObjectiveDescription);
    }

    private static Battlefield Field() => new(12, 12);

    private static Combatant[] Two() =>
    [
        CombatTestData.Combatant("hero", stats: CombatTestData.Stats(initiativeBonus: 10)),
        CombatTestData.Combatant("monster", sideId: CombatTestData.Monsters, x: 5),
    ];

    /// <summary>Walks the turn order round once, so a round boundary is crossed.</summary>
    private static void EndRound(Encounter encounter)
    {
        var round = encounter.Round;

        while (encounter.Round == round && !encounter.IsComplete)
        {
            encounter.EndTurn();
        }
    }

    /// <summary>
    /// Drops a combatant and then asks the encounter to notice, which is what an action
    /// would have done — damage applied outside a turn has no other road to the check.
    /// </summary>
    private static void Kill(Encounter encounter, string id)
    {
        DamageRules.Apply(
            encounter.Combatants.Single(combatant => combatant.Id == id),
            1_000,
            DamageType.Slashing);

        encounter.EndTurn();
    }
}
