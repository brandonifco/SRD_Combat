namespace SRDCombat.Game;

/// <summary>
/// Decides what starting the gauntlet (as opposed to a one-fight scenario) means for
/// <c>--spawn</c>/<c>--scenario</c>/<c>--level</c>/<c>--difficulty</c>, and assembles the
/// client-facing refusal text when it means nothing runnable (#490, #443).
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision and the wording live here; a client only calls and shows.</b>
/// Before this, <c>PlayMode.OnReady</c> held three separate gates inline: the
/// <c>--spawn</c>/<c>--scenario</c>-without-<c>--one-fight</c> refusals (the gauntlet
/// loop draws its own roster every fight and never reads either flag — #463, #476) and
/// <c>PlayMode.TryResolveGauntletLevel</c>'s <c>--continue</c>/<c>--level</c>
/// interaction and level parse (#488). All three are reachable only from a
/// Godot-hosted test or a probe capture, and all three are tested decisions rather
/// than incidental wiring, so they belong where a plain xUnit test can pin them —
/// <see cref="ScenarioComposition"/> (#490a) is the model this follows: compute in
/// <c>Game</c>, render in the client. Since #491 the client no longer calls this
/// directly: <see cref="PlayModeLaunch.TryResolve"/> composes it with
/// <see cref="SavePathArgument.TryResolve"/> and the mode selection into one result, so
/// the order these gates run in is pinned by a test rather than by <c>OnReady</c>'s
/// statement sequence.
/// </para>
/// <para>
/// <b>This PR (#490b) is the gauntlet-start half</b> — the authoring/one-fight half
/// (<c>--one-fight</c>, <c>--watch</c>'s <c>--scenario</c> vs. <c>--spawn</c>
/// composition) is #490a's <see cref="ScenarioComposition"/> and is untouched here.
/// </para>
/// <para>
/// <b>#443's own difficulty fix left this half open</b> (a Codex adversarial-review
/// finding): #443 taught <see cref="ScenarioComposition"/> to honour
/// <c>--difficulty</c> for the flagless budgeted <c>--one-fight</c> fight, but the
/// gauntlet path never read <paramref name="difficulty"/> at all — a
/// <c>--difficulty=high</c>, a typo'd value, or a bare <c>--difficulty</c> on an
/// ordinary gauntlet launch was silently read by nothing, the exact "present flag
/// dropped" shape #489 already closed for every other flag here. The gauntlet
/// escalates its own difficulty per rung and has no single difficulty for the flag to
/// set, so <c>--difficulty</c> outside <c>--one-fight</c> is refused here the same way
/// <c>--spawn</c>/<c>--scenario</c> already are, rather than silently ignored.
/// </para>
/// </remarks>
public static class GauntletStart
{
    /// <summary>
    /// The gauntlet-start decision: the level a fresh run begins at, or the
    /// fully-assembled reason nothing starts. <see cref="Refusal"/> null means
    /// <see cref="Level"/> is the level to start (or resume) at — for a
    /// <c>--one-fight</c> run, or a <c>--continue</c>d one, nothing here reads
    /// <see cref="Level"/> and its value is a harmless placeholder, exactly as
    /// <c>PlayMode.TryResolveGauntletLevel</c>'s own remarks describe for the
    /// equivalent case.
    /// </summary>
    public sealed record Result(int Level, string? Refusal);

    /// <summary>
    /// Resolves the gauntlet-start gates in the same order <c>PlayMode.OnReady</c> and
    /// <c>PlayMode.TryResolveGauntletLevel</c> checked them in, with the same wording:
    /// <c>--spawn</c> or <c>--scenario</c> without <c>--one-fight</c> refuses first
    /// (the gauntlet loop never reads either), then <c>--difficulty</c> without
    /// <c>--one-fight</c> refuses the same way (the gauntlet escalates its own
    /// difficulty every rung and never reads the flag either — #443), then — only when
    /// the gauntlet is actually starting, i.e. <paramref name="oneFight"/> is false —
    /// <c>--continue</c> together with <c>--level</c> refuses (a resumed run
    /// re-resolves at the level its own experience earned), then a bare or
    /// out-of-range <c>--level</c> refuses via <see cref="ScenarioArguments.TryParseLevel"/>,
    /// and an absent <c>--level</c> defaults to 1 — the gauntlet's own default, not
    /// <see cref="ScenarioArguments"/>'s spawn-mode default of 3 (its remarks say why).
    /// </summary>
    /// <remarks>
    /// <paramref name="oneFight"/> true short-circuits before either the
    /// <c>--continue</c>/<c>--level</c> check or the level parse — a one-fight run
    /// never reaches the gauntlet's own <c>--level</c> handling today (it is resolved
    /// through <see cref="ScenarioComposition"/> instead), so <c>--one-fight
    /// --continue --level=4</c> is silently ignored exactly as it always was, not newly
    /// refused. That silence is #490a/#488's existing scope, not this method's to
    /// close. The same short-circuit means a <c>--one-fight</c> run's own
    /// <c>--difficulty</c> is never even inspected here — whether it is honoured or
    /// refused is entirely <see cref="ScenarioComposition.Compose"/>'s decision (#443).
    /// </remarks>
    public static Result Resolve(
        FlagValue spawn, FlagValue scenario, bool oneFight, bool continuing, FlagValue level,
        FlagValue difficulty = default)
    {
        if (spawn.Present && !oneFight)
        {
            return new Result(
                Level: default,
                Refusal: "--spawn refused: the gauntlet does not read it — it draws its own roster " +
                    "every fight. Pass --one-fight (or run with --watch) to field a chosen cast.");
        }

        if (scenario.Present && !oneFight)
        {
            return new Result(
                Level: default,
                Refusal: "--scenario refused: the gauntlet does not read it — it draws its own roster " +
                    "every fight. Pass --one-fight (or run with --watch) to play it.");
        }

        if (difficulty.Present && !oneFight)
        {
            return new Result(
                Level: default,
                Refusal: "--difficulty refused: the gauntlet does not read it — it escalates its own " +
                    "difficulty every rung. Pass --one-fight to choose a single fight's difficulty.");
        }

        if (oneFight)
        {
            return new Result(Level: default, Refusal: null);
        }

        if (continuing && level.Present)
        {
            return new Result(
                Level: default,
                Refusal: "--level refused: --continue resumes at the level the save's own " +
                    "experience has earned; --level does not apply here. Start a new run to choose one.");
        }

        if (!level.Present)
        {
            return new Result(Level: 1, Refusal: null);
        }

        if (!ScenarioArguments.TryParseLevel(level.Value, present: true, out var parsed, out var levelError))
        {
            return new Result(Level: default, Refusal: $"--level refused: {levelError}");
        }

        return new Result(Level: parsed, Refusal: null);
    }
}
