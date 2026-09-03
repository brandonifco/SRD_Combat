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
/// <see cref="TryResolveGauntletLevel"/> is this client's own
/// <c>PlayMode.TryResolveGauntletLevel</c> twin (#602): whether <c>--level</c> applies at
/// all — refused against <c>--continue</c>, forwarded into <c>--create</c>'s drafts the
/// same as a pregenerated party — is a question <see cref="TryParseLevel"/> alone never
/// answered, which is how #488's Godot bug (a silently dropped <c>--level</c> on a
/// created party) had a console-side twin nothing caught. <see cref="TryResolveDifficulty"/>
/// answers the same "does this flag apply here" question for <c>--difficulty</c> (#605):
/// it only ever governed <c>--one-fight</c>, but <c>Program.cs</c> used to call
/// <see cref="TryParseDifficulty"/> only inside that branch, so a <c>--difficulty</c>
/// passed on the ordinary gauntlet path — valid or not — was never read at all.
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

    /// <summary>
    /// The level a fresh gauntlet run begins at, deciding first whether <c>--level</c>
    /// applies at all (#602, the console twin of <c>PlayMode.TryResolveGauntletLevel</c>
    /// closing #488 on the Godot side). A resumed run has nothing for <c>--level</c> to
    /// apply to — <see cref="SRDCombat.Game.GauntletRun.Resume"/> re-resolves at the
    /// level the save's own experience has earned — so <c>--continue --level</c> is
    /// refused rather than silently ignored, the same shape #489 already held every
    /// other flag here to. Absent, a fresh run keeps its old default of level 1;
    /// present, <see cref="TryParseLevel"/> does the actual parse and range check.
    /// </summary>
    internal static bool TryResolveGauntletLevel(bool continuing, string[] args, out int level, out string? error)
    {
        var levelGiven = args.Contains("--level");

        if (continuing && levelGiven)
        {
            level = default;
            error = "--level refused: --continue resumes at the level the save's own " +
                "experience has earned; --level does not apply here. Start a new run to choose one.";
            return false;
        }

        if (!levelGiven)
        {
            level = 1;
            error = null;
            return true;
        }

        return TryParseLevel(args, out level, out error);
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

        // Enum.TryParse is deliberately not used here — it accepts far more than a
        // single declared name. Numeric text parses too ("3" becomes the undefined
        // value 3, which throws downstream at the engine rather than refusing here;
        // "0" silently parses as Low), and so does a comma-separated name list read
        // as a bitwise-OR combination — "low,high" parses to the defined value High
        // even though nobody typed a single name (#602). Matching the trimmed text
        // directly against the declared name set is the only way to accept exactly
        // one of them and refuse everything else, numeric, combined or malformed
        // alike.
        EncounterDifficulty? match = null;

        foreach (var value in Enum.GetValues<EncounterDifficulty>())
        {
            if (string.Equals(value.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                match = value;
                break;
            }
        }

        if (match is not { } parsed)
        {
            difficulty = default;
            error = $"--difficulty {text}: not one of low, moderate, high";
            return false;
        }

        difficulty = parsed;
        error = null;
        return true;
    }

    /// <summary>
    /// Whether <c>--difficulty</c> applies at all, mirroring <see cref="TryResolveGauntletLevel"/>'s
    /// shape: only <c>--one-fight</c> has a single difficulty to choose — the gauntlet's
    /// ladder sets its own difficulty per rung — so <c>--difficulty</c> given on the
    /// ordinary gauntlet path used to be read by nothing at all and silently ignored,
    /// rather than refused (a Codex finding on #605, the same shape #489 already closed
    /// for every other flag here). Absent on the gauntlet path, the value is unused and
    /// stays the Low default; present there, it is refused by name rather than dropped.
    /// On the one-fight path this simply forwards to <see cref="TryParseDifficulty"/>,
    /// which does the actual parse and name check.
    /// </summary>
    internal static bool TryResolveDifficulty(bool oneFight, string[] args, out EncounterDifficulty difficulty, out string? error)
    {
        if (!oneFight && args.Contains("--difficulty"))
        {
            difficulty = default;
            error = "--difficulty refused: only --one-fight has a single difficulty to " +
                "choose; the gauntlet's ladder sets its own per rung. Pass --one-fight " +
                "--difficulty low|moderate|high.";
            return false;
        }

        if (!oneFight)
        {
            difficulty = EncounterDifficulty.Low;
            error = null;
            return true;
        }

        return TryParseDifficulty(args, out difficulty, out error);
    }
}
