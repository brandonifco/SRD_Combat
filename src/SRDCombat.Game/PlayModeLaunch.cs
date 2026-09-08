using System.Diagnostics.CodeAnalysis;

namespace SRDCombat.Game;

/// <summary>The three ways the Godot client's <c>PlayMode</c> can be launched.</summary>
/// <remarks>
/// There is no <c>Create</c> mode here, unlike the console's
/// <c>SRDCombat.Console.ConsoleLaunch</c> twin: the Godot client drafts a party in
/// a separate <c>CreateMode</c> node that hands the drafts to <c>PlayMode</c> through its
/// <c>CreatedDrafts</c> init property, so "a created party" is a client-side field rather
/// than a flag <c>PlayMode.OnReady</c> ever reads. A fresh run therefore resolves to
/// <see cref="FreshGauntlet"/> whether it will field the pregens or created drafts, and the
/// created-vs-pregen choice stays where the field is — in the client.
/// </remarks>
public enum PlayModeLaunchMode
{
    /// <summary>A single fight against the party — <c>--one-fight</c>.</summary>
    OneFight,

    /// <summary>Resumes a persisted run — <c>--continue</c>.</summary>
    Continue,

    /// <summary>Starts a fresh run (pregens or created drafts). The default when nothing else applies.</summary>
    FreshGauntlet,
}

/// <summary>
/// Which of <c>PlayMode</c>'s launch modes a Godot launch is in, and the pure-decision
/// values that mode reads — the Godot twin of the console's
/// <c>SRDCombat.Console.ConsoleLaunch</c> (#624), folding the two separate
/// resolve-and-branch blocks <c>PlayMode.OnReady</c> ran after the seed
/// (<see cref="SavePathArgument.TryResolve"/> then <see cref="GauntletStart.Resolve"/>)
/// and the one-fight/continue/fresh mode selection into one callable, directly testable
/// result (#491).
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision lives here; the client only calls, then does the I/O the chosen mode
/// needs.</b> Before this, <c>PlayMode.OnReady</c> called
/// <see cref="SavePathArgument.TryResolve"/> and <see cref="GauntletStart.Resolve"/> as two
/// separate blocks, each with its own <c>_phase = RunOver; _interlude.Add(error);
/// _subtitle = $"seed {_seed}"; return;</c> handling, and then re-read
/// <c>HasArgument("one-fight")</c>/<c>HasArgument("continue")</c> a second time to pick the
/// branch. Those decisions are the same shape #490a/#490b already moved into <c>Game</c>
/// for a plain xUnit test to pin; this folds their <em>composition</em> — the order they
/// run in, which refusal wins when two flags are wrong at once, and the resulting mode —
/// into one function so the composition itself is pinned rather than re-expressed in the
/// live node. The client keeps every piece of I/O and ambient state: content loading, save
/// loading, run construction, and the probe pace side-effects all stay in
/// <c>OnReady</c>, exactly as <c>Program.cs</c> keeps its own after
/// <c>SRDCombat.Console.ConsoleLaunch.TryResolve</c>.
/// </para>
/// <para>
/// <b>Why the seed is not folded in.</b> The console folds <c>--seed</c> into
/// <c>SRDCombat.Console.ConsoleLaunch</c>, but the Godot client cannot: its
/// seed resolver (<c>FightScreen.TryResolveSeed</c>) lives in the client assembly, which
/// <c>Game</c> does not reference, and its default when <c>--seed</c> is absent is an
/// ambient, probe/capture-aware roll (<c>Random.Shared.Next()</c> or a fixed capture
/// seed) that a pure <c>Game</c> function must not make. So the seed stays
/// <c>OnReady</c>'s own first step, resolved (and its refusal shown as
/// <c>"seed refused"</c> with no seed number) before this runs — which is exactly why
/// <see cref="TryResolve"/> below takes no seed and refuses nothing about it. The gate
/// order the client sees is unchanged: seed first, then save, then the gauntlet-start
/// gates, then the mode.
/// </para>
/// <para>
/// <see cref="TryResolve"/> is a pure function of its <see cref="FlagValue"/> and
/// <c>bool</c> inputs — the client's <c>--flag</c> vs. <c>--flag=value</c> dialect is
/// already resolved into <see cref="FlagValue"/>s before it is called, the same crossing
/// point <see cref="ScenarioComposition"/> and <see cref="GauntletStart"/> take (their own
/// remarks say why the dialect stays client-side). Every field below is present regardless
/// of mode, but only the fields the chosen mode's branch in <c>OnReady</c> reads are
/// meaningful — <see cref="Level"/> is the fresh gauntlet's starting level and a harmless
/// placeholder for <see cref="PlayModeLaunchMode.OneFight"/> and
/// <see cref="PlayModeLaunchMode.Continue"/> (exactly as <see cref="GauntletStart.Result"/>
/// already describes for its own <c>Level</c>).
/// </para>
/// </remarks>
public sealed record PlayModeLaunch(
    PlayModeLaunchMode Mode,
    string SavePath,
    int Level)
{
    /// <summary>
    /// Resolves the save path, the gauntlet-start gates and the launch mode in the same
    /// order <c>PlayMode.OnReady</c> checked them — <see cref="SavePathArgument.TryResolve"/>
    /// first, so a bare <c>--save</c> wins over a bad gauntlet flag exactly as it did when
    /// the save block returned before the gauntlet block ran, then
    /// <see cref="GauntletStart.Resolve"/>, then the mode. On any refusal
    /// <paramref name="launch"/> is null and <paramref name="error"/> is the refusing
    /// seam's verbatim wording, for the caller to show with a <c>seed {_seed}</c> subtitle
    /// the same way it always has; on success <paramref name="error"/> is null and
    /// <paramref name="launch"/> carries the mode, the resolved save path and the
    /// gauntlet-start level.
    /// </summary>
    public static bool TryResolve(
        FlagValue save,
        FlagValue spawn,
        FlagValue scenario,
        bool oneFight,
        bool continuing,
        FlagValue level,
        FlagValue difficulty,
        [NotNullWhen(true)] out PlayModeLaunch? launch,
        out string? error)
    {
        if (!SavePathArgument.TryResolve(save, out var savePath, out var saveError))
        {
            launch = null;
            error = saveError;
            return false;
        }

        var gauntletStart = GauntletStart.Resolve(spawn, scenario, oneFight, continuing, level, difficulty);

        if (gauntletStart.Refusal is not null)
        {
            launch = null;
            error = gauntletStart.Refusal;
            return false;
        }

        var mode = oneFight ? PlayModeLaunchMode.OneFight
            : continuing ? PlayModeLaunchMode.Continue
            : PlayModeLaunchMode.FreshGauntlet;

        launch = new PlayModeLaunch(mode, savePath, gauntletStart.Level);
        error = null;
        return true;
    }
}
