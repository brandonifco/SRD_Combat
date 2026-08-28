using System.Text.RegularExpressions;
using SRDCombat.Content;
using SRDCombat.Core.Definitions;
using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// Re-runs <see cref="EntryMechanicsParser.Classify"/> over every entry of every
/// monster in the committed corpus and asserts the result matches what is on disk.
/// This is the whole-corpus safety net #189 asks for: no PDF is needed, because the
/// corpus's own stored (name, section, text) is enough to reproduce what the parser
/// did — the corpus <em>is</em> the fixture set.
/// </summary>
/// <remarks>
/// The stored entry <c>name</c> is already bare (the usage suffix, e.g. "(Recharge
/// 6)", was parsed out of the printed heading and is preserved separately on
/// <c>usage</c>) — re-classifying from the bare name would silently lose it, so
/// <see cref="UsageLimit"/> is restored from the stored value before comparing.
/// </remarks>
public sealed class CorpusRoundTripTests
{
    private static readonly IReadOnlyList<MonsterDefinition> Monsters =
        ContentLoader.Load(RepositoryPaths.SrdContentDirectory).Monsters;

    public static IEnumerable<object[]> MonsterEntryPairs() =>
        Monsters.SelectMany(
            monster => monster.Entries,
            (monster, entry) => new object[] { monster.Name, entry });

    [Theory]
    [MemberData(nameof(MonsterEntryPairs))]
    public void ReparsingAStoredEntryReproducesItByteForByte(string monsterName, MonsterEntry stored)
    {
        var reparsed = EntryMechanicsParser.Classify(stored.Name, stored.Section, stored.Text)
            with
            { Usage = stored.Usage };

        var expected = ContentSerializer.Serialize(stored);
        var actual = ContentSerializer.Serialize(reparsed);

        Assert.True(
            expected == actual,
            $"Re-parsing '{monsterName}' :: '{stored.Name}' ({stored.Section}) did not reproduce " +
            $"the stored entry.\nExpected: {expected}\nActual:   {actual}");
    }

    /// <summary>
    /// The verbatim invariant (design §6.2): every residue string is a substring of
    /// its own entry's text. Nothing capitalises, synthesises a trailing period, or
    /// reassembles a clause any more, so a residue line is always greppable back to
    /// the exact page it came from — and this is what would catch a
    /// <c>CapitalizeFirst</c>-shaped helper being reintroduced.
    /// </summary>
    [Theory]
    [MemberData(nameof(MonsterEntryPairs))]
    public void EveryResidueStringIsVerbatimFromItsEntrysText(string monsterName, MonsterEntry stored)
    {
        foreach (var clause in stored.UnmodelledClauses)
        {
            Assert.True(
                stored.Text.Contains(clause, StringComparison.Ordinal),
                $"'{monsterName}' :: '{stored.Name}' ({stored.Section}) has a residue clause not " +
                $"found verbatim in its own text: [{clause}]");
        }
    }

    /// <summary>
    /// The glue census golden file (design §4.4): every distinct absorbed run across
    /// the whole corpus, whitespace-normalised and sorted, pinned against a checked-in
    /// list. The corpus is closed, so this vocabulary is finite — any change to the
    /// glue set, or any widening of a claim that changes what glue has to bridge,
    /// shows up here as a reviewable diff rather than silently. Per the 2026-08-24
    /// three-strikes protocol rule (design §4.4, §12.3), the third proposal to grow
    /// this list auto-files a mechanism issue asking whether the closed-set answer is
    /// still right — record each addition below with its date and reason.
    /// </summary>
    /// <remarks>
    /// Golden list produced by the first run against the 2026-08-24 regeneration and
    /// read once before being committed (design §14). Ten distinct runs: whitespace
    /// alone (a run of only spaces between two claims), each of the three connectives
    /// bounded by claims on both sides (", and ", " and ", " plus ", ", plus "), and
    /// the four punctuation marks in their observed contexts (a bare "." or ":", and
    /// each followed by a single trailing space before the next claim starts).
    /// </remarks>
    [Fact]
    public void TheGlueSetsObservedVocabularyMatchesTheCheckedInCensus()
    {
        var expected = new[]
        {
            " ",
            " and ",
            " plus ",
            ", ",
            ", and ",
            ", plus ",
            ".",
            ". ",
            ":",
            ": ",
        };

        var observed = MonsterEntryPairs()
            .SelectMany(pair =>
            {
                var entry = (MonsterEntry)pair[1];
                EntryMechanicsParser.Classify(entry.Name, entry.Section, entry.Text, out var coverage);
                return coverage.AbsorbedGlueRuns();
            })
            .Select(run => Regex.Replace(run, @"\s+", " "))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(run => run, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, observed);
    }

    [Fact]
    public void PriestsDivineAidRemainsAdjacentToAnIntactSpellcastingList()
    {
        var priest = Assert.Single(Monsters, monster => monster.Name == "Priest");
        var spellcasting = Assert.Single(priest.Entries, entry => entry.Name == "Spellcasting");
        var divineAid = Assert.Single(priest.Entries, entry => entry.Name == "Divine Aid");

        Assert.Contains("At Will: Light, Thaumaturgy", spellcasting.Text, StringComparison.Ordinal);
        Assert.Contains("1/Day: Spirit Guardians", spellcasting.Text, StringComparison.Ordinal);
        Assert.Equal(MonsterEntrySection.BonusAction, divineAid.Section);
        Assert.Contains("Bless, Dispel Magic, Healing Word, or Lesser Restoration", divineAid.Text, StringComparison.Ordinal);
    }
}
