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
Console.WriteLine($"SRD_Combat — a fight (seed {seed})");

var difficulty = DifficultyFrom(args);
var level = LevelFrom(args) ?? 1;

var party = PregeneratedParty.Build(content, level);
Display.PartySideId = PregeneratedParty.SideId;

var fight = EncounterFactory.Build(content, party, difficulty, random);
var encounter = fight.Encounter;

if (fight.Built.Monsters.Count == 0)
{
    Console.Error.WriteLine("The budget bought nothing. Try a higher difficulty or level.");
    return 1;
}

Display.Labels = Labels.For(encounter.Combatants);

var roster = fight.Built.Monsters
    .GroupBy(monster => monster.Name)
    .Select(group => group.Count() > 1 ? $"{group.Count()} {group.Key}s" : group.Key)
    .ToArray();

Console.WriteLine(
    $"A {difficulty.ToString().ToLowerInvariant()}-difficulty fight for {party.Count} level {level} " +
    $"characters: {string.Join(", ", roster)}.");
Console.WriteLine(
    $"Budget {fight.Built.Budget} XP, spent {fight.Built.Spent}, {fight.Built.Remaining} left over.");
Console.WriteLine("Type 'help' for commands.");

var completed = new CommandLoop(encounter, PregeneratedParty.SideId).Run();

Console.WriteLine();

if (!completed)
{
    Console.WriteLine("Left the fight.");
    return 0;
}

// Deliberately not echoing WinningSide: it is an internal identifier, and "monsters
// holds the field" is the sort of line that tells a player they are reading a database.
Console.WriteLine(
    encounter.WinningSide == PregeneratedParty.SideId
        ? "The party wins."
        : "The party falls.");

return 0;

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
/// Written out rather than "the first argument that does not start with a dash", which
/// reads the value of <c>--seed 12345</c> as a content path — caught on the first run.
/// </remarks>
static IEnumerable<string> PositionalArguments(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith('-'))
        {
            // Every option this client takes has exactly one value.
            i++;
            continue;
        }

        yield return args[i];
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
