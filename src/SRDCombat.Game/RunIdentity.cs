using RulesKernel.Identity;
using SRDCombat.Content;

namespace SRDCombat.Game;

/// <summary>
/// What makes two runs of this engine comparable: which revision of these rules resolved
/// them, which content roster they were resolved against, and which generator supplied
/// their dice.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a shared vocabulary rather than three more local constants.</b> The
/// parts were already here under other names and in other places — <c>RunDice</c> derives
/// the dice, <see cref="SrdContent.ContentFingerprint"/> pins the roster,
/// <see cref="SavedRun.Seed"/> stores the seed — and one part was missing entirely:
/// nothing recorded which revision of the engine's own rules produced a run. The kernel's
/// <see cref="ReplayCompatibilityIdentity"/> is the shape a bug report or a replay bundle
/// (#722) needs, and adopting it is nearly free while nothing has been written to disk in
/// it. After the first stored bundle it would be a save-migration.
/// </para>
/// <para>
/// <b>Nothing here gates anything.</b> No load path compares an identity, and no refusal
/// rests on one. It is provenance — the same standing this project already gives
/// <see cref="SavedRun.ContentVersion"/>, whose own remarks record that per-id resolution,
/// not a coarse fingerprint, is the real gate.
/// </para>
/// </remarks>
public static class RunIdentity
{
    /// <summary>This engine's ruleset identifier, distinct from the corpus it reads.</summary>
    /// <remarks>
    /// Deliberately not <c>srd-5.2.1</c>: that names the printed source, which
    /// <see cref="SrdSourceId"/> carries. This names the implementation — the subset of
    /// those rules this engine executes, plus every reading it had to make where the
    /// print was a judgement call. The two move independently, which is the whole reason
    /// the kernel keeps <see cref="RulesetVersion"/> and <see cref="SourceBaselineId"/>
    /// apart.
    /// </remarks>
    public const string RulesetId = "srd-combat";

    /// <summary>
    /// This engine's implemented-rules revision. <c>1</c> is its introduction; nothing was
    /// ever recorded against an earlier value, because until this constant existed nothing
    /// recorded a rules revision at all.
    /// </summary>
    /// <remarks>
    /// <b>What bumps it is not decided here, and this constant does not pretend it is.</b>
    /// The kernel's contract for the field is that a bump is expected whenever a change
    /// could alter how a recorded decision resolves, which at this project's rate of rules
    /// change is a large share of gameplay pull requests; whether that is the convention
    /// this project wants, and what enforces it, is a decision the project has not taken.
    /// Until it does, this value means "the rules as of this constant's introduction" and
    /// nothing more — strictly more than the nothing that preceded it, and strictly less
    /// than a reader may assume from a version number. Do not read a stable value here as
    /// evidence that the rules did not move.
    /// </remarks>
    public const int RulesetRevision = 1;

    /// <summary>The printed corpus every rule in this engine is derived from.</summary>
    /// <remarks>
    /// A fixed printing, not a revised corpus: the revision is in the name, and a corrected
    /// SRD is published as a new numbered document rather than as edits to this one. So the
    /// baseline carries no <see cref="SourceBaselineId.AsOf"/> — absent means "this corpus
    /// has no temporal dimension", which is the true statement here, and is not the same as
    /// an unknown date.
    /// </remarks>
    public const string SrdSourceId = "srd-5.2.1";

    /// <summary>
    /// What <see cref="SrdContent.ContentFingerprint"/> is a hash <em>of</em>, in the
    /// kernel's <see cref="SourceBaselineId.HashDerivation"/> slot.
    /// </summary>
    /// <remarks>
    /// The fingerprint hashes the sorted roster of content ids and nothing else — its own
    /// remarks say so: "by id alone, not by the numbers behind them." Two builds whose
    /// rosters match fingerprint identically even where a stat block changed underneath, so
    /// a baseline claiming to pin the corpus's <em>content</em> would be claiming more than
    /// this hash supports. Naming the derivation is what keeps that honest.
    /// </remarks>
    public const string ContentFingerprintDerivation = "id-roster";

    /// <summary>
    /// The replay-record shape's revision. <c>0</c> because this project records no replays:
    /// #722 is open and nothing writes a bundle.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> wired to <see cref="RunSave.CurrentFormatVersion"/>,
    /// <c>ContentSerializer.CurrentFormatVersion</c> or
    /// <see cref="ScenarioFile.CurrentFormatVersion"/>. Each of those versions a file this
    /// project actually writes, and a replay bundle is not any of them; borrowing one would
    /// make a save-format bump silently claim the replay shape had changed, and vice versa.
    /// The first bundle that reaches disk is what moves this to <c>1</c>.
    /// </remarks>
    public const int ReplaySchemaRevision = 0;

    /// <summary>
    /// The generator this engine's dice come from — <c>System.Random</c>, seeded per fight
    /// by <see cref="RunDice.SeedFor"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named rather than omitted, and the name is the point.</b> The kernel treats an
    /// absent algorithm as the claim "this engine consumes no randomness"
    /// (<see cref="ReplayCompatibilityIdentity.IsDeterministicWithoutRandomness"/>), which
    /// would be flatly false here. So leaving the slot empty is not the cautious option; it
    /// is the wrong one.
    /// </para>
    /// <para>
    /// <b>What the name does not claim.</b> <c>System.Random</c>'s seeded sequence is stable
    /// in practice and has never been documented as a contract, which is why the kernel
    /// ships PCG32 instead of blessing it. This engine still uses <c>System.Random</c>
    /// (<c>Core/Dice</c>), and changing that is a separate decision with its own cost. What
    /// this constant buys meanwhile is narrower than it may look, and worth stating
    /// exactly: if the generator is ever <em>replaced</em>, an identity carrying a different
    /// name is visibly a different identity rather than a silently different one.
    /// <para>
    /// It buys nothing against the risk it names. This is a compile-time string; a runtime
    /// that changed seeded <c>System.Random</c>'s sequence would produce a byte-identical
    /// identity for a run that no longer replays — the silent divergence
    /// <see cref="RandomAlgorithmId"/> exists to make visible. The name identifies an API
    /// surface, not an algorithm, and there is no reference implementation for it to be
    /// named after. Nothing here detects a runtime-level sequence change, and nothing can.
    /// </para>
    /// <para>
    /// The kernel's decision 0005 lists exactly this combination — "keep
    /// <c>System.Random</c> with a recorded algorithm id" — among its rejected
    /// alternatives, on the ground that recording the identity of a generator whose
    /// behaviour is not contractually stable records the wrong thing. That is a ruling on
    /// what the <em>kernel</em> ships, and it does not bind an engine that has not migrated:
    /// the alternative here is not "record nothing" but "record that this engine consumes
    /// no randomness", which is what an absent algorithm asserts and is flatly false. Of the
    /// two available, naming it is the honest one.
    /// </para>
    /// <para>
    /// The seeding routine is not named here — per decision 0005 an engine's own seed
    /// derivation belongs to its <see cref="RulesetVersion"/>, and
    /// <see cref="RunDice.SeedFor"/> is exactly such a derivation.
    /// </para>
    /// </para>
    /// </remarks>
    public static readonly RandomAlgorithmId DiceAlgorithm = new("system-random-seeded");

    /// <summary>This engine's ruleset and its revision.</summary>
    public static RulesetVersion Ruleset { get; } = new(RulesetId, RulesetRevision);

    /// <summary>
    /// The SRD corpus baseline for a loaded content build: the existing whole-roster
    /// fingerprint, said in the kernel's vocabulary.
    /// </summary>
    /// <remarks>
    /// <see cref="SrdContent.ContentFingerprint"/> is upper-case hex
    /// (<c>Convert.ToHexString</c>) and <see cref="SourceBaselineId"/> lower-cases what it
    /// is given, so a baseline's <see cref="SourceBaselineId.ContentHash"/> is never
    /// ordinally equal to the string a save stores in
    /// <see cref="SavedRun.ContentVersion"/>. Anything comparing the two must fold case;
    /// <c>RunIdentityTests</c> pins it so the first such comparison does not discover it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static SourceBaselineId BaselineFor(SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new SourceBaselineId(
            SrdSourceId, content.ContentFingerprint, ContentFingerprintDerivation);
    }

    /// <summary>
    /// The whole replay-compatibility identity of a run played against
    /// <paramref name="content"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static ReplayCompatibilityIdentity For(SrdContent content) => new(
        Ruleset,
        new ReplaySchemaVersion(ReplaySchemaRevision),
        [BaselineFor(content)],
        DiceAlgorithm);
}
