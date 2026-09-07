using System.Diagnostics.CodeAnalysis;
using SRDCombat.Core.Rules;

namespace SRDCombat.Console;

/// <summary>The four ways this client's <c>Program.cs</c> can be launched.</summary>
internal enum ConsoleLaunchMode
{
    /// <summary>A single fight against the pregenerated party — <c>--one-fight</c>.</summary>
    OneFight,

    /// <summary>Resumes a persisted run — <c>--continue</c>.</summary>
    Continue,

    /// <summary>Starts a fresh run with an interactively drafted party — <c>--create</c>.</summary>
    Create,

    /// <summary>Starts a fresh run with the pregenerated party. The default when nothing else applies.</summary>
    FreshGauntlet,
}

/// <summary>
/// Which of the four launch modes <c>Program.cs</c> is in, and the values that mode
/// alone reads — the seam #624 pulled out of <c>Program.cs</c>'s top-level statements,
/// the console twin of #490's Godot gap. Before this, which mode a launch was in and
/// which flags that mode consulted were decided by the sequence of top-level statements
/// itself: nothing could call it, so a flag a mode never reached was silently dropped
/// rather than refused. <c>--one-fight</c> returned before <c>--continue</c>,
/// <c>--create</c> or <c>--save</c> were ever read, so <c>--one-fight --continue</c>
/// silently played a fresh fight and <c>--one-fight --create</c> silently played the
/// pregenerated party; <c>--continue</c> similarly never inspected <c>--create</c> (it
/// simply took priority, so <c>--continue --create</c> silently resumed rather than
/// refusing the combination) or a syntactically valid <c>--seed</c> (a malformed one was
/// still caught, because <c>TryParseSeed</c> used to run unconditionally before any mode
/// was chosen — only a valid-but-unusable seed slipped through).
/// <para>
/// <see cref="TryResolve"/> is a pure function of <c>args</c>: no I/O, no ambient
/// randomness (a fresh <c>--seed</c> is rolled by the caller, never here), same
/// refusal-by-name policy #489 already holds every flag in this project to. Every field
/// below is present regardless of mode, but only the fields that mode's own branch in
/// <c>Program.cs</c> reads are meaningful — <see cref="Seed"/> and <see cref="Level"/>
/// are unused for <see cref="ConsoleLaunchMode.Continue"/>, <see cref="Difficulty"/> is
/// unused outside <see cref="ConsoleLaunchMode.OneFight"/>, and <see cref="SavePath"/>
/// is unused for <see cref="ConsoleLaunchMode.OneFight"/> (and refused, per below, if
/// given there) — because <see cref="ConsoleArguments"/>'s own <c>TryResolve*</c>
/// functions already refuse those flags on the modes that would otherwise ignore them,
/// so any mode reaching them at all reached them legitimately.
/// </para>
/// </summary>
internal sealed record ConsoleLaunch(
    ConsoleLaunchMode Mode,
    int? Seed,
    int Level,
    EncounterDifficulty Difficulty,
    string SavePath)
{
    /// <summary>Where a run autosaves and <c>--continue</c> looks, absent <c>--save</c>.</summary>
    internal const string DefaultSavePath = "srdcombat-save.json";

    internal static bool TryResolve(
        string[] args, [NotNullWhen(true)] out ConsoleLaunch? launch, out string? error)
    {
        var oneFight = args.Contains("--one-fight");
        var continuing = args.Contains("--continue");
        var creating = args.Contains("--create");

        // Mode conflicts first, the same "does this flag even apply here" question
        // ConsoleArguments.TryResolveGauntletLevel and TryResolveDifficulty already ask
        // for --level and --difficulty — asked here for the flags that pick a mode
        // outright rather than a value within one.
        if (oneFight && continuing)
        {
            launch = null;
            error = "--continue refused: --one-fight plays a single fight against the " +
                "pregenerated party; there is no run to continue. Drop --one-fight or --continue.";
            return false;
        }

        if (oneFight && creating)
        {
            launch = null;
            error = "--create refused: --one-fight plays a single fight against the " +
                "pregenerated party, not a created one; there is no created party for it " +
                "to use instead. Drop --one-fight or --create.";
            return false;
        }

        if (oneFight && args.Contains("--save"))
        {
            launch = null;
            error = "--save refused: --one-fight has no run to save; --save only applies " +
                "to the persistent gauntlet. Drop --one-fight or --save.";
            return false;
        }

        if (continuing && creating)
        {
            launch = null;
            error = "--create refused: --continue already resumes a run; --create only " +
                "starts a fresh one. Drop --continue or --create.";
            return false;
        }

        if (!ConsoleArguments.TryResolveSeed(continuing, args, out var seed, out var seedError))
        {
            launch = null;
            error = seedError;
            return false;
        }

        if (!ConsoleArguments.TryResolveGauntletLevel(continuing, args, out var level, out var levelError))
        {
            launch = null;
            error = levelError;
            return false;
        }

        if (!ConsoleArguments.TryResolveDifficulty(oneFight, args, out var difficulty, out var difficultyError))
        {
            launch = null;
            error = difficultyError;
            return false;
        }

        var mode = oneFight ? ConsoleLaunchMode.OneFight
            : continuing ? ConsoleLaunchMode.Continue
            : creating ? ConsoleLaunchMode.Create
            : ConsoleLaunchMode.FreshGauntlet;

        launch = new ConsoleLaunch(mode, seed, level, difficulty, SavePathFrom(args) ?? DefaultSavePath);
        error = null;
        return true;
    }

    private static string? SavePathFrom(string[] args)
    {
        var index = Array.FindIndex(args, argument => argument is "--save");

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
