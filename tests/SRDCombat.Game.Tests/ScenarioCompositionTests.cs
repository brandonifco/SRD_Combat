using SRDCombat.Content;

namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="ScenarioComposition.Compose"/> — the <c>--scenario</c>/<c>--spawn</c>
/// authoring half of #490's flag-vs-mode seam, moved off <c>FightScreen</c> by #490a so
/// a plain xUnit test pins the decision and its assembled refusal wording rather than
/// only a probe capture.
/// </summary>
/// <remarks>
/// <para>
/// <b>Moved from <c>SRDCombat.Viewer.Tests.ScenarioFromFileTests</c>.</b> That class
/// pinned <c>FightScreen.ScenarioFromFile</c> — the <c>--scenario=&lt;path&gt;</c> half
/// alone, reachable from a plain xUnit test only because the flag's value was already
/// read past Godot's <c>OS</c> boundary. Now that the whole decision (the mutual
/// exclusion, the budgeted default, and <c>--spawn</c>'s roster/level assembly) lives in
/// <c>SRDCombat.Game</c>, this class tests all three cases directly, with no client and
/// no Godot involved at all.
/// </para>
/// <para>
/// <b>An empty <see cref="SrdContent"/> is enough for every refusal path.</b> A missing
/// monster id refuses regardless of what else the content holds, and even the party's
/// own resolution failing against empty content (no species/class/background to find)
/// still lands as an error — <c>ScenarioContent.CheckAgainst</c> catches it and reports
/// it in the same list — so <c>errors.Count &gt; 0</c> either way. The one path this
/// cannot exercise is a scenario that actually <em>resolves</em> (the notice-only,
/// no-error case), because resolving a party needs real species/class/background
/// definitions; that path is
/// <c>ScenarioContentTests.AContentVersionMismatchIsANoticeAndRefusesNothing</c>'s,
/// against the real corpus it needs. What this class pins is the plumbing above it: a
/// <see cref="ScenarioCheck"/> with errors becomes a refusal naming them, and one with
/// only notices comes back through <see cref="ScenarioComposition.Result.Notices"/>
/// instead of refusing.
/// </para>
/// </remarks>
public sealed class ScenarioCompositionTests : IDisposable
{
    private static readonly SrdContent EmptyContent =
        new(Monsters: [], Weapons: [], Armor: [], Species: [], Backgrounds: [], Classes: [], Spells: [], MagicItems: []);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "srdcombat-scenario-composition-tests", Guid.NewGuid().ToString("N"));

    public ScenarioCompositionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    private string Write(string name, string json)
    {
        var path = PathFor(name);
        File.WriteAllText(path, json);
        return path;
    }

    private static string ScenarioJson(string extra = "") =>
        $$"""
        {
          "formatVersion": 1,
          "name": "a scenario",
          "notes": "for a test",
          "party": { "pregeneratedLevel": 3 },
          "enemies": { "roster": [ { "monsterId": "monster.ogre", "count": 1 } ] }{{(extra.Length == 0 ? "" : "," + extra)}}
        }
        """;

    private static ScenarioComposition.Result Compose(
        FlagValue spawn = default,
        FlagValue scenario = default,
        FlagValue level = default,
        SrdContent? content = null) =>
        ScenarioComposition.Compose(spawn, scenario, level, content ?? EmptyContent);

    // ---- the mutual exclusion ----

    [Fact]
    public void ScenarioAndSpawnTogetherIsRefused()
    {
        var result = Compose(spawn: FlagValue.Of("Ogre"), scenario: FlagValue.Of("a.scenario.json"));

        Assert.Null(result.Scenario);
        Assert.Equal(
            "--scenario and --spawn both name this fight's cast; pass one, not both.",
            result.Refusal);
    }

    // ---- neither flag: the budgeted default ----

    [Fact]
    public void NoSpawnAndNoScenarioComposesTheBudgetedDefault()
    {
        var result = Compose();

        Assert.Null(result.Refusal);
        Assert.NotNull(result.Scenario);
        Assert.Equal("one fight", result.Scenario!.Name);
        Assert.Equal(ScenarioComposition.BudgetedFightLevel, result.Scenario.Party.PregeneratedLevel);
        Assert.NotNull(result.Scenario.Enemies.Budget);
        Assert.Equal(ScenarioComposition.BudgetedFightLevel, result.Scenario.Enemies.Budget!.Level);
        Assert.Empty(result.Notices);
    }

    // ---- --spawn ----

    [Fact]
    public void ABareSpawnIsRefused()
    {
        var result = Compose(spawn: FlagValue.Bare());

        Assert.Null(result.Scenario);
        Assert.Equal(
            "--spawn refused: --spawn: no value given (use --spawn=\"...\")",
            result.Refusal);
    }

    [Fact]
    public void SpawnRosterErrorsAreReportedByName()
    {
        var result = Compose(spawn: FlagValue.Of("Nonexistent Monster"));

        Assert.Null(result.Scenario);
        Assert.StartsWith("--spawn refused: ", result.Refusal);
        Assert.Contains("Nonexistent Monster", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("no such monster", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ABadLevelInSpawnModeIsRefusedAndJoinedWithRosterErrors()
    {
        var result = Compose(spawn: FlagValue.Of("Nonexistent Monster"), level: FlagValue.Of("9"));

        Assert.Null(result.Scenario);
        Assert.StartsWith("--spawn refused: ", result.Refusal);
        // Both failures land in one message, "; "-joined — a roster typo and a bad
        // level are two separate reasons this cast cannot run, and #463's stated
        // discipline is naming every failure, not just the first one found.
        Assert.Contains("no such monster", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("out of range", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("; ", result.Refusal, StringComparison.Ordinal);
    }

    // ---- --scenario (the moved ScenarioFromFile cases) ----

    [Fact]
    public void ScenarioNoValueGiven_RefusesNamingTheFlag()
    {
        var result = Compose(scenario: FlagValue.Bare());

        Assert.Null(result.Scenario);
        Assert.Equal("--scenario: no value given (use --scenario=<path>)", result.Refusal);
    }

    [Fact]
    public void ScenarioMissingFile_RefusesNamingThePath()
    {
        var path = PathFor("does-not-exist.scenario.json");

        var result = Compose(scenario: FlagValue.Of(path));

        Assert.Null(result.Scenario);
        Assert.Equal($"--scenario=\"{path}\": no such file", result.Refusal);
    }

    [Fact]
    public void ScenarioUnparseableJson_RefusesWithTheParsersMessage()
    {
        var path = Write("broken.scenario.json", "{ not json");

        var result = Compose(scenario: FlagValue.Of(path));

        Assert.Null(result.Scenario);
        Assert.StartsWith($"--scenario=\"{path}\" refused: ", result.Refusal);
    }

    [Fact]
    public void ScenarioUnknownMember_RefusesNamingIt()
    {
        var path = Write("typo.scenario.json", ScenarioJson("""  "notASerializedField": true """));

        var result = Compose(scenario: FlagValue.Of(path));

        Assert.Null(result.Scenario);
        Assert.Contains("notASerializedField", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ScenarioAMissingMonsterId_RefusesNamingIt()
    {
        var path = Write("missing-monster.scenario.json", ScenarioJson());

        var result = Compose(scenario: FlagValue.Of(path));

        Assert.Null(result.Scenario);
        Assert.Contains("monster.ogre", result.Refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A structurally broken scenario — one <see cref="ScenarioFile.FromJson"/> itself
    /// refuses, before any content is consulted — is reported the same way as a content
    /// drift, so the caller has one refusal shape rather than two.
    /// </summary>
    [Fact]
    public void ScenarioAStructurallyBrokenScenario_RefusesWithFromJsonsMessage()
    {
        var path = Write(
            "no-cast.scenario.json",
            """
            {
              "formatVersion": 1,
              "name": "",
              "notes": "",
              "party": { "pregeneratedLevel": 3 },
              "enemies": {}
            }
            """);

        var result = Compose(scenario: FlagValue.Of(path));

        Assert.Null(result.Scenario);
        Assert.Contains("enemies", result.Refusal, StringComparison.Ordinal);
    }
}
