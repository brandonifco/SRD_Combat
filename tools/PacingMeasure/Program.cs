using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
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

// The party starts at level 1 unless told otherwise. --start-level exists to separate
// "this change is bad" from "this change is bad for a level 1 party", which the canonical
// form cannot tell apart: most runs die in the opening cycle, so a change that only hurts
// the fragile opening moves every figure exactly as a change that hurts everywhere does.
// Compare like with like — a level 3 start is its own baseline, not comparable to a
// level 1 one, because the budget re-prices every encounter against the party's level.
var startLevel = StartLevel(args);

var contentDirectory = args.FirstOrDefault(argument => !argument.StartsWith('-')
        && !IsSeedValue(args, argument))
    ?? FindContentDirectory();

if (contentDirectory is null || !Directory.Exists(contentDirectory))
{
    Console.Error.WriteLine("No content directory found. Pass the path to data/srd.");
    return 1;
}

var content = ContentLoader.Load(contentDirectory);
var results = new List<(int Cleared, int Level, RunEnd End)>();

for (var seed = firstSeed; seed <= lastSeed; seed++)
{
    var random = new SeededRandomSource(seed);
    var run = GauntletRun.Start(content, GauntletLadder.Default(), startLevel);
    var end = RunEnd.Cleared;

    while (run.Next is not null)
    {
        var rest = run.PrepareForNext(random);

        // The Long Rest merchant is part of the game as played: the canonical run
        // spends its winnings the way the auto-buyer does, so the economy's effect on
        // pacing is measured rather than accrued and ignored.
        if (rest == RestKind.Long)
        {
            Shop.AutoBuy(content, run);
        }

        var fight = run.BeginNext(random);

        if (fight.Built.Monsters.Count == 0)
        {
            end = RunEnd.NoFight;
            break;
        }

        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);

        // The policy's round limit fired: the fight cannot resolve, so the run's story
        // ends here with whatever it had cleared. Counted separately from defeat rather
        // than folded into it: a party that died and a fight that would not resolve look
        // identical in "fights cleared" and mean opposite things about a change. A
        // battlefield the policy cannot cross shows up here and nowhere else.
        if (!fight.Encounter.IsComplete)
        {
            end = RunEnd.Stalled;
            break;
        }

        run.CompleteFight(fight, noLoot ? null : random);

        if (run.Outcome != RunOutcome.InProgress)
        {
            end = run.Outcome == RunOutcome.Defeated ? RunEnd.Defeated : RunEnd.Cleared;
            break;
        }
    }

    var level = run.Party.Max(member => member.Sheet.Level);
    results.Add((run.Cleared, level, end));
    Console.WriteLine($"seed {seed}: cleared {run.Cleared}, level {level}, {end}");
}

var sorted = results.Select(result => result.Cleared).OrderBy(cleared => cleared).ToArray();

// The median of an even count is the mean of the middle pair, which is how every
// recorded figure was made; an odd count takes the middle value.
var median = sorted.Length % 2 == 0
    ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
    : sorted[sorted.Length / 2];

Console.WriteLine(
    $"seeds {firstSeed}-{lastSeed} ({(noLoot ? "no loot" : "loot")}" +
    $"{(startLevel == 1 ? string.Empty : $", from level {startLevel}")}): " +
    $"median {median}  best {sorted[^1]}  " +
    $"cleared-all {results.Count(result => result.Cleared >= 30)}  " +
    $"reached-L4 {results.Count(result => result.Level >= 4)}");

// The shape, because the median is the wrong summary for this distribution and is about
// to stop working as an instrument entirely. Runs do not spread around a centre: they
// pile at "died in the opening" and at "cleared everything", so the median reports which
// pile is bigger rather than how far a run gets. Once clears pass half the seeds it pins
// to 30 and can never move again — at 54 of 120 that is about seven runs away. Read these
// three numbers, not the median, when judging a change.
var diedEarly = results.Count(result => result.Cleared <= 4);
var clearedAll = results.Count(result => result.Cleared >= 30);

Console.WriteLine(
    $"  shape: died-by-fight-4 {diedEarly}  middle {results.Count - diedEarly - clearedAll}  " +
    $"cleared-all {clearedAll}  (of {results.Count})");

// Why the runs ended. A change that makes fights unresolvable and a change that makes
// them lethal both lower every figure above; only this line tells them apart.
Console.WriteLine(
    "  ended: " + string.Join(
        "  ",
        Enum.GetValues<RunEnd>()
            .Select(reason => (Reason: reason, Count: results.Count(result => result.End == reason)))
            .Where(entry => entry.Count > 0)
            .Select(entry => $"{entry.Reason} {entry.Count}")));

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

static int StartLevel(string[] args)
{
    var index = Array.FindIndex(args, argument => argument is "--start-level");

    if (index >= 0 && index + 1 < args.Length)
    {
        if (int.TryParse(args[index + 1], out var level) && level is >= 1 and <= 5)
        {
            return level;
        }

        Console.Error.WriteLine($"Cannot read '{args[index + 1]}' as a level 1-5; using 1.");
    }

    return 1;
}

// The value following --seeds is not a content path, whatever it looks like.
static bool IsSeedValue(string[] args, string argument)
{
    var index = Array.IndexOf(args, argument);

    return index > 0 && args[index - 1] is "--seeds" or "--start-level";
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

/// <summary>Why a run stopped, so that defeat and an unresolvable fight stay distinct.</summary>
internal enum RunEnd
{
    /// <summary>The ladder ran out — the party survived everything put in front of it.</summary>
    Cleared,

    /// <summary>The party was defeated.</summary>
    Defeated,

    /// <summary>The policy's round limit fired: a fight that could not resolve.</summary>
    Stalled,

    /// <summary>The builder produced no monsters, which should not happen.</summary>
    NoFight,
}
