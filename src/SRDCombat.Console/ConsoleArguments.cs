using SRDCombat.Core.Rules;

namespace SRDCombat.Console;

/// <summary>
/// Parses <c>--level</c>, <c>--seed</c> and <c>--difficulty</c> for this client's own
/// dialect — a separate value word (<c>--level 5</c>), as opposed to the Godot client's
/// single-word <c>--level=5</c> (<see cref="SRDCombat.Game.ScenarioArguments"/>'s own
/// remarks are why the two clients never share one parser). Same policy as that class's
/// <c>TryParseLevel</c> and the Godot client's <c>FightScreen.TryParseSeed</c> (#489): a
/// flag present with a value nothing here can use is refused by name, value and accepted
/// set — never defaulted, never clamped. Absent, each keeps the default it always had.
/// </summary>
internal static class ConsoleArguments
{
    /// <summary>The party level a single fight or a fresh gauntlet begins at, default 1.</summary>
    internal static bool TryParseLevel(string[] args, out int level, out string? error)
    {
        var index = Array.FindIndex(args, argument => argument is "--level");

        if (index < 0)
        {
            level = 1;
            error = null;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            level = default;
            error = "--level: no value given (use --level <n>, 1-5)";
            return false;
        }

        var text = args[index + 1];

        if (!int.TryParse(text, out var parsed))
        {
            level = default;
            error = $"--level {text}: not a whole number (1-5)";
            return false;
        }

        if (parsed is < 1 or > 5)
        {
            level = default;
            error = $"--level {parsed}: out of range (1-5)";
            return false;
        }

        level = parsed;
        error = null;
        return true;
    }

    /// <summary>The run's seed. Absent is <c>null</c> — the caller rolls a fresh one.</summary>
    internal static bool TryParseSeed(string[] args, out int? seed, out string? error)
    {
        var index = Array.FindIndex(args, argument => argument is "--seed");

        if (index < 0)
        {
            seed = null;
            error = null;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            seed = null;
            error = "--seed: no value given (use --seed <n>)";
            return false;
        }

        var text = args[index + 1];

        if (!int.TryParse(text, out var parsed))
        {
            seed = null;
            error = $"--seed {text}: not a whole number";
            return false;
        }

        seed = parsed;
        error = null;
        return true;
    }

    /// <summary>The single-fight path's difficulty, default Low.</summary>
    internal static bool TryParseDifficulty(string[] args, out EncounterDifficulty difficulty, out string? error)
    {
        var index = Array.FindIndex(args, argument => argument is "--difficulty");

        if (index < 0)
        {
            // "one or two scary moments ... their characters should emerge victorious" is
            // the right default for sitting down cold.
            difficulty = EncounterDifficulty.Low;
            error = null;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            difficulty = default;
            error = "--difficulty: no value given (use --difficulty low|moderate|high)";
            return false;
        }

        var text = args[index + 1];

        if (!Enum.TryParse<EncounterDifficulty>(text, ignoreCase: true, out var parsed))
        {
            difficulty = default;
            error = $"--difficulty {text}: not one of low, moderate, high";
            return false;
        }

        difficulty = parsed;
        error = null;
        return true;
    }
}
