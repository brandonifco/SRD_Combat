namespace SRDCombat.Game;

/// <summary>
/// Parses the flags that shape a <c>--spawn</c> scenario alongside the roster itself
/// (#463) — today just <c>--level</c>, with room for the composition, terrain and
/// repeat-count flags Brandon has asked for next as this grows into a custom battle
/// builder. Every flag here fails the way <see cref="RosterParser"/>'s roster does: a
/// named value, the accepted range, refused rather than defaulted or clamped — a
/// silent fallback here is the same shape as a silently thinned roster (rule 2 in
/// CLAUDE.md), just for a number instead of a cast.
/// </summary>
public static class ScenarioArguments
{
    /// <summary>
    /// The party level range a spawned scenario may ask for — forwarded from
    /// <see cref="BattleScenario"/> rather than copied.
    /// </summary>
    /// <remarks>
    /// #491's second note: this band was becoming a third hard-coded copy of 1–5. The
    /// answer the battle-builder design settled on is that <see cref="BattleScenario"/>
    /// is the value every author produces and this class is one adapter onto it (#473,
    /// design §13), so the band is stated on the value and every adapter forwards. The
    /// two client-side copies are a separate concern and stay #491's.
    /// </remarks>
    public const int MinimumLevel = BattleScenario.MinimumLevel;

    /// <inheritdoc cref="MinimumLevel"/>
    public const int MaximumLevel = BattleScenario.MaximumLevel;

    /// <summary>
    /// Parses <c>--level</c>'s value for spawn mode. <paramref name="text"/> alone
    /// cannot tell "the flag was not passed" from "the flag was passed with no
    /// value" — both read as <c>null</c> from the client's own
    /// <c>ArgumentValue</c>, which only recognises the <c>--level=value</c> form
    /// (see its doc comment) — so <paramref name="present"/> carries that fact
    /// explicitly; callers pass their own <c>HasArgument("level")</c>. Not present
    /// succeeds with the default level 3 — the budgeted path's own fixed level stays
    /// #443's concern and is untouched by this helper. Present with no value (a bare
    /// <c>--level</c>, or the space form <c>--level 5</c>, which this client does not
    /// accept — see <c>ArgumentValue</c>) is refused rather than defaulted, the same
    /// as any other bad value: silently falling back to 3 here is exactly the shape
    /// #463 exists to close, just one flag over. A present value that is not a whole
    /// number, or is outside <see cref="MinimumLevel"/>–<see cref="MaximumLevel"/>,
    /// fails and names both the value typed and the accepted range; there is no
    /// fallback and no clamp anywhere in this method, because any of them would let a
    /// tester believe the fight ran at a level they never typed.
    /// </summary>
    public static bool TryParseLevel(string? text, bool present, out int level, out string? error)
    {
        if (!present)
        {
            level = 3;
            error = null;
            return true;
        }

        if (text is null)
        {
            level = default;
            error = "--level: no value given (use --level=1-5; the space form \"--level 5\" is not accepted)";
            return false;
        }

        if (!int.TryParse(text, out var parsed))
        {
            level = default;
            error = $"--level=\"{text}\": not a whole number ({MinimumLevel}-{MaximumLevel})";
            return false;
        }

        if (parsed < MinimumLevel || parsed > MaximumLevel)
        {
            level = default;
            error = $"--level={parsed}: out of range ({MinimumLevel}-{MaximumLevel})";
            return false;
        }

        level = parsed;
        error = null;
        return true;
    }
}
