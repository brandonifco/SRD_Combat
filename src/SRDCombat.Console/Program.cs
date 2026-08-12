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

var party = PregeneratedParty.Build(content, level: 1, x: 1);
Display.PartySideId = PregeneratedParty.SideId;

// Two monsters the party can plausibly beat at level 1, drawn from the curated pool so
// the fight only contains creatures whose turns the engine executes in full.
var pool = MonsterPool.Draw(content.Monsters, maximumChallengeRating: 0.5m);

var chosen = pool
    .Where(monster => monster.ChallengeRating >= 0.25m)
    .OrderBy(monster => monster.Id, StringComparer.Ordinal)
    .Take(2)
    .ToArray();

if (chosen.Length == 0)
{
    Console.Error.WriteLine("The monster pool is empty at this challenge rating.");
    return 1;
}

var monsters = chosen
    .Select((monster, index) => new Combatant(
        $"monster{index}",
        monster.Name,
        "monsters",
        CombatantStats.FromMonster(monster),
        new GridPosition(8, 1 + index)))
    .ToArray();

var encounter = Encounter.Start(
    new Battlefield(12, 6),
    [.. party.Select(member => member.Combatant), .. monsters],
    random);

Display.Labels = Labels.For(encounter.Combatants);

Console.WriteLine($"The party meets {string.Join(" and ", chosen.Select(monster => monster.Name))}.");
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
