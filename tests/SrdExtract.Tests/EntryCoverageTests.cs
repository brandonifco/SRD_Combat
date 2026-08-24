using System.Text.RegularExpressions;
using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// Unit tests over <see cref="EntryCoverage"/> itself — the span-coverage type from
/// the #382 refactor (docs/2026-08-24-span-accounting-design.md). Stage 0: the type
/// is exercised directly here, with nothing in the parser wired to it yet.
/// </summary>
public sealed partial class EntryCoverageTests
{
    #region Claim and Masked

    [Fact]
    public void AClaimedSpanIsMaskedToSpacesOfTheSameLength()
    {
        var coverage = new EntryCoverage("Hello world");
        coverage.Claim(new TextSpan(0, 5), "test.claim");

        Assert.Equal("      world", coverage.Masked);
        Assert.Equal("Hello world", coverage.Text);
    }

    [Fact]
    public void AnUnclaimedTextMasksToItselfUnchanged()
    {
        var coverage = new EntryCoverage("Untouched");

        Assert.Equal("Untouched", coverage.Masked);
    }

    [Fact]
    public void ClaimWholeEntryMasksEverythingAndLeavesNoResidue()
    {
        var coverage = new EntryCoverage("The whole thing.");
        coverage.ClaimWholeEntry("trait.registry");

        Assert.Equal(new string(' ', "The whole thing.".Length), coverage.Masked);
        Assert.Empty(coverage.Residue());
    }

    [Fact]
    public void ClaimingASpanOutsideTheTextThrows()
    {
        var coverage = new EntryCoverage("short");

        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.Claim(new TextSpan(3, 10), "test.claim"));
    }

    [Fact]
    public void ClaimingAZeroLengthSpanIsANoOpRatherThanAnError()
    {
        var coverage = new EntryCoverage("text");
        coverage.Claim(new TextSpan(2, 0), "test.claim");

        Assert.Equal("text", coverage.Masked);
        Assert.Single(coverage.Residue());
    }

    #endregion

    #region Overlap is union, never an error

    [Fact]
    public void OverlappingClaimsUnionRatherThanDuplicateOrConflict()
    {
        // Two matchers reading the same characters — the embedded save's rider and a
        // later condition-name match, per design §2.4.
        var coverage = new EntryCoverage("ABCDEFG");
        coverage.Claim(new TextSpan(0, 4), "matcher.one"); // "ABCD"
        coverage.Claim(new TextSpan(2, 4), "matcher.two"); // "CDEF", overlaps 2..4

        Assert.Equal("      G", coverage.Masked);
        Assert.Equal(["G"], coverage.Residue());
    }

    #endregion

    #region The glue rule — bounded-both-sides absorption

    [Fact]
    public void AConnectiveRunBoundedOnBothSidesByClaimsIsAbsorbed()
    {
        // ", and " between a claimed Hit clause and a claimed imposable rider.
        var text = "AAAA, and BBBB";
        var coverage = new EntryCoverage(text);
        coverage.Claim(new TextSpan(0, 4), "claim.left"); // "AAAA"
        coverage.Claim(new TextSpan(10, 4), "claim.right"); // "BBBB"

        Assert.Empty(coverage.Residue());
    }

    [Fact]
    public void APlusConnectiveRunBetweenTwoClaimsIsAbsorbed()
    {
        var text = "5 (2d4) Slashing damage plus 2 (1d4) Fire damage";
        var coverage = new EntryCoverage(text);
        coverage.Claim(new TextSpan(0, 23), "damage.first"); // "5 (2d4) Slashing damage"
        coverage.Claim(new TextSpan(29, 19), "damage.second"); // "2 (1d4) Fire damage"

        Assert.Empty(coverage.Residue());
    }

    [Fact]
    public void ASentenceBoundaryBetweenTwoClaimsIsAbsorbed()
    {
        var text = "First claim. Second claim.";
        var coverage = new EntryCoverage(text);
        coverage.Claim(new TextSpan(0, 12), "claim.first"); // "First claim."
        coverage.Claim(new TextSpan(13, 13), "claim.second"); // "Second claim."

        Assert.Empty(coverage.Residue());
    }

    [Fact]
    public void ATrailingFullStopAfterTheLastClaimIsAbsorbedByTheTextsEdge()
    {
        var coverage = new EntryCoverage("AAAA.");
        coverage.Claim(new TextSpan(0, 4), "claim.only");

        Assert.Empty(coverage.Residue());
    }

    [Fact]
    public void LeadingWhitespaceBeforeTheFirstClaimIsAbsorbedByTheTextsEdge()
    {
        var coverage = new EntryCoverage("  AAAA");
        coverage.Claim(new TextSpan(2, 4), "claim.only");

        Assert.Empty(coverage.Residue());
    }

    #endregion

    #region The glue rule — a dangling connective is residue, not absorbed

    [Fact]
    public void ALeadingConnectiveWithNoClaimOnItsLeftIsResidue()
    {
        var coverage = new EntryCoverage("and BBBB");
        coverage.Claim(new TextSpan(4, 4), "claim.only"); // "BBBB"

        Assert.Equal(["and"], coverage.Residue());
    }

    [Fact]
    public void ATrailingConnectiveWithNoClaimOnItsRightIsResidue()
    {
        var coverage = new EntryCoverage("AAAA and");
        coverage.Claim(new TextSpan(0, 4), "claim.only"); // "AAAA"

        Assert.Equal(["and"], coverage.Residue());
    }

    [Fact]
    public void AnOrClauseBetweenAClaimAndUnclaimedTextReadsAsOneResidueClause()
    {
        // The Swarm of Rats' Bites shape (#371): ", or 2 (1d4) Piercing damage if the
        // swarm is Bloodied" is one uncovered run — not glue at all, because it carries
        // real words — so the whole alternative survives as one clause.
        var text = "Hit: 5 (2d4) Piercing damage, or 2 (1d4) Piercing damage if the swarm is Bloodied.";
        var coverage = new EntryCoverage(text);
        coverage.Claim(new TextSpan(0, 29), "attack.hit"); // "Hit: 5 (2d4) Piercing damage,"

        var residue = Assert.Single(coverage.Residue());
        Assert.Equal("or 2 (1d4) Piercing damage if the swarm is Bloodied", residue);
    }

    #endregion

    #region A non-glue run is residue in full, never glue-trimmed away

    [Fact]
    public void ARunContainingOrdinaryWordsIsNeverAbsorbedEvenWhenItStartsWithAConnective()
    {
        // "The mummy makes two Rotting Fist attacks and uses Dreadful Glare." — the
        // bundled use clause. Design §9.1: "And uses Dreadful Glare." becomes
        // "and uses Dreadful Glare" — verbatim, no synthesised capital or period.
        var text = "AAAA and uses Dreadful Glare";
        var coverage = new EntryCoverage(text);
        coverage.Claim(new TextSpan(0, 4), "multiattack.composition"); // "AAAA"

        var residue = Assert.Single(coverage.Residue());
        Assert.Equal("and uses Dreadful Glare", residue);
    }

    #endregion

    #region Chunking at sentence boundaries

    [Fact]
    public void AnUncoveredRunSpanningTwoSentencesYieldsTwoResidueChunks()
    {
        var coverage = new EntryCoverage("The golem goes berserk. It attacks the nearest creature.");

        Assert.Equal(
            ["The golem goes berserk", "It attacks the nearest creature"],
            coverage.Residue());
    }

    [Fact]
    public void SentenceChunkingDoesNotBreakOnTheFtAbbreviation()
    {
        // Mirrors SentenceSplittingDoesNotBreakOnFtAbbreviation in the characterization
        // suite: "reach 5 ft. Hit:" must not read as a sentence boundary.
        var coverage = new EntryCoverage("reach 5 ft. Hit: something");

        var residue = Assert.Single(coverage.Residue());
        Assert.Equal("reach 5 ft. Hit: something", residue);
    }

    #endregion

    #region Verbatim invariant

    [Fact]
    public void EveryResidueStringIsAVerbatimSubstringOfTheEntrysText()
    {
        var text = "The golem starts its turn Bloodied, and it goes berserk immediately.";
        var coverage = new EntryCoverage(text);
        coverage.Claim(new TextSpan(4, 5), "partial.claim"); // "golem"

        foreach (var clause in coverage.Residue())
        {
            Assert.Contains(clause, text, StringComparison.Ordinal);
        }
    }

    #endregion

    #region Claim(Regex, Match, note, unreadGroups) — the wildcard convention

    [GeneratedRegex(@"(?<head>ABC)(?<unread>.*?)(?<tail>XYZ)")]
    private static partial Regex HeadUnreadTailPattern();

    [Fact]
    public void ClaimingARegexMatchExcludesAnUnreadNamedGroupFromTheClaim()
    {
        var text = "ABC123XYZ";
        var coverage = new EntryCoverage(text);
        var pattern = HeadUnreadTailPattern();
        var match = pattern.Match(text);

        coverage.Claim(pattern, match, "test.headtail", "unread");

        Assert.Equal(["123"], coverage.Residue());
        Assert.Equal("   123   ", coverage.Masked);
    }

    [Fact]
    public void ClaimingARegexMatchWithNoUnreadGroupsClaimsTheWholeMatch()
    {
        var text = "ABC123XYZ";
        var coverage = new EntryCoverage(text);
        var pattern = HeadUnreadTailPattern();
        var match = pattern.Match(text);

        coverage.Claim(pattern, match, "test.headtail");

        Assert.Empty(coverage.Residue());
    }

    [Fact]
    public void ClaimingARegexMatchRecordsItsPatternInTheProcessWideRegistry()
    {
        var text = "ABC123XYZ";
        var coverage = new EntryCoverage(text);
        var pattern = HeadUnreadTailPattern();
        var match = pattern.Match(text);

        coverage.Claim(pattern, match, "test.headtail", "unread");

        Assert.Contains(pattern.ToString(), EntryCoverage.ClaimingPatterns);
    }

    [Fact]
    public void ClaimingAFailedMatchThrows()
    {
        var coverage = new EntryCoverage("nothing matches here");
        var pattern = HeadUnreadTailPattern();
        var match = pattern.Match("nothing matches here");

        Assert.Throws<ArgumentException>(() => coverage.Claim(pattern, match, "test.headtail"));
    }

    #endregion
}
