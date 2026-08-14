using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Game;

// The pacing series' instrument, committed so the methodology is code rather than
// archaeology (#132). Every number in CLAUDE.md's measured series should come from this
// tool, with its seed range written down.
//
// One run per seed: the pregenerated party at level 1 walks the default ladder,
// SimpleTacticsPolicy playing both sides, until defeat, survival, or the policy's round
// limit. The figure per seed is fights cleared of 30; the report is the median, the
// best, how many runs cleared everything, and how many reached level 4 (the number that
// decides whether the tier's upper content is ever seen — see #79's post-mortem).
//
// Loot is ON unless --no-loot is passed, because the canonical measurement is the game
// the player plays: Program.cs passes the run's random to CompleteFight, so Moderate
// rungs drop potions and High milestones drop items. --no-loot reproduces the
// 2026-08-14 series (whose scratch harness omitted the random without anyone deciding
// it) for continuity with those recorded numbers; the two forms differ by the loot
// itself AND by every dice draw after the first loot roll, so their absolute medians
// are not comparable — compare builds only within one form and one seed range.

var noLoot = args.Contains("--no-loot");
var (firstSeed, lastSeed) = SeedRange(args);

var contentDirectory = args.FirstOrDefault(argument => !argument.StartsWith('-')
        && !IsSeedValue(args, argument))
    ?? FindContentDirectory();

if (contentDirectory is null || !Directory.Exists(contentDirectory))
{
    Console.Error.WriteLine("No content directory found. Pass the path to data/srd.");
    return 1;
}

var content = ContentLoader.Load(contentDirectory);
var results = new List<(int Cleared, int Level)>();

for (var seed = firstSeed; seed <= lastSeed; seed++)
{
    var random = new SeededRandomSource(seed);
    var run = GauntletRun.Start(content, GauntletLadder.Default(), 1);

    while (run.Next is not null)
    {
        run.PrepareForNext(random);
        var fight = run.BeginNext(random);

        if (fight.Built.Monsters.Count == 0)
        {
            break;
        }

        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);

        // The policy's round limit fired: the fight cannot resolve, so the run's story
        // ends here with whatever it had cleared.
        if (!fight.Encounter.IsComplete)
        {
            break;
        }

        run.CompleteFight(fight, noLoot ? null : random);

        if (run.Outcome != RunOutcome.InProgress)
        {
            break;
        }
    }

    var level = run.Party.Max(member => member.Sheet.Level);
    results.Add((run.Cleared, level));
    Console.WriteLine($"seed {seed}: cleared {run.Cleared}, level {level}");
}

var sorted = results.Select(result => result.Cleared).OrderBy(cleared => cleared).ToArray();

// The median of an even count is the mean of the middle pair, which is how every
// recorded figure was made; an odd count takes the middle value.
var median = sorted.Length % 2 == 0
    ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
    : sorted[sorted.Length / 2];

Console.WriteLine(
    $"seeds {firstSeed}-{lastSeed} ({(noLoot ? "no loot" : "loot")}): " +
    $"median {median}  best {sorted[^1]}  " +
    $"cleared-all {results.Count(result => result.Cleared >= 30)}  " +
    $"reached-L4 {results.Count(result => result.Level >= 4)}");

return 0;

static (int First, int Last) SeedRange(string[] args)
{
    var index = Array.FindIndex(args, argument => argument is "--seeds");

    if (index >= 0 && index + 1 < args.Length)
    {
        var parts = args[index + 1].Split('-');

        if (parts.Length == 2
            && int.TryParse(parts[0], out var first)
            && int.TryParse(parts[1], out var last)
            && first <= last)
        {
            return (first, last);
        }

        Console.Error.WriteLine($"Cannot read '{args[index + 1]}' as a seed range; using 1-40.");
    }

    return (1, 40);
}

// The value following --seeds is not a content path, whatever it looks like.
static bool IsSeedValue(string[] args, string argument)
{
    var index = Array.IndexOf(args, argument);

    return index > 0 && args[index - 1] is "--seeds";
}

// Walks up from the working directory looking for data/srd, the way the console does.
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
