using RulesKernel.Identity;
using SRDCombat.Content;

namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="RunIdentity"/>: the run-comparability facts this engine now states out loud,
/// pinned so none of them can change without a test saying so.
/// </summary>
public class RunIdentityTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void TheIdentityNamesThisEnginesRulesetAndRevision()
    {
        var identity = RunIdentity.For(Content);

        Assert.Equal("srd-combat", identity.Ruleset.Id);
        // A literal, not RunIdentity.RulesetRevision. Comparing the constant to itself
        // passes for any value it could ever hold, which is no pin at all -- changing the
        // constant to 7 left all seven tests green.
        Assert.Equal(1, identity.Ruleset.Version);
    }

    /// <summary>
    /// The one corpus this engine reads, pinned by the fingerprint that already existed.
    /// The derivation is <c>id-roster</c> because that is literally what
    /// <see cref="SrdContent.ContentFingerprint"/> hashes — a baseline that claimed to pin
    /// the corpus's content would be claiming more than the hash supports.
    /// </summary>
    [Fact]
    public void TheOnlyBaselineIsTheSrdRosterFingerprint()
    {
        var baseline = Assert.Single(RunIdentity.For(Content).SourceBaselines);

        Assert.Equal("srd-5.2.1", baseline.SourceId);
        Assert.Equal("id-roster", baseline.HashDerivation);
        Assert.Equal(Content.ContentFingerprint, baseline.ContentHash, ignoreCase: true);
    }

    /// <summary>
    /// SRD 5.2.1 is a fixed printing — the revision is in its name, and a correction is
    /// published as a new numbered document. Absent here means "this corpus has no
    /// temporal dimension", which the kernel distinguishes from an unknown date.
    /// </summary>
    [Fact]
    public void TheSrdBaselineCarriesNoAsOfDate()
    {
        Assert.Null(RunIdentity.BaselineFor(Content).AsOf);
    }

    /// <summary>
    /// The trip-wire for the one case-folding hazard this adoption introduces:
    /// <see cref="SrdContent.ContentFingerprint"/> is upper-case hex and
    /// <see cref="SourceBaselineId"/> lower-cases what it is handed, so the two strings are
    /// never ordinally equal. A comparison written later against
    /// <see cref="SavedRun.ContentVersion"/> that forgot this would report every save as
    /// content-drifted. Asserted rather than assumed, in both directions.
    /// </summary>
    [Fact]
    public void TheBaselineHashIsTheFingerprintLowerCased()
    {
        var baseline = RunIdentity.BaselineFor(Content);

        Assert.Equal(Content.ContentFingerprint.ToLowerInvariant(), baseline.ContentHash);
        Assert.NotEqual(Content.ContentFingerprint, baseline.ContentHash, StringComparer.Ordinal);
    }

    /// <summary>
    /// This engine draws dice, so the algorithm slot is filled. Leaving it empty is not the
    /// cautious choice — the kernel reads an absent algorithm as the positive claim that no
    /// randomness is consumed, which would be false here.
    /// </summary>
    [Fact]
    public void TheIdentityDeclaresThatThisEngineConsumesRandomness()
    {
        var identity = RunIdentity.For(Content);

        Assert.False(identity.IsDeterministicWithoutRandomness);
        Assert.Equal("system-random-seeded", identity.RandomAlgorithm?.Name);
    }

    /// <summary>
    /// Nothing writes a replay bundle yet (#722), so the replay-record shape has no
    /// revision. It is pinned at 0 here precisely so that the first bundle to reach disk
    /// has to move it deliberately.
    /// </summary>
    [Fact]
    public void TheReplaySchemaRevisionIsStillZeroBecauseNothingRecordsAReplay()
    {
        Assert.Equal(0, RunIdentity.For(Content).ReplaySchema.Version);
    }

    /// <summary>
    /// An identity is a value, not an object: two runs on the same content compare equal,
    /// which is the whole point of having one.
    /// </summary>
    [Fact]
    public void TwoRunsOnTheSameContentShareOneIdentity()
    {
        var first = GauntletRun.Start(Content, seed: 1);
        var second = GauntletRun.Start(Content, seed: 2);

        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(RunIdentity.For(Content), first.Identity);
    }
}
