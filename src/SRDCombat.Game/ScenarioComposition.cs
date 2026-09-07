using SRDCombat.Content;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// One client flag as the composition seam sees it, with the client's own <c>--flag</c>
/// vs. <c>--flag=value</c> dialect already resolved before this type exists.
/// </summary>
/// <remarks>
/// <see cref="ScenarioComposition"/> is shared by every client that authors a one-fight
/// scenario, and clients disagree about how a flag's value is spelled on the command
/// line — this client's <c>--name=value</c>, the console's <c>--name value</c> (see
/// <c>ClientArguments</c>'s and the console's own <c>ConsoleArguments</c>'s doc
/// comments). Splitting a shell word into <c>=</c>-delimited halves, or consuming the
/// next argument, is dialect and stays in each client; what crosses into <c>Game</c> is
/// only the fact both dialects agree on: was the flag present at all, and if so, what
/// value (if any) came with it. <c>Present</c> true with <c>Value</c> null is a bare
/// flag — passed with nothing after it, refused wherever a value is required rather than
/// treated as absent (the same present-but-valueless distinction <c>ClientArguments</c>'
/// own doc comment describes).
/// </remarks>
public readonly record struct FlagValue(bool Present, string? Value)
{
    /// <summary>The flag was not passed at all.</summary>
    public static readonly FlagValue Absent = new(Present: false, Value: null);

    /// <summary>The flag was passed with no value — <c>--name</c> alone.</summary>
    public static FlagValue Bare() => new(Present: true, Value: null);

    /// <summary>The flag was passed with (or without) a value already read by the caller's dialect.</summary>
    public static FlagValue Of(string? value) => new(Present: true, Value: value);
}

/// <summary>
/// Decides what a one-fight authoring flag set (<c>--spawn</c> / <c>--scenario</c> /
/// <c>--level</c>) means, and assembles the client-facing refusal text when it means
/// nothing runnable (#490).
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision and the wording live here; a client only calls and shows.</b> Before
/// this, <c>FightScreen.ScenarioFromArguments</c> held the <c>--scenario</c>/<c>--spawn</c>
/// mutual exclusion, the budgeted-default fallback, and the assembled
/// <c>"--spawn refused: …"</c> / <c>"--scenario=\"…\" refused: …"</c> text, reachable only
/// from a Godot-hosted test or a probe capture. That text is a tested decision, not
/// incidental formatting, so it belongs where a plain xUnit test can pin it —
/// <see cref="RosterParser"/> is the model this follows: compute in <c>Game</c>, render
/// in the client.
/// </para>
/// <para>
/// <b>This PR (#490a) is the authoring half only</b> — <c>--one-fight</c> and
/// <c>--watch</c>'s <c>--scenario</c> vs. <c>--spawn</c> composition. The gauntlet-start
/// gates are a separate, later slice (#490b) and are untouched here.
/// </para>
/// </remarks>
public static class ScenarioComposition
{
    /// <summary>
    /// The level the flagless one-fight path has always run at, and the level its budget
    /// is priced for. One constant because a scenario states both, and two numbers that
    /// have to agree are one number — raising it is #443's concern, not this seam's.
    /// </summary>
    public const int BudgetedFightLevel = 3;

    /// <summary>
    /// A composed scenario, or the fully-assembled reason there isn't one.
    /// <see cref="Scenario"/> is null exactly when <see cref="Refusal"/> is not, and a
    /// caller throws <see cref="Refusal"/> verbatim — the wording is the tested decision,
    /// not a template the caller finishes.
    /// </summary>
    /// <param name="Scenario">The composed scenario, or null when refused.</param>
    /// <param name="Refusal">The client-facing refusal text, or null when composed.</param>
    /// <param name="Notices">
    /// Anything worth telling the player that refuses nothing — today only a
    /// <c>--scenario</c> file's content-fingerprint mismatch (design §5, note 3). Empty
    /// whenever there is nothing to say.
    /// </param>
    public sealed record Result(BattleScenario? Scenario, string? Refusal, IReadOnlyList<string> Notices)
    {
        /// <summary>Whether this result carries a scenario to run.</summary>
        public bool IsValid => Refusal is null && Scenario is not null;
    }

    /// <summary>
    /// Composes a one-fight scenario from this client's <c>--spawn</c>/<c>--scenario</c>/
    /// <c>--level</c> flags, refusing exactly what <c>FightScreen.ScenarioFromArguments</c>
    /// refused before it moved here — same checks, same order, same message.
    /// </summary>
    public static Result Compose(FlagValue spawn, FlagValue scenario, FlagValue level, SrdContent content)
    {
        // Named once, refused before either flag's own parsing runs: a file and a typed
        // roster are two different answers to "what does this fight fight", and picking
        // one over the other silently is exactly the shape #463 already closed for
        // --spawn against the gauntlet loop, one flag pair over.
        if (scenario.Present && spawn.Present)
        {
            return new Result(
                Scenario: null,
                Refusal: "--scenario and --spawn both name this fight's cast; pass one, not both.",
                Notices: []);
        }

        if (scenario.Present)
        {
            return ComposeFromFile(scenario.Value, content);
        }

        if (!spawn.Present)
        {
            return new Result(
                Scenario: new BattleScenario
                {
                    FormatVersion = ScenarioFile.CurrentFormatVersion,
                    Name = "one fight",
                    Notes = string.Empty,
                    Party = new ScenarioParty { PregeneratedLevel = BudgetedFightLevel },
                    Enemies = new ScenarioEnemies
                    {
                        Budget = new ScenarioBudget
                        {
                            Difficulty = EncounterDifficulty.Moderate,
                            Level = BudgetedFightLevel,
                        },
                    },
                },
                Refusal: null,
                Notices: []);
        }

        var errors = new List<string>();
        IReadOnlyList<MonsterDefinition> monsters = [];

        if (spawn.Value is { } text)
        {
            var roster = RosterParser.Parse(text, content.Monsters);
            errors.AddRange(roster.Errors);
            monsters = roster.Monsters;
        }
        else
        {
            errors.Add("--spawn: no value given (use --spawn=\"...\")");
        }

        var levelOk = ScenarioArguments.TryParseLevel(
            level.Value, level.Present, out var parsedLevel, out var levelError);

        if (!levelOk)
        {
            errors.Add(levelError!);
        }

        if (errors.Count > 0)
        {
            return new Result(
                Scenario: null,
                Refusal: $"--spawn refused: {string.Join("; ", errors)}",
                Notices: []);
        }

        return new Result(
            Scenario: new BattleScenario
            {
                FormatVersion = ScenarioFile.CurrentFormatVersion,
                Name = "--spawn",
                Notes = string.Empty,
                Party = new ScenarioParty { PregeneratedLevel = parsedLevel },
                Enemies = new ScenarioEnemies { Roster = RosterParser.ToRoster(monsters) },
            },
            Refusal: null,
            Notices: []);
    }

    /// <summary>
    /// Loads <c>--scenario=&lt;path&gt;</c>'s file and turns it into the scenario it
    /// names, refusing by name at every step: no value given, no such file, whatever
    /// <see cref="ScenarioFile.FromJson"/> reports for unparseable JSON, an unmapped
    /// member or a structurally broken scenario, and whatever
    /// <see cref="ScenarioContent.CheckAgainst"/> reports for an id this build's content
    /// no longer has. A content-fingerprint mismatch is <see cref="ScenarioCheck"/>'s
    /// <c>Notices</c>, not an error — S1's stated divergence from a save's refusal
    /// (<see cref="BattleScenario.ContentVersion"/>'s remarks) — so it comes back through
    /// <see cref="Result.Notices"/> rather than as a refusal.
    /// </summary>
    /// <param name="path"><c>--scenario</c>'s value, or null for a bare flag.</param>
    private static Result ComposeFromFile(string? path, SrdContent content)
    {
        if (path is null)
        {
            return new Result(null, "--scenario: no value given (use --scenario=<path>)", []);
        }

        if (!File.Exists(path))
        {
            return new Result(null, $"--scenario=\"{path}\": no such file", []);
        }

        var load = ScenarioFile.FromJson(File.ReadAllText(path));

        if (!load.IsValid)
        {
            return new Result(null, $"--scenario=\"{path}\" refused: {string.Join("; ", load.Errors)}", []);
        }

        var scenario = load.Scenario!;
        var check = ScenarioContent.CheckAgainst(scenario, content);

        if (!check.IsValid)
        {
            return new Result(null, $"--scenario=\"{path}\" refused: {string.Join("; ", check.Errors)}", []);
        }

        return new Result(scenario, null, check.Notices);
    }
}
