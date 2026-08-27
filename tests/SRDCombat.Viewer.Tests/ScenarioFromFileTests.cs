using SRDCombat.Content;
using SRDCombat.Game;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="FightScreen.ScenarioFromFile"/> — the <c>--scenario=&lt;path&gt;</c> half
/// of #476's seam — refuses by name at every step and carries a content-fingerprint
/// mismatch as a notice rather than a refusal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Split from argument-reading on purpose, so this is reachable at all.</b>
/// <c>FightScreen.ScenarioFromFile</c> takes the flag's value as a parameter instead of
/// calling <c>ArgumentValue("scenario")</c> itself — that call reaches into Godot's
/// <c>OS</c> singleton and, per this project's own stated boundary (see this project's
/// <c>.csproj</c>), cannot run outside the engine without terminating the test host.
/// Everything <em>past</em> the value — reading the file, parsing it, checking it
/// against content — is ordinary .NET and <c>SRDCombat.Game</c> code, so it is tested
/// here rather than only by a probe capture.
/// </para>
/// <para>
/// <b>An empty <see cref="SrdContent"/> is enough for every refusal path.</b> A missing
/// monster id refuses regardless of what else the content holds, and even the party's
/// own resolution failing against empty content (no species/class/background to find)
/// still lands as an error — <c>ScenarioContent.CheckAgainst</c> catches it and reports
/// it in the same list — so <c>errors.Count &gt; 0</c> either way. The one path this
/// cannot exercise is a scenario that actually <em>resolves</em> (the notice-only,
/// no-error case), because resolving a party needs real species/class/background
/// definitions; that path is <c>ScenarioContentTests.AContentVersionMismatchIsANoticeAndRefusesNothing</c>'s
/// in <c>SRDCombat.Game.Tests</c>, against the real corpus it needs. What this class
/// pins is the plumbing above it: a <see cref="ScenarioCheck"/> with errors becomes a
/// <see cref="FightScreen.ScenarioRefusedException"/> naming them, and one with only
/// notices comes back through the <c>out</c> parameter instead of throwing.
/// </para>
/// </remarks>
public sealed class ScenarioFromFileTests : IDisposable
{
    private static readonly SrdContent EmptyContent =
        new(Monsters: [], Weapons: [], Armor: [], Species: [], Backgrounds: [], Classes: [], Spells: [], MagicItems: []);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "srdcombat-scenario-from-file-tests", Guid.NewGuid().ToString("N"));

    public ScenarioFromFileTests() => Directory.CreateDirectory(_directory);

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

    [Fact]
    public void NoValueGiven_RefusesNamingTheFlag()
    {
        var refusal = Assert.Throws<FightScreen.ScenarioRefusedException>(
            () => FightScreen.ScenarioFromFile(null, EmptyContent, out _));

        Assert.Equal("--scenario: no value given (use --scenario=<path>)", refusal.Message);
    }

    [Fact]
    public void MissingFile_RefusesNamingThePath()
    {
        var path = PathFor("does-not-exist.scenario.json");

        var refusal = Assert.Throws<FightScreen.ScenarioRefusedException>(
            () => FightScreen.ScenarioFromFile(path, EmptyContent, out _));

        Assert.Equal($"--scenario=\"{path}\": no such file", refusal.Message);
    }

    [Fact]
    public void UnparseableJson_RefusesWithTheParsersMessage()
    {
        var path = Write("broken.scenario.json", "{ not json");

        var refusal = Assert.Throws<FightScreen.ScenarioRefusedException>(
            () => FightScreen.ScenarioFromFile(path, EmptyContent, out _));

        Assert.StartsWith($"--scenario=\"{path}\" refused: ", refusal.Message);
    }

    [Fact]
    public void UnknownMember_RefusesNamingIt()
    {
        var path = Write("typo.scenario.json", ScenarioJson("""  "notASerializedField": true """));

        var refusal = Assert.Throws<FightScreen.ScenarioRefusedException>(
            () => FightScreen.ScenarioFromFile(path, EmptyContent, out _));

        Assert.Contains("notASerializedField", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingMonsterId_RefusesNamingIt()
    {
        var path = Write("missing-monster.scenario.json", ScenarioJson());

        var refusal = Assert.Throws<FightScreen.ScenarioRefusedException>(
            () => FightScreen.ScenarioFromFile(path, EmptyContent, out _));

        Assert.Contains("monster.ogre", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A structurally broken scenario — one <see cref="ScenarioFile.FromJson"/> itself
    /// refuses, before any content is consulted — is reported the same way as a content
    /// drift, so the caller has one refusal shape rather than two.
    /// </summary>
    [Fact]
    public void AStructurallyBrokenScenario_RefusesWithFromJsonsMessage()
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

        var refusal = Assert.Throws<FightScreen.ScenarioRefusedException>(
            () => FightScreen.ScenarioFromFile(path, EmptyContent, out _));

        Assert.Contains("enemies", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoticeSuffix_OfNoNotices_IsEmpty() =>
        Assert.Equal(string.Empty, FightScreen.NoticeSuffix([]));

    [Fact]
    public void NoticeSuffix_JoinsAndDashesAShortNotice() =>
        Assert.Equal(" — a short notice", FightScreen.NoticeSuffix(["a short notice"]));

    [Fact]
    public void NoticeSuffix_TrimsALongNoticeRatherThanOverrunningTheHeading()
    {
        var suffix = FightScreen.NoticeSuffix([new string('x', 200)]);

        // " — " (3) + Chrome.Trim's own width (100), the width every other single-line
        // heading fragment in this client is trimmed to.
        Assert.Equal(103, suffix.Length);
        Assert.EndsWith("…", suffix, StringComparison.Ordinal);
    }
}
