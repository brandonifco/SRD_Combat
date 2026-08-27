using System.Reflection;
using System.Text.Json;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The scenario format: strict machine JSON, refused rather than repaired, with every
/// data field able to make the round trip.
/// </summary>
/// <remarks>
/// <para>
/// These pin the on-disk shape the way <c>RunSaveTests</c> pins a save's and
/// <c>ContentSerializerTests</c> pins content's. Per Brandon's answer on 2026-08-26 —
/// a scenario file is the authoring surface's artifact and he will never hand-edit one —
/// this file <b>is</b> the format's documentation. There is no grammar to write down.
/// </para>
/// <para>
/// <b>Nothing here loads content.</b> A file that is structurally wrong should say so
/// without a 330-monster corpus being parsed first, which is exactly the split
/// <see cref="ScenarioFile"/> and <see cref="ScenarioContent"/> are in.
/// </para>
/// </remarks>
public class BattleScenarioShapeTests
{
    /// <summary>
    /// A draft with every id-bearing field filled, so the round trip has something to
    /// lose. Hand-authored rather than borrowed from <see cref="PregeneratedParty"/>
    /// because that would load content this class deliberately does not need.
    /// </summary>
    private static CharacterDraft Draft { get; } = new()
    {
        Name = "Vesh",
        SpeciesId = "species.dwarf",
        ClassId = "class.fighter",
        BackgroundId = "background.soldier",
        Level = 3,
        BaseAbilityScores = new Dictionary<Ability, int>
        {
            [Ability.Strength] = 15,
            [Ability.Dexterity] = 13,
            [Ability.Constitution] = 14,
            [Ability.Intelligence] = 10,
            [Ability.Wisdom] = 12,
            [Ability.Charisma] = 8,
        },
        WeaponIds = ["weapon.longsword"],
        ArmorId = "armor.chain-mail",
        HasShield = true,
    };

    /// <summary>Explicit party, explicit cast, an objective, and both provenance fields.</summary>
    private static BattleScenario Explicit { get; } = new()
    {
        FormatVersion = ScenarioFile.CurrentFormatVersion,
        Name = "vesh-against-two-ogres",
        Notes = "What one level 3 Fighter can do about a pair of Ogres.",
        Party = new ScenarioParty { Members = [new ScenarioMember { Draft = Draft, Level = 4 }] },
        Enemies = new ScenarioEnemies
        {
            Roster = [new ScenarioRosterEntry { MonsterId = "monster.ogre", Count = 2 }],
        },
        Objective = ObjectiveSpec.Survive(3),
        ContentVersion = "0123456789ABCDEF",
        Seed = 17,
    };

    /// <summary>The pregenerated preset, and a budgeted draw with every pool axis moved.</summary>
    private static BattleScenario Budgeted { get; } = new()
    {
        FormatVersion = ScenarioFile.CurrentFormatVersion,
        Name = "moderate-with-the-cuts-lifted",
        Notes = "The pool's plausibility and genre cuts lifted, for #312.",
        Party = new ScenarioParty { PregeneratedLevel = 3 },
        Enemies = new ScenarioEnemies
        {
            Budget = new ScenarioBudget
            {
                Difficulty = EncounterDifficulty.High,
                Level = 5,
                MaximumChallengeRating = 2m,
                Horde = true,
                CoverageFloor = MonsterCoverage.Diminished,
                PlausibleFoesOnly = false,
                TraditionalFoesOnly = false,
            },
        },
    };

    [Fact]
    public void EveryFieldOfAnExplicitScenarioSurvivesTheRoundTrip()
    {
        var read = Reread(Explicit);

        Assert.Equal(ScenarioFile.CurrentFormatVersion, read.FormatVersion);
        Assert.Equal("vesh-against-two-ogres", read.Name);
        Assert.Equal("What one level 3 Fighter can do about a pair of Ogres.", read.Notes);
        Assert.Equal("0123456789ABCDEF", read.ContentVersion);
        Assert.Equal(17, read.Seed);

        Assert.Null(read.Party.PregeneratedLevel);
        var member = Assert.Single(read.Party.Members!);
        Assert.Equal(4, member.Level);
        Assert.Equal("Vesh", member.Draft.Name);
        Assert.Equal("species.dwarf", member.Draft.SpeciesId);
        Assert.Equal("armor.chain-mail", member.Draft.ArmorId);
        Assert.True(member.Draft.HasShield);
        Assert.Equal(["weapon.longsword"], member.Draft.WeaponIds);

        Assert.Null(read.Enemies.Budget);
        var entry = Assert.Single(read.Enemies.Roster!);
        Assert.Equal("monster.ogre", entry.MonsterId);
        Assert.Equal(2, entry.Count);

        Assert.Equal(ObjectiveKind.SurviveRounds, read.Objective!.Kind);
        Assert.Equal(3, read.Objective.Rounds);
    }

    [Fact]
    public void EveryFieldOfABudgetedScenarioSurvivesTheRoundTrip()
    {
        var read = Reread(Budgeted);

        Assert.Equal(3, read.Party.PregeneratedLevel);
        Assert.Null(read.Party.Members);
        Assert.Null(read.Objective);
        Assert.Null(read.ContentVersion);
        Assert.Null(read.Seed);

        Assert.Null(read.Enemies.Roster);
        var budget = read.Enemies.Budget!;
        Assert.Equal(EncounterDifficulty.High, budget.Difficulty);
        Assert.Equal(5, budget.Level);
        Assert.Equal(2m, budget.MaximumChallengeRating);
        Assert.True(budget.Horde);
        Assert.Equal(MonsterCoverage.Diminished, budget.CoverageFloor);
        Assert.False(budget.PlausibleFoesOnly);
        Assert.False(budget.TraditionalFoesOnly);
    }

    /// <summary>
    /// The guard the two tests above cannot be: they check the fields that existed when
    /// they were written, and a field added get-only next year would pass both by being
    /// invisible on each side of the round trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Content.ContentSerializer"/> sets <c>IgnoreReadOnlyProperties = true</c>,
    /// so a get-only property is silently not written and silently absent on read — the
    /// exact silent-loss shape the project's rules exist for. This walks the whole
    /// property graph of <see cref="BattleScenario"/> — every record reachable from it
    /// that this assembly declares — and asserts two things per property: that it can be
    /// set at all, and that its camelCase name actually appears in the serialized JSON of
    /// a scenario that populates it.
    /// </para>
    /// <para>
    /// <b>Knockout-verified</b> (#416's pattern): changing <c>BattleScenario.Seed</c>,
    /// <c>ScenarioBudget.Horde</c> or <c>ScenarioMember.Level</c> to get-only fails this
    /// test naming the property, before it fails anything else.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryDataPropertyCanBeSetAndIsActuallyWritten()
    {
        var written = ScenarioFile.ToJson(Explicit) + ScenarioFile.ToJson(Budgeted);
        var visited = new HashSet<Type>();
        var walked = 0;

        foreach (var (owner, property) in Reachable(typeof(BattleScenario), visited))
        {
            walked++;

            Assert.True(
                property.SetMethod is not null,
                $"{owner.Name}.{property.Name} has no init accessor, so ContentSerializer's "
                + "IgnoreReadOnlyProperties would drop it from every scenario file silently.");

            var name = $"\"{JsonNamingPolicy.CamelCase.ConvertName(property.Name)}\"";

            Assert.True(
                written.Contains(name, StringComparison.Ordinal),
                $"{owner.Name}.{property.Name} never appears in a serialized scenario as {name}; "
                + "either it cannot be written, or this test's two samples stopped populating it.");
        }

        // The premise: if the walk stops finding properties, the assertions above pass by
        // examining nothing. Eight on BattleScenario, two on ScenarioParty, two on
        // ScenarioMember, two on ScenarioEnemies, two on ScenarioRosterEntry, seven on
        // ScenarioBudget, two on ObjectiveSpec.
        Assert.Equal(25, walked);
    }

    /// <summary>
    /// <c>UnmappedMemberHandling.Disallow</c>: a typo is refused naming the property,
    /// never skipped. This is the guard that replaces the DTO mirror this project
    /// deliberately does not have.
    /// </summary>
    [Fact]
    public void AnUnknownMemberIsRefusedNamingIt()
    {
        var json = ScenarioFile.ToJson(Budgeted);
        var load = ScenarioFile.FromJson(json.Insert(json.IndexOf('{') + 1, "\n  \"tittle\": 1,"));

        Assert.False(load.IsValid);
        Assert.Contains("tittle", Assert.Single(load.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingRequiredMemberIsRefusedNamingIt()
    {
        var json = string.Join(
            '\n',
            ScenarioFile.ToJson(Budgeted)
                .Split('\n')
                .Where(line => !line.Contains("\"notes\":", StringComparison.Ordinal)));

        var load = ScenarioFile.FromJson(json);

        Assert.False(load.IsValid);
        Assert.Contains("notes", Assert.Single(load.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFormatVersionThisBuildDoesNotWriteIsRefused()
    {
        var load = ScenarioFile.FromJson(
            ScenarioFile.ToJson(Budgeted with { FormatVersion = 99 }));

        Assert.Contains("formatVersion 99", Assert.Single(load.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void AScenarioWithNoNameIsRefused() =>
        Assert.Contains(
            "name:",
            string.Join('\n', Load(Budgeted with { Name = "  " }).Errors),
            StringComparison.Ordinal);

    /// <summary>
    /// Blank notes are the library's problem, not the format's — a capture written to
    /// somebody's own disk owes the library nothing. <c>ScenarioLibraryTests</c> is where
    /// a committed scenario without a stated reason becomes a failure.
    /// </summary>
    [Fact]
    public void BlankNotesLoadFineBecauseOnlyTheCommittedLibraryRequiresThem() =>
        Assert.True(Load(Budgeted with { Notes = "" }).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void APartyLevelOutsideTheBandIsRefusedNamingTheFieldAndTheRange(int level)
    {
        var errors = string.Join('\n', Load(Budgeted with
        {
            Party = new ScenarioParty { PregeneratedLevel = level },
        }).Errors);

        Assert.Contains($"party.pregeneratedLevel={level}", errors, StringComparison.Ordinal);
        Assert.Contains("(1-5)", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AMemberLevelOutsideTheBandIsRefusedNamingWhichMember()
    {
        var errors = string.Join('\n', Load(Explicit with
        {
            Party = new ScenarioParty
            {
                Members =
                [
                    new ScenarioMember { Draft = Draft, Level = 3 },
                    new ScenarioMember { Draft = Draft, Level = 9 },
                ],
            },
        }).Errors);

        Assert.Contains("party.members[1].level=9", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void APartyNamingNeitherModeIsRefusedSayingSo() =>
        Assert.Contains(
            "names neither a pregeneratedLevel nor members",
            string.Join('\n', Load(Budgeted with { Party = new ScenarioParty() }).Errors),
            StringComparison.Ordinal);

    [Fact]
    public void APartyNamingBothModesIsRefusedSayingSo() =>
        Assert.Contains(
            "names both a pregeneratedLevel and members",
            string.Join('\n', Load(Budgeted with
            {
                Party = new ScenarioParty
                {
                    PregeneratedLevel = 3,
                    Members = [new ScenarioMember { Draft = Draft, Level = 3 }],
                },
            }).Errors),
            StringComparison.Ordinal);

    [Fact]
    public void AnEmptyExplicitPartyIsRefused() =>
        Assert.Contains(
            "at least one member",
            string.Join('\n', Load(Explicit with { Party = new ScenarioParty { Members = [] } }).Errors),
            StringComparison.Ordinal);

    [Fact]
    public void MoreMembersThanAScenarioMayFieldIsRefused() =>
        Assert.Contains(
            $"more than the {ScenarioParty.MaximumMembers}",
            string.Join('\n', Load(Explicit with
            {
                Party = new ScenarioParty
                {
                    Members =
                    [
                        .. Enumerable
                            .Range(0, ScenarioParty.MaximumMembers + 1)
                            .Select(_ => new ScenarioMember { Draft = Draft, Level = 3 }),
                    ],
                },
            }).Errors),
            StringComparison.Ordinal);

    [Fact]
    public void EnemiesNamingNeitherModeIsRefusedSayingSo() =>
        Assert.Contains(
            "names neither a roster nor a budget",
            string.Join('\n', Load(Budgeted with { Enemies = new ScenarioEnemies() }).Errors),
            StringComparison.Ordinal);

    [Fact]
    public void EnemiesNamingBothModesIsRefusedSayingSo() =>
        Assert.Contains(
            "names both a roster and a budget",
            string.Join('\n', Load(Budgeted with
            {
                Enemies = new ScenarioEnemies
                {
                    Roster = [new ScenarioRosterEntry { MonsterId = "monster.ogre", Count = 1 }],
                    Budget = Budgeted.Enemies.Budget,
                },
            }).Errors),
            StringComparison.Ordinal);

    [Theory]
    [InlineData(0)]
    [InlineData(RosterParser.MaximumCount + 1)]
    public void ARosterCountOutsideTheGrammarsCeilingIsRefused(int count) =>
        Assert.Contains(
            $"enemies.roster[0].count={count}",
            string.Join('\n', Load(Explicit with
            {
                Enemies = new ScenarioEnemies
                {
                    Roster = [new ScenarioRosterEntry { MonsterId = "monster.ogre", Count = count }],
                },
            }).Errors),
            StringComparison.Ordinal);

    [Fact]
    public void AnEmptyRosterIsRefused() =>
        Assert.Contains(
            "at least one entry",
            string.Join('\n', Load(Explicit with { Enemies = new ScenarioEnemies { Roster = [] } }).Errors),
            StringComparison.Ordinal);

    [Fact]
    public void ASurviveObjectiveWithNoRoundsIsRefused() =>
        Assert.Contains(
            "at least one round",
            string.Join('\n', Load(Budgeted with { Objective = new ObjectiveSpec(ObjectiveKind.SurviveRounds) }).Errors),
            StringComparison.Ordinal);

    /// <summary>
    /// A round count on an objective that does not count rounds is not harmlessly
    /// ignored: it is an author believing they asked for something. Same shape as every
    /// other refusal in the file.
    /// </summary>
    [Fact]
    public void RoundsOnAnObjectiveThatDoesNotCountThemIsRefused() =>
        Assert.Contains(
            "only a SurviveRounds objective counts rounds",
            string.Join('\n', Load(Budgeted with { Objective = new ObjectiveSpec(ObjectiveKind.KillLeader, 4) }).Errors),
            StringComparison.Ordinal);

    /// <summary>
    /// Everything wrong at once, because an author fixing a file wants the whole list
    /// rather than one problem per run. This is what the result record buys over throwing
    /// on the first failure.
    /// </summary>
    [Fact]
    public void EveryStructuralProblemIsReportedNotJustTheFirst()
    {
        var load = Load(Budgeted with
        {
            Name = "",
            Party = new ScenarioParty { PregeneratedLevel = 12 },
            Enemies = new ScenarioEnemies(),
            Objective = new ObjectiveSpec(ObjectiveKind.KillLeader, 2),
        });

        Assert.Equal(4, load.Errors.Count);
        Assert.Null(load.Scenario);
    }

    /// <summary>The preset stores a level and no drafts at all — see <see cref="ScenarioContent.ResolveParty"/>.</summary>
    [Fact]
    public void ThePregeneratedPresetStoresNoDrafts()
    {
        var json = ScenarioFile.ToJson(Budgeted);

        Assert.Contains("\"pregeneratedLevel\": 3", json, StringComparison.Ordinal);
        Assert.DoesNotContain("classId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("speciesId", json, StringComparison.OrdinalIgnoreCase);
    }

    private static BattleScenario Reread(BattleScenario scenario)
    {
        var load = ScenarioFile.FromJson(ScenarioFile.ToJson(scenario));

        Assert.Empty(load.Errors);

        return load.Scenario!;
    }

    private static ScenarioLoad Load(BattleScenario scenario) =>
        ScenarioFile.FromJson(ScenarioFile.ToJson(scenario));

    /// <summary>
    /// Every public instance property of every record this assembly declares that is
    /// reachable from <paramref name="type"/>. Stops at types from other assemblies —
    /// <c>CharacterDraft</c> is <c>SRDCombat.Core</c>'s and is pinned by the save's own
    /// tests, not by the scenario's.
    /// </summary>
    private static IEnumerable<(Type Owner, PropertyInfo Property)> Reachable(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            yield return (type, property);

            var next = Unwrap(property.PropertyType);

            if (next.Assembly != typeof(BattleScenario).Assembly)
            {
                continue;
            }

            foreach (var reached in Reachable(next, visited))
            {
                yield return reached;
            }
        }
    }

    /// <summary>Peels <c>Nullable&lt;T&gt;</c> and <c>IReadOnlyList&lt;T&gt;</c> off a property type.</summary>
    private static Type Unwrap(Type type) =>
        type.IsGenericType ? Unwrap(type.GetGenericArguments()[0]) : type;
}
