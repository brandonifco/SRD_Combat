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

// The gauntlet is persistent: the run autosaves after every cleared fight, and defeat
// means reload rather than reset — the file is deliberately left holding the state
// after the last fight the party won.
var savePath = SavePathFrom(args) ?? "srdcombat-save.json";

GauntletRun run;

if (ContinueRequested(args))
{
    if (!File.Exists(savePath))
    {
        Console.Error.WriteLine($"No save at '{savePath}'. Pass --save <path> or start a new run.");
        return 1;
    }

    try
    {
        run = GauntletRun.Resume(content, RunSave.FromJson(File.ReadAllText(savePath)));
    }
    catch (Exception failure) when (failure is System.Text.Json.JsonException or InvalidDataException)
    {
        // A refusal is the format explaining itself; show it rather than swallowing it.
        Console.Error.WriteLine($"Cannot load '{savePath}': {failure.Message}");
        return 1;
    }

    Console.WriteLine($"SRD_Combat — continuing after fight {run.Cleared} of {run.Ladder.Count} (seed {seed})");
}
else
{
    if (args.Contains("--create"))
    {
        // Creation runs before the run's dice: the drafts are choices, not rolls, and
        // the seed governs the fights they walk into.
        var drafts = PartyCreator.CreateParty(content);

        if (drafts is null)
        {
            Console.WriteLine("Creation abandoned; no run started.");
            return 0;
        }

        run = GauntletRun.Start(content, drafts, GauntletLadder.Default());
    }
    else
    {
        run = GauntletRun.Start(content, GauntletLadder.Default(), LevelFrom(args) ?? 1);
    }

    Console.WriteLine($"SRD_Combat — a gauntlet of {run.Ladder.Count} fights (seed {seed})");
}

Console.WriteLine($"Autosaves to '{savePath}' after each cleared fight; --continue resumes it.");
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

    // The merchant reaches the party at each Long Rest — the two per cycle that
    // bracket the milestone. The shop is the player's, never automatic here: a human
    // who walks past the stall keeps a purse that compounds.
    if (rest == RestKind.Long)
    {
        VisitShop(run);
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
    var lootBefore = run.LootFound.Count;
    var result = PlayFight(fight, random);

    if (result == FightResult.Quit)
    {
        Console.WriteLine();
        Console.WriteLine("Left the gauntlet.");
        return 0;
    }

    run.CompleteFight(fight, random);

    foreach (var levelUp in run.LevelUps.Skip(levelUpsBefore))
    {
        Console.WriteLine(levelUp + "!");
    }

    foreach (var loot in run.LootFound.Skip(lootBefore))
    {
        Console.WriteLine(loot + "!");
    }

    // Saved only after a cleared fight, never after the defeat itself — the file keeps
    // the last state worth returning to, which is what makes reloading a retry.
    if (run.Outcome != RunOutcome.Defeated)
    {
        File.WriteAllText(savePath, RunSave.ToJson(run));
    }
}

Console.WriteLine();
Console.WriteLine(run.Outcome == RunOutcome.Survived
    ? $"The gauntlet is beaten — {run.Ladder.Count} fights cleared."
    : $"The run ends after {run.Cleared} fight(s).");

if (run.Outcome == RunOutcome.Defeated && run.Cleared > 0)
{
    Console.WriteLine($"Run --continue to retry from after fight {run.Cleared}.");
}

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

// The Long Rest merchant: numbered offers, buy by number, done to move on. Refusals
// are shown with their code like every other engine answer.
void VisitShop(GauntletRun run)
{
    while (true)
    {
        var offers = Shop.Offers(content, run.Party, run.States);

        Console.WriteLine();
        Console.WriteLine($"A merchant is here. The purse holds {Shop.Price(run.GoldCopper)}.");

        if (offers.Count == 0)
        {
            Console.WriteLine("Nothing here would improve anybody; the party moves on.");
            return;
        }

        for (var i = 0; i < offers.Count; i++)
        {
            var affordable = offers[i].CostCopper <= run.GoldCopper ? "  " : "* ";
            Console.WriteLine($"  {affordable}{i + 1}. {offers[i].Description}");

            // What the price actually buys, under the offer it belongs to. The lines
            // come from the offer rather than being built here, so the mouse client
            // and this one cannot come to different conclusions about the same trade.
            foreach (var effect in offers[i].Effect.Lines)
            {
                Console.WriteLine($"        {effect}");
            }
        }

        Console.WriteLine("(* beyond the purse)  buy <number>, or done:");
        Console.Write("> ");

        var line = Console.ReadLine()?.Trim() ?? "done";

        if (line.Length == 0 || line.Equals("done", StringComparison.OrdinalIgnoreCase)
            || line.Equals("d", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numberWord = words.Length > 1 && words[0].Equals("buy", StringComparison.OrdinalIgnoreCase)
            ? words[1]
            : words[0];

        if (!int.TryParse(numberWord, out var picked) || picked < 1 || picked > offers.Count)
        {
            Console.WriteLine($"'{line}' is not an offer here — a number 1..{offers.Count}, or done.");
            continue;
        }

        if (run.Purchase(offers[picked - 1]) is { } refusal)
        {
            Console.WriteLine($"[{refusal.Code}] {refusal.Message}");
            continue;
        }

        Console.WriteLine($"Bought: {offers[picked - 1].Description}.");
    }
}

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

static bool ContinueRequested(string[] args) => args.Contains("--continue");

static string? SavePathFrom(string[] args)
{
    var index = Array.FindIndex(args, argument => argument is "--save");

    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
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
    string[] takesAValue = ["--seed", "--level", "--difficulty", "--save"];

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
