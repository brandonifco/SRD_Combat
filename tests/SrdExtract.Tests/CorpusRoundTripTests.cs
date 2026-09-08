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

    /// <summary>
    /// Trip-wire for #412: <see cref="EntryMechanicsParser.Classify"/>'s Attack branch
    /// (~line 176) claims its rider text unconditionally, with no section gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the SavingThrow branch, which #411/#373 gated to Action/BonusAction
    /// because <c>Encounter.UseEntry</c> refuses every other section before it ever
    /// reads Mechanics. Today this is vacuous: querying the corpus directly (2026-09,
    /// 423 Attack-mechanics entries) shows every one of them is Action-section, so no
    /// Attack entry is currently reachable from a Trait, Reaction or LegendaryAction
    /// stat block section, but nothing asserted that this stays true until this test.
    /// </para>
    /// <para>
    /// If this test ever goes red, do <b>not</b> copy #411's fix (gating the rider
    /// claim by section) onto the Attack branch — <c>Encounter.MakeOpportunityAttack</c>
    /// selects an attack from <c>attacker.Stats.Attacks</c> section-blind (filtered only
    /// by <c>Kind == Melee</c> and usage availability), so a Reaction-section Attack
    /// entry would genuinely fire through an opportunity attack, and suppressing its
    /// rider claim would silently drop a rule that really executes. The failing entry
    /// needs a human reading of which gate shape is correct — <c>UseEntry</c>'s
    /// (refuse by section) or <c>MakeOpportunityAttack</c>'s (fire regardless) — not a
    /// reflexive mirror of the SavingThrow branch.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryAttackMechanicsEntryIsActionOrBonusActionSectioned()
    {
        var attackEntries = MonsterEntryPairs()
            .Select(pair => (Monster: (string)pair[0], Entry: (MonsterEntry)pair[1]))
            .Where(pair => pair.Entry.Mechanics == EntryMechanics.Attack)
            .ToList();

        // Guard against a vacuous pass: if a future extraction change stopped grading
        // anything as Attack, the offender filter below would be empty and the invariant
        // would go unchecked. The whole point is that Attack entries exist and are all
        // Action/BonusAction — so assert the corpus still has them before checking them.
        Assert.True(
            attackEntries.Count > 0,
            "no Attack-mechanics entries found in the corpus — this trip-wire would pass " +
            "vacuously; the extraction that grades entries as EntryMechanics.Attack has " +
            "changed and this test no longer verifies anything (#412).");

        var offenders = attackEntries
            .Where(pair => pair.Entry.Section is not (MonsterEntrySection.Action or MonsterEntrySection.BonusAction))
            .Select(pair =>
                $"'{pair.Monster}' :: '{pair.Entry.Name}' ({pair.Entry.Section})")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Attack-mechanics entries outside Action/BonusAction section — the invariant " +
            "#412 trip-wires just broke. This needs a human decision on which gate shape is " +
            "correct (UseEntry's section refusal, or MakeOpportunityAttack's section-blind " +
            "firing — see #412 and this test's remarks), not a reflexive mirror of #411's " +
            $"SavingThrow-branch fix.\nOffending entries:\n{string.Join('\n', offenders)}");
    }

    /// <summary>
    /// Trip-wire for #666, on #412's own pattern: the grappler name
    /// <c>AttackRollAdvantageConditionPattern</c> captures ("by the ankheg", "by the
    /// bugbear") is a bare <c>\w+</c>, out of the wildcard convention's scope and
    /// claimed anyway (design §2.3) on the same precedent
    /// <c>AlternativeDamagePattern</c>'s and <c>EmDashAlternativeDamagePattern</c>'s
    /// own subject groups state. Nothing stores that captured word — the enum member
    /// alone is derived — so nothing at runtime checks it names the attacker rather
    /// than some other creature. This test is the check: for every corpus entry
    /// graded <see cref="AttackRollAdvantageCondition.TargetIsGrappledByAttacker"/>,
    /// the printed "Grappled by the &lt;word&gt;" names a word of the entry's own
    /// monster, exactly as Ankheg, Bugbear Stalker, Bugbear Warrior and Mimic do
    /// today. The first "Grappled by &lt;someone else&gt;" would be a misattribution
    /// this test turns loud instead of leaving it to load silently — the omission
    /// class #382 closed by construction does not cover a claim that reads the wrong
    /// creature, which is exactly the risk a bare wildcard capture carries.
    /// </summary>
    [Fact]
    public void EveryGrappledByAttackerAdvantageConditionNamesAWordOfItsOwnMonster()
    {
        var grapplerName = new Regex(
            @"Grappled\s+by\s+the\s+(?<grappler>\w+)",
            RegexOptions.IgnoreCase);

        var matches = MonsterEntryPairs()
            .Select(pair => (Monster: (string)pair[0], Entry: (MonsterEntry)pair[1]))
            .Where(pair => pair.Entry.Attack?.AdvantageCondition
                == AttackRollAdvantageCondition.TargetIsGrappledByAttacker)
            .ToList();

        // The census bar this slice claims (#666's spec): exactly four entries print
        // this circumstance. A count that drifts means either a new corpus entry
        // needs this trip-wire's attention or the pattern's own reach changed —
        // either way, loud rather than silently trusted.
        Assert.Equal(4, matches.Count);

        var offenders = new List<string>();

        foreach (var (monster, entry) in matches)
        {
            var match = grapplerName.Match(entry.Text);

            if (!match.Success)
            {
                offenders.Add($"'{monster}' :: '{entry.Name}' — no \"Grappled by the <word>\" text found");
                continue;
            }

            var grappler = match.Groups["grappler"].Value;

            if (!monster.Contains(grappler, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(
                    $"'{monster}' :: '{entry.Name}' — captured grappler '{grappler}' is not a word of " +
                    $"the monster's own name");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "AttackRollAdvantageCondition.TargetIsGrappledByAttacker claimed on an entry whose printed " +
            "grappler is not the entry's own monster — this needs a human reading of what the printed " +
            $"text actually says, not a reflexive widening of the pattern:\n{string.Join('\n', offenders)}");
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
