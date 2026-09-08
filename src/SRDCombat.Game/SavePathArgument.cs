namespace SRDCombat.Game;

/// <summary>
/// Resolves the Godot client's <c>--save</c> flag to a path, uniformly across all four
/// of <c>PlayMode</c>'s own launch modes (#654) — a bare <c>--save</c> (present, no
/// value) is refused by name rather than silently falling back to
/// <see cref="DefaultPath"/>, the same present-but-valueless policy #489 already holds
/// every other flag in this project to (<see cref="ScenarioArguments.TryParseLevel"/>
/// and <see cref="ScenarioComposition.TryParseDifficulty"/> are the model).
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision lives here; the client only calls and shows.</b> Before this,
/// <c>PlayMode.OnReady</c> read <c>--save</c> with <c>ArgumentValue("save") ??
/// "srdcombat-save.json"</c> — the <c>??</c> cannot distinguish "the flag was never
/// passed" from "the flag was passed with nothing after it", so a bare <c>--save</c>
/// silently wrote autosaves to the default path instead of naming the typo. That default
/// was also read only after the gauntlet's own <c>--one-fight</c> branch had already
/// returned, so a bare <c>--save --one-fight</c> was never even inspected. This method is
/// called once, before any mode branch, so the refusal fires the same way regardless of
/// which of the four launch modes reaches it — the same reasoning
/// <c>FightScreen.SeedArgument</c>'s own doc comment gives for resolving <c>--seed</c>
/// first. <c>--save</c> happens to have no effect at all under <c>--one-fight</c> (there
/// is no run to save — <c>PlayMode.Run.cs</c>'s autosave and <c>--continue</c>'s load both
/// key off <c>_run</c>, which a one-fight launch never sets) but a typo'd flag is worth
/// naming on every launch, not only the one where it would have mattered.
/// </para>
/// <para>
/// This is deliberately narrower than the console's <c>ConsoleLaunch</c> seam, which also
/// refuses <c>--save</c> outright when combined with <c>--one-fight</c> (there being
/// nothing to save at all): that is a different question — whether the flag applies in a
/// mode, not whether its value is well-formed — and #654's scope is the bare-value gap
/// alone.
/// </para>
/// </remarks>
public static class SavePathArgument
{
    /// <summary>Where a run autosaves and <c>--continue</c> looks, absent <c>--save</c>.</summary>
    public const string DefaultPath = "srdcombat-save.json";

    /// <summary>
    /// Not present succeeds with <see cref="DefaultPath"/>. Present with no value (a bare
    /// <c>--save</c>) is refused rather than defaulted. Present with a value succeeds with
    /// that value verbatim — no existence check and no normalisation here; <c>SaveFile</c>
    /// decides whether the path is usable when it is actually read or written.
    /// </summary>
    public static bool TryResolve(FlagValue save, out string savePath, out string? error)
    {
        if (!save.Present)
        {
            savePath = DefaultPath;
            error = null;
            return true;
        }

        if (save.Value is null)
        {
            savePath = string.Empty;
            error = "--save: no value given (use --save=<path>)";
            return false;
        }

        savePath = save.Value;
        error = null;
        return true;
    }
}
