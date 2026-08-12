using SRDCombat.Console;
using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

// The text client: the first thing in this project a person can actually play.
//
// It builds the pregenerated party from real extracted content, draws an encounter
// from MonsterPool at a matching challenge rating, and hands the party's turns to the
// player while SimpleTacticsPolicy takes the monsters'.

// A seed makes a fight reproducible, which matters as much for reporting a bug as for
// testing: "it happened on seed 12345" is a complete repro.
var seed = SeedFrom(args) ?? Random.Shared.Next();

var contentDirectory = PositionalArguments(args).FirstOrDefault()
    ?? FindContentDirectory()
    ?? ".";

if (!Directory.Exists(contentDirectory))
{
    Console.Error.WriteLine($"No content at '{contentDirectory}'. Pass the path to data/srd.");
    return 1;
}

var content = ContentLoader.Load(contentDirectory);

var random = new SeededRandomSource(seed);

Console.WriteLine();

Display.PartySideId = PregeneratedParty.SideId;

// A single fight is still available, because it is the quickest way to look at one
// thing; the gauntlet is the game.
if (SingleFightRequested(args))
{
    var level = LevelFrom(args) ?? 1;
    var only = EncounterFactory.Build(content, PregeneratedParty.Build(content, level), DifficultyFrom(args), random);

    Console.WriteLine($"SRD_Combat — one fight (seed {seed})");
    return PlayFight(only, random) is FightResult.Won or FightResult.Lost ? 0 : 0;
}

var run = GauntletRun.Start(content, GauntletLadder.Default(), LevelFrom(args) ?? 1);

Console.WriteLine($"SRD_Combat — a gauntlet of {run.Ladder.Count} fights (seed {seed})");
Console.WriteLine("Type 'help' during a fight for commands.");

while (run.Next is { } step)
{
    var returnsBefore = run.Returns.Count;
    var rest = run.PrepareForNext(random);

    Console.WriteLine();
    Console.WriteLine(new string('=', 60));
    Console.WriteLine(
        $"Fight {run.Cleared + 1} of {run.Ladder.Count} — " +
        $"{step.Difficulty.ToString().ToLowerInvariant()} difficulty.");

    if (rest is { } taken)
    {
        Console.WriteLine($"The party takes a {taken} Rest.");
    }

    foreach (var returned in run.Returns.Skip(returnsBefore))
    {
        Console.WriteLine(returned + ".");
    }

    foreach (var (member, state) in run.Party.Zip(run.States))
    {
        Console.WriteLine(
            $"  {member.Draft.Name,-8} " +
            (state.IsDead
                ? "dead"
                : $"level {state.Level}, {state.CurrentHitPoints}/{member.Sheet.MaximumHitPoints} hp, " +
                  $"{state.HitDiceRemaining} hit {(state.HitDiceRemaining == 1 ? "die" : "dice")}, " +
                  $"{state.ExperiencePoints} xp"));
    }

    var fight = run.BeginNext(random);

    if (fight.Built.Monsters.Count == 0)
    {
        Console.Error.WriteLine("The budget bought nothing; the ladder cannot continue.");
        return 1;
    }

    var levelUpsBefore = run.LevelUps.Count;
    var result = PlayFight(fight, random);

    if (result == FightResult.Quit)
    {
        Console.WriteLine();
        Console.WriteLine("Left the gauntlet.");
        return 0;
    }

    run.CompleteFight(fight);

    foreach (var levelUp in run.LevelUps.Skip(levelUpsBefore))
    {
        Console.WriteLine(levelUp + "!");
    }
}

Console.WriteLine();
Console.WriteLine(run.Outcome == RunOutcome.Survived
    ? $"The gauntlet is beaten — {run.Ladder.Count} fights cleared."
    : $"The run ends after {run.Cleared} fight(s).");

var fallen = run.Fallen.ToArray();

if (fallen.Length > 0)
{
    Console.WriteLine("Fallen: " + string.Join(", ", fallen) + ".");
}
else if (run.Casualties.Count > 0)
{
    Console.WriteLine($"Everyone made it, though {run.Casualties.Count} went down along the way.");
}

return 0;

// Plays one fight to its end, drawing it first.
FightResult PlayFight(Fight fight, IRandomSource dice)
{
    Display.Labels = Labels.For(fight.Encounter.Combatants);

    var roster = fight.Built.Monsters
        .GroupBy(monster => monster.Name)
        .Select(group => group.Count() > 1 ? $"{group.Count()} {group.Key}s" : group.Key);

    Console.WriteLine($"Against: {string.Join(", ", roster)}.");
    Console.WriteLine(
        $"Budget {fight.Built.Budget} XP, spent {fight.Built.Spent}, {fight.Built.Remaining} left over.");

    if (!new CommandLoop(fight.Encounter, PregeneratedParty.SideId).Run())
    {
        return FightResult.Quit;
    }

    Console.WriteLine();
    Console.WriteLine(fight.Encounter.WinningSide == PregeneratedParty.SideId
        ? "The party wins."
        : "The party falls.");

    return fight.Encounter.WinningSide == PregeneratedParty.SideId ? FightResult.Won : FightResult.Lost;
}

static bool SingleFightRequested(string[] args) => args.Contains("--one-fight");

static EncounterDifficulty DifficultyFrom(string[] args)
{
    var index = Array.FindIndex(args, argument => argument is "--difficulty");

    return index >= 0
        && index + 1 < args.Length
        && Enum.TryParse<EncounterDifficulty>(args[index + 1], ignoreCase: true, out var difficulty)
            ? difficulty
            // Low is "one or two scary moments ... their characters should emerge
            // victorious", which is the right default for sitting down cold.
            : EncounterDifficulty.Low;
}

static int? LevelFrom(string[] args)
{
    var index = Array.FindIndex(args, argument => argument is "--level");

    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var level)
        ? Math.Clamp(level, 1, 5)
        : null;
}

static int? SeedFrom(string[] args)
{
    var index = Array.FindIndex(args, argument => argument is "--seed");

    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var seed)
        ? seed
        : null;
}

/// <summary>
/// Arguments that are not options and not an option's value.
/// </summary>
/// <remarks>
/// The options that take a value are named explicitly, and both cheaper rules were tried
/// and failed on their first run. "The first argument without a dash" reads the value of
/// <c>--seed 12345</c> as a content path; "every option takes one value" then swallowed
/// <c>--difficulty</c> as if it belonged to the valueless <c>--one-fight</c>, handing the
/// content loader "high". Argument shape is not guessable, so it is declared.
/// </remarks>
static IEnumerable<string> PositionalArguments(string[] args)
{
    string[] takesAValue = ["--seed", "--level", "--difficulty"];

    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith('-'))
        {
            yield return args[i];
            continue;
        }

        if (takesAValue.Contains(args[i], StringComparer.Ordinal))
        {
            i++;
        }
    }
}

// Walks up from the working directory looking for data/srd, so the client runs from
// anywhere in the repo without being told where the content is.
static string? FindContentDirectory()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, "data", "srd");

        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}

/// <summary>How a fight ended, from the client's point of view.</summary>
internal enum FightResult
{
    Won,
    Lost,
    Quit,
}
