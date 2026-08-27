using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="ScenarioRunner"/> (#474): the one entry point from an authored scenario to
/// a fight, and the proof that it overrides draws rather than bypassing generation.
/// </summary>
public class ScenarioRunnerTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    /// <summary>
    /// Everything about a fight that a second code path could get wrong: the cast in
    /// order, the opening shape, the board square for square, where every body stood, the
    /// initiative order, and then the whole fight played out narration by narration.
    /// </summary>
    /// <remarks>
    /// <b>Played to completion on purpose.</b> A board and an initiative order that match
    /// prove the generation stream was not re-timed up to <c>Encounter.Start</c>; only the
    /// narration proves the dice underneath the fight itself are the same stream too,
    /// which is the property every batch number taken from a scenario rests on.
    /// </remarks>
    private static string Fingerprint(Fight fight)
    {
        var battlefield = fight.Encounter.Battlefield;

        var lines = new List<string>
        {
            $"cast: {string.Join(", ", fight.Built.Monsters.Select(monster => monster.Name))}",
            $"priced: {fight.Built.Budget}/{fight.Built.Spent}",
            $"layout: {fight.Layout}",
            $"board: {battlefield.Width}x{battlefield.Height}",
            $"blocked: {Squares(battlefield.Blocked)}",
            $"difficult: {Squares(battlefield.DifficultTerrain)}",
            $"obstacles: {Squares(battlefield.LowObstacles)}",
            $"objective: {fight.Encounter.Objective.Kind}/{fight.Encounter.Objective.LeaderId}",
            $"order: {string.Join(", ", fight.Encounter.TurnOrder.Select(c => $"{c.Id}={c.Name}@{c.Position}"))}",
        };

        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
        lines.AddRange(fight.Encounter.Log.Select(step => $"{step.Kind}: {step.Narration}"));

        return string.Join("\n", lines);
    }

    private static string Squares(IReadOnlyCollection<GridPosition> squares) =>
        string.Join(" ", squares.OrderBy(s => s.X).ThenBy(s => s.Y).Select(s => $"{s.X},{s.Y}"));

    private static BattleScenario Scenario(ScenarioParty party, ScenarioEnemies enemies, string name = "test") =>
        new()
        {
            FormatVersion = ScenarioFile.CurrentFormatVersion,
            Name = name,
            Notes = string.Empty,
            Party = party,
            Enemies = enemies,
        };

    private static MonsterDefinition Named(string name) =>
        Content.Monsters.Single(monster => monster.Name == name);

    /// <summary>
    /// Criterion 1. The same <c>(scenario, seed)</c> is the same fight — not merely the
    /// same cast, but the same board, the same initiative and the same narration all the
    /// way to the end.
    /// </summary>
    [Theory]
    [InlineData(17)]
    [InlineData(20250812)]
    public void TheSameScenarioAndSeedProduceTheSameFight(int seed)
    {
        var scenario = Scenario(
            new ScenarioParty { PregeneratedLevel = 3 },
            new ScenarioEnemies
            {
                Budget = new ScenarioBudget { Difficulty = EncounterDifficulty.Moderate, Level = 3 },
            });

        Assert.Equal(
            Fingerprint(ScenarioRunner.Build(Content, scenario, seed)),
            Fingerprint(ScenarioRunner.Build(Content, scenario, seed)));
    }

    /// <summary>
    /// <b>Criterion 2 — the proof obligation this slice exists to meet.</b> A budgeted
    /// scenario naming the difficulty, level and CR cap the ladder would have used builds
    /// the identical fight <see cref="EncounterFactory.Build"/> builds at that seed. If
    /// this ever fails, a draw was skipped or re-timed and every number taken from a
    /// scenario is about a different game — it is a bug, not a tolerance to widen.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1234)]
    public void ABudgetedScenarioBuildsTheSameFightTheLadderWouldHave(int seed)
    {
        var scenario = Scenario(
            new ScenarioParty { PregeneratedLevel = 3 },
            new ScenarioEnemies
            {
                Budget = new ScenarioBudget
                {
                    Difficulty = EncounterDifficulty.Moderate,
                    Level = 3,
                    MaximumChallengeRating = 4m,
                },
            });

        var drawn = EncounterFactory.Build(
            Content,
            PregeneratedParty.Build(Content, level: 3),
            EncounterDifficulty.Moderate,
            new SeededRandomSource(seed));

        Assert.Equal(Fingerprint(drawn), Fingerprint(ScenarioRunner.Build(Content, scenario, seed)));
    }

    /// <summary>
    /// <b>The trap S1's architect left a warning about.</b>
    /// <see cref="ScenarioBudget.Level"/> is the level the printed budget prices against
    /// and is not the party's: "a Moderate fight priced for a level 3 party, fought by a
    /// level 1 party" is exactly what the field exists to author. A runner that read the
    /// party instead would compile, pass every other test here, and silently measure a
    /// different fight than the one written down — so this pins the budget the fight was
    /// actually priced at, which nothing else would catch.
    /// </summary>
    [Fact]
    public void TheBudgetsLevelPricesTheFightAndThePartysDoesNot()
    {
        var scenario = Scenario(
            new ScenarioParty { PregeneratedLevel = 1 },
            new ScenarioEnemies
            {
                Budget = new ScenarioBudget { Difficulty = EncounterDifficulty.Moderate, Level = 5 },
            });

        var fight = ScenarioRunner.Build(Content, scenario, seed: 9);

        // The party really is the level 1 one, so the two numbers genuinely disagree.
        Assert.All(fight.Party, member => Assert.Equal(1, member.Sheet.Level));

        var authored = EncounterBudget.ForLevels(
            Enumerable.Repeat(5, fight.Party.Count), EncounterDifficulty.Moderate);
        var partys = EncounterBudget.ForLevels(
            Enumerable.Repeat(1, fight.Party.Count), EncounterDifficulty.Moderate);

        Assert.NotEqual(authored, partys);
        Assert.Equal(authored, fight.Built.Budget);
    }

    /// <summary>
    /// The other half of the same line: the authored level reaches the budgeting step and
    /// stops there. The <see cref="BattleLayout"/> draw's level gate is about the party
    /// that has to survive being flanked, not about a price, so a level 1 party priced at
    /// level 5 still opens in columns — bypassing that gate is the battlefield override
    /// block's job (S6, #478), where a batch report can label it, and must not fall out of
    /// the budget level unlabelled.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    [InlineData(77)]
    public void AnAuthoredBudgetLevelDoesNotUnlockTheLayoutDrawForALowLevelParty(int seed)
    {
        var scenario = Scenario(
            new ScenarioParty { PregeneratedLevel = 1 },
            new ScenarioEnemies
            {
                Budget = new ScenarioBudget { Difficulty = EncounterDifficulty.Moderate, Level = 5 },
            });

        Assert.Equal(BattleLayout.Columns, ScenarioRunner.Build(Content, scenario, seed).Layout);
    }

    /// <summary>
    /// Criterion 3. An explicit-roster scenario reproduces today's <c>--spawn</c> fight
    /// exactly, for the same roster, level and seed — including the whole narration, so
    /// the dice stream is the same one and not merely a matching opening.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(4242)]
    public void AnExplicitRosterScenarioReproducesTheSpawnedFight(int seed)
    {
        var parsed = RosterParser.Parse("Ogre, 2 Goblin Warrior", Content.Monsters);
        Assert.Empty(parsed.Errors);

        var spawned = EncounterFactory.BuildChosen(
            PregeneratedParty.Build(Content, level: 3), parsed.Monsters, new SeededRandomSource(seed));

        var scenario = Scenario(
            new ScenarioParty { PregeneratedLevel = 3 },
            new ScenarioEnemies { Roster = RosterParser.ToRoster(parsed.Monsters) });

        Assert.Equal(Fingerprint(spawned), Fingerprint(ScenarioRunner.Build(Content, scenario, seed)));
    }

    /// <summary>
    /// Criterion 4. <see cref="EncounterFactory.BuildChosen"/> grew an objective
    /// parameter; omitting it is byte-for-byte the fight it built before, which is what
    /// keeps <c>--spawn</c> unchanged.
    /// </summary>
    [Fact]
    public void BuildChosenWithoutAnObjectiveBuildsExactlyWhatItAlwaysDid()
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var roster = new[] { Named("Ogre"), Named("Wolf") };

        var withoutArgument = EncounterFactory.BuildChosen(party, roster, new SeededRandomSource(8));
        var withExplicitNull = EncounterFactory.BuildChosen(
            PregeneratedParty.Build(Content, level: 3), roster, new SeededRandomSource(8), objective: null);

        Assert.Equal(ObjectiveKind.Defeat, withoutArgument.Encounter.Objective.Kind);
        Assert.Equal(Fingerprint(withoutArgument), Fingerprint(withExplicitNull));
    }

    /// <summary>
    /// And the parameter is honoured when a scenario names one: a chosen cast can be a
    /// boss fight now, resolved through the same <c>Resolve</c> the budgeted path uses —
    /// the dearest monster by printed XP, which here is the Ogre over the Wolf.
    /// </summary>
    [Fact]
    public void AnExplicitRosterScenarioHonoursItsObjective()
    {
        var scenario = Scenario(
            new ScenarioParty { PregeneratedLevel = 3 },
            new ScenarioEnemies
            {
                Roster =
                [
                    new ScenarioRosterEntry { MonsterId = Named("Wolf").Id, Count = 2 },
                    new ScenarioRosterEntry { MonsterId = Named("Ogre").Id, Count = 1 },
                ],
            }) with
        {
            Objective = new ObjectiveSpec(ObjectiveKind.KillLeader),
        };

        var fight = ScenarioRunner.Build(Content, scenario, seed: 13);

        Assert.Equal(ObjectiveKind.KillLeader, fight.Encounter.Objective.Kind);

        // Index 2 is the Ogre, which is both the dearest by printed XP and the last named
        // — so this fails if the roster were grouped rather than walked in order.
        Assert.Equal("monster2", fight.Encounter.Objective.LeaderId);
    }

    /// <summary>
    /// A scenario's cast is expanded in the order it was authored, one combatant per head.
    /// Order is the fight: it decides which creature takes which spawn square and which
    /// index its combatant id carries.
    /// </summary>
    [Fact]
    public void ACastIsExpandedInTheOrderItWasAuthored()
    {
        var scenario = Scenario(
            new ScenarioParty { PregeneratedLevel = 3 },
            new ScenarioEnemies
            {
                Roster =
                [
                    new ScenarioRosterEntry { MonsterId = Named("Goblin Warrior").Id, Count = 1 },
                    new ScenarioRosterEntry { MonsterId = Named("Ogre").Id, Count = 2 },
                    new ScenarioRosterEntry { MonsterId = Named("Goblin Warrior").Id, Count = 1 },
                ],
            });

        Assert.Equal(
            ["Goblin Warrior", "Ogre", "Ogre", "Goblin Warrior"],
            ScenarioRunner.Build(Content, scenario, seed: 2)
                .Built.Monsters.Select(monster => monster.Name).ToArray());
    }

    /// <summary>
    /// The pool axes are the scenario's to move, and moving them changes which bag the
    /// draw comes out of. The committed <c>moderate-level-3-pool-cuts-lifted</c> scenario
    /// is the instrument this makes possible (#312); this asserts the axes actually reach
    /// <c>MonsterPool.Draw</c> rather than being carried and dropped.
    /// </summary>
    [Fact]
    public void ThePoolAxesAScenarioNamesReachTheDraw()
    {
        var shipped = MonsterPool.Draw(Content.Monsters, 4m);
        var lifted = MonsterPool.Draw(
            Content.Monsters, 4m, MonsterCoverage.Diminished,
            plausibleFoesOnly: false, traditionalFoesOnly: false);

        Assert.True(lifted.Count > shipped.Count, "the lifted bag must be the larger one for this test to mean anything");

        var admitted = new HashSet<string>(lifted.Select(monster => monster.Name));
        admitted.ExceptWith(shipped.Select(monster => monster.Name));

        // Enough seeds that a fight drawn only from the shipped bag is not merely unlucky.
        var drawn = Enumerable.Range(1, 60)
            .SelectMany(seed => ScenarioRunner.Build(
                Content,
                Scenario(
                    new ScenarioParty { PregeneratedLevel = 3 },
                    new ScenarioEnemies
                    {
                        Budget = new ScenarioBudget
                        {
                            Difficulty = EncounterDifficulty.Moderate,
                            Level = 3,
                            CoverageFloor = MonsterCoverage.Diminished,
                            PlausibleFoesOnly = false,
                            TraditionalFoesOnly = false,
                        },
                    }),
                seed).Built.Monsters.Select(monster => monster.Name))
            .ToHashSet();

        Assert.True(drawn.Overlaps(admitted), "no creature the lifted cuts admit was ever drawn");
    }

    /// <summary>
    /// Criterion 6's voice. A scenario naming content this build does not have is refused
    /// by name and says which scenario asked, rather than failing somewhere downstream
    /// with a message about a dictionary.
    /// </summary>
    [Fact]
    public void AScenarioNamingAMissingMonsterIsRefusedByName()
    {
        var scenario = Scenario(
            new ScenarioParty { PregeneratedLevel = 3 },
            new ScenarioEnemies
            {
                Roster = [new ScenarioRosterEntry { MonsterId = "monster.hippogriff-of-doom", Count = 1 }],
            },
            name: "the-broken-one");

        var failure = Assert.Throws<InvalidDataException>(
            () => ScenarioRunner.Build(Content, scenario, seed: 1));

        Assert.Contains("monster.hippogriff-of-doom", failure.Message, StringComparison.Ordinal);
        Assert.Contains("the-broken-one", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scenario naming neither a roster nor a budget cannot come off disk —
    /// <see cref="ScenarioFile.FromJson"/> refuses it — so reaching the runner means it
    /// was built in memory, and the refusal says exactly that rather than throwing a null
    /// reference three frames down.
    /// </summary>
    [Fact]
    public void AScenarioThatNamesNeitherEnemiesNorABudgetIsRefused()
    {
        var scenario = Scenario(new ScenarioParty { PregeneratedLevel = 3 }, new ScenarioEnemies());

        var failure = Assert.Throws<InvalidDataException>(
            () => ScenarioRunner.Build(Content, scenario, seed: 1));

        Assert.Contains("built in memory", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every scenario in the committed library builds a fight and plays it out. The
    /// library test next door proves each one loads and resolves; this proves the runner
    /// can actually field it, which is the thing a batch or a by-hand run will ask for.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScenarioLibraryTests.Files), MemberType = typeof(ScenarioLibraryTests))]
    public void EveryCommittedScenarioBuildsAndPlaysOut(string file)
    {
        var json = File.ReadAllText(Path.Combine(RepositoryPaths.ScenarioDirectory, file));
        var scenario = ScenarioFile.FromJson(json).Scenario!;

        var fight = ScenarioRunner.Build(Content, scenario, seed: 31);

        Assert.NotEmpty(fight.Built.Monsters);
        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
        Assert.True(fight.Encounter.IsComplete, $"{file} did not resolve inside the round limit");
    }
}
