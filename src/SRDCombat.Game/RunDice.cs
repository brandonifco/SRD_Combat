namespace SRDCombat.Game;

/// <summary>
/// Derives the dice a single fight plays on from a run's seed and how many fights it
/// has already cleared — the boundary a resumed run reseeds at.
/// </summary>
/// <remarks>
/// <para>
/// <b>The boundary, defined.</b> A "fight" here is everything between one call to
/// <see cref="GauntletRun.PrepareForNext"/> and the matching <see cref="GauntletRun.CompleteFight"/>
/// — the rest taken, the Long Rest shop if one opens, <see cref="GauntletRun.BeginNext"/>'s
/// encounter draw, the fight itself, and the loot or potion it pays out. Every driver
/// (the console client, the Godot client, <c>tools/PacingMeasure</c>) reseeds its
/// <c>IRandomSource</c> to <see cref="SeedFor"/> exactly once, at the top of that
/// span, before <c>PrepareForNext</c> — never in the middle of it. Everything inside
/// one span draws from that one seeded stream in order, so nothing inside it is
/// independently reproducible; only the span as a whole is.
/// </para>
/// <para>
/// <b>The deliberate reading, stated once here rather than argued at every call
/// site</b> (the same reason <c>AreaTargeting</c>'s judgement calls live in its own
/// doc comment): retrying a fight — after a defeat, or a plain <c>--continue</c> —
/// does not merely draw the same monsters on the same battlefield. It re-plays the
/// exact same dice for the exact same sequence of engine calls, because
/// <see cref="SeedFor"/> is a pure function of the run's own seed and how many fights
/// came before this one, indifferent to how much dice a differently-played earlier
/// attempt at <em>this same fight</em> spent, or how many rounds it ran, or what the
/// player chose. A retry that made the identical choices from here would see the
/// identical fight unfold, blow for blow — which is what "the same fight" promises
/// a player who wants to learn a fight rather than gamble on a new one, and what
/// makes a bug report of "seed 12345, fight 7" complete on its own: <em>within one
/// run</em>, (seed, fight number) reproduces the fight regardless of the play history
/// that got there, because nothing about a game's own actions consumes dice from a
/// later fight's span.
/// </para>
/// <para>
/// <b>Not <see cref="HashCode.Combine{T1, T2}"/> and not a string hash.</b> Neither
/// is specified to be stable across .NET versions — <c>HashCode</c>'s own
/// documentation says its output may change between releases, which would silently
/// break every save's reproducibility on a runtime upgrade. This is a fixed
/// SplitMix64 finalizer: the same 64-bit mixing step SplitMix64 and PCG use to turn a
/// counter into a well-distributed value, run over the run's seed and the fight index
/// packed into one 64-bit word, folded down to the 32 bits <c>SeededRandomSource</c>
/// takes. It is arithmetic, not a library call, so its output is exactly this method
/// forever.
/// </para>
/// </remarks>
public static class RunDice
{
    /// <summary>
    /// The seed the fight at <paramref name="cleared"/> plays on, within the run
    /// seeded <paramref name="runSeed"/>.
    /// </summary>
    /// <param name="runSeed"><see cref="GauntletRun.Seed"/> — fixed for the whole run.</param>
    /// <param name="cleared">
    /// <see cref="GauntletRun.Cleared"/> at the moment the fight begins — the number
    /// of fights already won, which is also this fight's index on the ladder.
    /// </param>
    public static int SeedFor(int runSeed, int cleared)
    {
        var z = ((ulong)(uint)runSeed << 32) | (uint)cleared;

        z ^= z >> 30;
        z *= 0xBF58476D1CE4E5B9UL;
        z ^= z >> 27;
        z *= 0x94D049BB133111EBUL;
        z ^= z >> 31;

        return unchecked((int)(z ^ (z >> 32)));
    }
}
