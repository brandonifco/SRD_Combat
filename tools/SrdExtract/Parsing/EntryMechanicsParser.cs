using System.Globalization;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SrdExtract.Parsing;

/// <summary>
/// Classifies every stat block entry and pulls out whatever mechanics the model can
/// express.
/// </summary>
/// <remarks>
/// <para>
/// The governing rule is that no entry may pass as ordinary prose. Each one is examined
/// and comes out as a recognised kind of mechanic, as explicitly inert
/// (<see cref="EntryMechanics.Narrative"/>, only ever from the curated list below), or
/// as <see cref="EntryMechanics.Unmodelled"/> with the offending clauses recorded.
/// </para>
/// <para>
/// A high Unmodelled count is the honest answer, not a failure. The alternative — a
/// heuristic that decides an entry "probably doesn't matter" — is how a Basilisk ends up
/// never petrifying anyone and nothing says so.
/// </para>
/// </remarks>
internal static partial class EntryMechanicsParser
{
    /// <summary>
    /// Entries confirmed to have no effect on a fight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately tiny and grown one deliberate decision at a time. Anything not on it
    /// is Unmodelled and counted, which is the safe direction to be wrong in. Several
    /// traits that look inert are not: Pack Tactics grants Advantage on attack rolls,
    /// Sunlight Sensitivity imposes Disadvantage, and Flyby removes Opportunity Attacks —
    /// none of those belong here.
    /// </para>
    /// <para>
    /// <b>This list is curated about stat block entries and species/class trait text —
    /// never spells.</b> <see cref="ClassifyTrait"/> is shared with <c>SpellParser</c>
    /// for its condition and saving-throw grammar, and for a while that sharing reached
    /// this list too: Water Breathing landed on <see cref="EntryMechanics.Narrative"/>
    /// only because it happens to spell the same name as this bestiary trait, not
    /// because anyone read the spell and judged it inert (#349). The <c>consultInertList</c>
    /// parameter below is how <c>SpellParser</c> opts out. Spells that genuinely do
    /// nothing in a fight — Water Breathing among 184 others, Detect Magic and Identify
    /// included — are not exceptions here; they classify as
    /// <see cref="EntryMechanics.Unmodelled"/> like every spell the grammar does not
    /// structure, which is the honest and already-established outcome for spell prose
    /// (see <c>SpellDefinition.UnclassifiedClauses</c> — a spell's classification carries
    /// no completeness claim either way, so Unmodelled costs nothing here). A curated
    /// Narrative list for spells was considered and rejected: singling out Water
    /// Breathing while Light, Alarm and Comprehend Languages stay Unmodelled would be
    /// the accidental result blessed into a decision it never was.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> KnownInertEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "Amphibious",
        "Water Breathing",
        "Illumination",
    };

    /// <summary>
    /// Classifies a species trait or other named rules text, applying the same rule as
    /// stat block entries: nothing passes as prose.
    /// </summary>
    /// <param name="name">The trait or spell's printed name.</param>
    /// <param name="text">Its body text.</param>
    /// <param name="consultInertList">
    /// Whether a name match against <see cref="KnownInertEntries"/> may grade this entry
    /// <see cref="EntryMechanics.Narrative"/>. True for species and class trait text,
    /// where the list's decisions were actually made about entries of that shape.
    /// <c>SpellParser</c> passes <see langword="false"/>: that list has never been
    /// curated about spell prose, and a name collision (#349) is not a reading of the
    /// spell.
    /// </param>
    public static TraitEntry ClassifyTrait(string name, string text, bool consultInertList = true) =>
        ClassifyTrait(name, text, consultInertList, out _);

    /// <summary>
    /// The same classification, with the <see cref="EntryCoverage"/> it built along the
    /// way — the census tool's own entry point (stage 3, not yet consumed for output).
    /// </summary>
    internal static TraitEntry ClassifyTrait(string name, string text, bool consultInertList, out EntryCoverage coverage)
    {
        var usage = ParseUsageLimit(name);
        var bareName = StripUsage(name);
        coverage = new EntryCoverage(text);
        var (conditions, claimableRiders) = ParseAppliedConditionsWithClaims(text, coverage);

        if (ParseSave(text, conditions, coverage) is { } save)
        {
            // The entry's mechanics is SavingThrow — one of the two the engine
            // imposes riders from (design §2.5) — so every fully-modelled rider's
            // clause is claimed now, not before.
            foreach (var (_, span) in claimableRiders)
            {
                coverage.Claim(span, "condition.rider");
            }

            return new TraitEntry(
                bareName,
                text,
                EntryMechanics.SavingThrow,
                save,
                usage,
                conditions,
                LeftoverMechanicalSentences(text, EntryMechanics.SavingThrow, conditions));
        }

        if (consultInertList && KnownInertEntries.Contains(bareName))
        {
            // A curated human decision is a reading of the whole entry, so it covers
            // the whole entry (design §2.6).
            coverage.ClaimWholeEntry("inert.curated");
            return new TraitEntry(bareName, text, EntryMechanics.Narrative, Usage: usage);
        }

        return new TraitEntry(
            bareName,
            text,
            EntryMechanics.Unmodelled,
            Usage: usage,
            AppliedConditions: conditions,
            UnmodelledClauses: MechanicalSentences(text));
    }

    /// <summary>Examines one entry and returns it classified, with whatever could be extracted.</summary>
    public static MonsterEntry Classify(string name, MonsterEntrySection section, string text) =>
        Classify(name, section, text, out _);

    /// <summary>
    /// The same classification, with the <see cref="EntryCoverage"/> it built along the
    /// way — the census tool's own entry point (stage 3, not yet consumed for output).
    /// </summary>
    internal static MonsterEntry Classify(string name, MonsterEntrySection section, string text, out EntryCoverage coverage)
    {
        var usage = ParseUsageLimit(name);
        var bareName = StripUsage(name);
        coverage = new EntryCoverage(text);
        var attack = StatBlockLineGrammar.ParseAttack(text, coverage);

        // A second, throwaway coverage computes riderText and must reflect only the
        // embedded-save claim — exactly what stages 1-2 left it as. riderText still
        // drives real output (the rider-parsing pass below and LeftoverMechanicalSentences'
        // scan, via mechanicsText), and both read text by matching literal labels
        // ("Attack Roll:", "Hit:") that ParseAttack's own claims into `coverage` would
        // mask out from under them if riderText were computed from `coverage` directly.
        // `coverage` itself keeps accumulating every claim, attack header and damage
        // included — that accumulation is stage 3's plumbing (design §10: "not yet
        // used"), kept strictly apart from what the still-authoritative old accounting
        // sees.
        var riderMask = new EntryCoverage(text);

        // The embedded saving throw — the Ghast's Claw — is structured before the
        // rider pass, and its span is claimed so the riders and the unmodelled-clause
        // scan see it masked out: its three sentences are the save's now, and the
        // rider inside them must not also be parsed (and refused) as the attack's own.
        if (attack is not null && ParseEmbeddedSave(text) is { } embedded)
        {
            attack = attack with { EmbeddedSave = embedded.Save };
            coverage.Claim(embedded.MatchedSpan, "attack.embedded_save");
            riderMask.Claim(embedded.MatchedSpan, "attack.embedded_save");
        }

        var riderText = riderMask.Masked;
        var (conditions, claimableRiders) = ParseAppliedConditionsWithClaims(
            riderText,
            coverage,
            attackEntry: attack is not null);

        if (attack is not null)
        {
            // The entry's mechanics is Attack — one of the two the engine imposes
            // riders from (design §2.5) — so every fully-modelled rider's clause is
            // claimed now, not before.
            foreach (var (_, span) in claimableRiders)
            {
                coverage.Claim(span, "condition.rider");
            }

            return Build(
                bareName,
                section,
                text,
                EntryMechanics.Attack,
                usage,
                conditions,
                attack: attack,
                mechanicsText: riderText);
        }

        if (ParseReaction(text, coverage) is { } reaction)
        {
            return Build(bareName, section, text, EntryMechanics.Reaction, usage, conditions, reaction: reaction);
        }

        if (ParseMultiattack(text, coverage, out var alternativeClause) is { } multiattack)
        {
            // The alternative branch ParseMultiattack set aside (see its remarks) and any
            // bundled-use clause folded into the same sentence (see
            // BundledMultiattackUseClauses' remarks) are not sentences
            // LeftoverMechanicalSentences would ever find on their own, since the whole
            // entry is one sentence in every case that prints either. Handing them in
            // here is what keeps them from vanishing behind DescribesTheComposition
            // matching the rest of the sentence (#341).
            var remainder = new List<string>();

            if (alternativeClause.Length > 0)
            {
                remainder.Add(CapitalizeFirst(alternativeClause));
            }

            remainder.AddRange(BundledMultiattackUseClauses(text));

            return Build(
                bareName,
                section,
                text,
                EntryMechanics.Multiattack,
                usage,
                conditions,
                multiattack: multiattack,
                extraUnmodelledClauses: remainder.Count > 0 ? remainder : null);
        }

        if (ParseSave(text, conditions, coverage) is { } save)
        {
            // The entry's mechanics is SavingThrow — the other of the two the engine
            // imposes riders from (design §2.5) — so every fully-modelled rider's
            // clause is claimed now.
            foreach (var (_, span) in claimableRiders)
            {
                coverage.Claim(span, "condition.rider");
            }

            return Build(bareName, section, text, EntryMechanics.SavingThrow, usage, conditions, save: save);
        }

        if (section == MonsterEntrySection.Trait && MonsterTraitRegistry.Implements(bareName))
        {
            // The engine executes this trait by its printed name. MonsterTraitRegistry
            // is the curated list, and the reading each name rests on — including where
            // it is deliberately narrower than the printed sentence — is recorded there.
            // A curated human decision is a reading of the whole entry, so it covers
            // the whole entry (design §2.6).
            coverage.ClaimWholeEntry("trait.registry");
            return new MonsterEntry(bareName, section, text, Mechanics: EntryMechanics.Passive, Usage: usage);
        }

        if (KnownInertEntries.Contains(bareName))
        {
            // No unmodelled clauses by definition — this is a recorded decision that the
            // entry does nothing in a fight.
            coverage.ClaimWholeEntry("inert.curated");
            return new MonsterEntry(bareName, section, text, Mechanics: EntryMechanics.Narrative, Usage: usage);
        }

        return new MonsterEntry(
            bareName,
            section,
            text,
            Mechanics: EntryMechanics.Unmodelled,
            Usage: usage,
            AppliedConditions: conditions,
            UnmodelledClauses: MechanicalSentences(text));
    }

    /// <param name="mechanicsText">
    /// The text <see cref="LeftoverMechanicalSentences"/> scans, when it must differ
    /// from <paramref name="text"/> — the Attack branch's embedded-save-masked
    /// <c>riderText</c>, so a rider already claimed by the embedded save is not
    /// re-scanned as the attack's own leftover prose. Defaults to <paramref name="text"/>
    /// itself, which is always right for every other branch: there is only ever one
    /// text stored on the entry, <paramref name="text"/>, unmasked.
    /// </param>
    private static MonsterEntry Build(
        string name,
        MonsterEntrySection section,
        string text,
        EntryMechanics mechanics,
        UsageLimit? usage,
        IReadOnlyList<AppliedCondition> conditions,
        MonsterAttack? attack = null,
        SaveEffect? save = null,
        MultiattackEffect? multiattack = null,
        ReactionEffect? reaction = null,
        IReadOnlyList<string>? extraUnmodelledClauses = null,
        string? mechanicsText = null)
    {
        var scanText = mechanicsText ?? text;

        return new(
            name,
            section,
            text,
            attack,
            mechanics,
            save,
            multiattack,
            reaction,
            usage,
            conditions,
            extraUnmodelledClauses is null
                ? LeftoverMechanicalSentences(scanText, mechanics, conditions, multiattack)
                : [.. LeftoverMechanicalSentences(scanText, mechanics, conditions, multiattack), .. extraUnmodelledClauses]);
    }

    /// <summary>Uppercases a clause's first letter, for fragments lifted mid-sentence.</summary>
    private static string CapitalizeFirst(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Finds sentences carrying mechanics that the entry's own structured form does not
    /// account for.
    /// </summary>
    /// <remarks>
    /// This is what catches the partly-structured case. An attack entry's header and its
    /// Hit clause are covered by <see cref="MonsterAttack"/>; a rider sentence after them
    /// ("If the target is a Large or smaller creature, it has the Grappled condition") is
    /// covered only when the engine will really impose that condition on exactly the
    /// targets the SRD names.
    /// </remarks>
    private static IReadOnlyList<string> LeftoverMechanicalSentences(
        string text,
        EntryMechanics mechanics,
        IReadOnlyList<AppliedCondition> conditions,
        MultiattackEffect? multiattack = null) =>
        SplitSentences(text)
            .Where(sentence => !IsAccountedFor(sentence, mechanics, conditions, multiattack))
            .ToArray();

    /// <summary>
    /// Every sentence of an entry the model could not classify at all.
    /// </summary>
    /// <remarks>
    /// Deliberately unfiltered. An earlier version screened sentences through a
    /// "does this look mechanical?" test, and the data showed exactly why that was
    /// wrong: Flyby ("doesn't provoke Opportunity Attacks"), Nimble Escape ("takes the
    /// Disengage or Hide action") and Shape-Shift all slipped through as apparently
    /// inert. A keyword list will always have false negatives, and a false negative here
    /// silently loses a rule — the one failure this whole model exists to prevent. If it
    /// was not modelled, it is reported, and the only route to "no combat effect" is the
    /// curated list.
    /// </remarks>
    private static IReadOnlyList<string> MechanicalSentences(string text) => SplitSentences(text).ToArray();

    /// <summary>Whether a sentence is already captured by the entry's structured form.</summary>
    /// <remarks>
    /// A sentence carrying a condition the engine will not impose is never accounted for,
    /// however well the rest of it is structured. That is the same lesson as the goblin's
    /// conditional damage, found again in the same data: thirteen attacks read as fully
    /// modelled because their whole entry was one sentence containing "Attack Roll:", and
    /// the "and the target has the Poisoned condition until ..." hanging off the end was
    /// invisible.
    /// </remarks>
    private static bool IsAccountedFor(
        string sentence,
        EntryMechanics mechanics,
        IReadOnlyList<AppliedCondition> conditions,
        MultiattackEffect? multiattack)
    {
        var carried = ConditionsIn(sentence, conditions).ToArray();

        if (carried.Any(condition => !ConditionRules.CanBeImposed(condition)))
        {
            return false;
        }

        // The repeat-save clock's own sentences are expressed by the duration the
        // rider carries: the automatic success is the ten-turn cap, and the separate
        // repeat sentence was joined onto its rider before parsing — this covers the
        // printings where it also stands alone in the source text.
        if (conditions.Any(condition => condition.Duration is { RepeatSaveAtTurnEnd: true })
            && (sentence.StartsWith("After 1 minute, it succeeds automatically", StringComparison.Ordinal)
                || sentence.StartsWith(
                    "At the end of each of its turns, the target repeats the save",
                    StringComparison.Ordinal)))
        {
            return true;
        }

        // A sentence that is nothing but an imposable rider is now fully expressed, even
        // though the attack or saving-throw grammar itself says nothing about it. Save
        // entries joined attacks here when the engine began imposing their riders on a
        // failed save (#6).
        return MatchesStructuredForm(sentence, mechanics, multiattack)
            || (mechanics is EntryMechanics.Attack or EntryMechanics.SavingThrow && carried.Length > 0);
    }

    private static bool MatchesStructuredForm(
        string sentence,
        EntryMechanics mechanics,
        MultiattackEffect? multiattack) => mechanics switch
    {
        EntryMechanics.Attack =>
            sentence.Contains("Attack Roll:", StringComparison.Ordinal)
            || sentence.StartsWith("Hit:", StringComparison.Ordinal),

        EntryMechanics.SavingThrow =>
            sentence.Contains("Saving Throw:", StringComparison.Ordinal)
            || sentence.StartsWith("Failure", StringComparison.Ordinal)
            || sentence.StartsWith("Success", StringComparison.Ordinal),

        EntryMechanics.Multiattack => multiattack is not null && DescribesTheComposition(sentence, multiattack),

        EntryMechanics.Reaction =>
            sentence.StartsWith("Trigger:", StringComparison.Ordinal)
            || sentence.StartsWith("Response:", StringComparison.Ordinal),

        _ => false,
    };

    /// <summary>
    /// Whether this sentence is the attack-composition sentence the Multiattack grammar
    /// read — the only part of a Multiattack entry the model expresses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to answer <c>true</c> for every sentence of a Multiattack entry, which
    /// is the goblin's conditional damage a fourth time: 45 entries print a second
    /// sentence — "It can replace one attack with a use of Roar/Spellcasting/…" — and
    /// the engine executes none of it, while the accounting called the whole entry
    /// modelled. The Lion graded <c>Playable</c> and the Pirate <c>Complete</c> on the
    /// strength of a replacement option that never happens.
    /// </para>
    /// <para>
    /// What the model expresses is <see cref="MultiattackEffect"/>: how many attacks, out
    /// of which named ones, mixed freely or not. So the test is that the sentence, read
    /// alone by the same grammar, yields exactly the composition the entry recorded.
    /// Comparing against the entry's own effect rather than merely asking "does this
    /// sentence parse?" is what stops a second composition sentence the entry-level parse
    /// dropped — the combination form returns on its first match — from being waved
    /// through by the sentence-level one.
    /// </para>
    /// <para>
    /// Deliberately strict in the safe direction: a composition split across two
    /// sentences, which the bestiary does not currently print, would match neither and be
    /// counted rather than approximated. Over-counting is the error this model chooses.
    /// </para>
    /// </remarks>
    private static bool DescribesTheComposition(string sentence, MultiattackEffect multiattack) =>
        ParseMultiattack(sentence) is { } parsed
        && parsed.AttackCount == multiattack.AttackCount
        && parsed.AnyCombination == multiattack.AnyCombination
        && parsed.AttackNames.SequenceEqual(multiattack.AttackNames, StringComparer.Ordinal);

    private static IEnumerable<string> SplitSentences(string text) =>
        SentenceBoundary()
            .Split(text)
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0);

    /// <summary>
    /// The same split as <see cref="SplitSentences"/>, with each piece's span into
    /// <paramref name="text"/> alongside it. The rider-parsing loop in
    /// <see cref="ParseAppliedConditions"/> needs this to look one sentence ahead
    /// without losing the offset it would claim there (design §5.2's annex rule).
    /// </summary>
    private static IEnumerable<(string Text, TextSpan Span)> SplitSentencesWithSpans(string text)
    {
        var cursor = 0;

        foreach (Match boundary in SentenceBoundary().Matches(text))
        {
            if (boundary.Index > cursor)
            {
                foreach (var piece in TrimToSpan(text, cursor, boundary.Index))
                {
                    yield return piece;
                }
            }

            cursor = boundary.Index + boundary.Length;
        }

        if (cursor < text.Length)
        {
            foreach (var piece in TrimToSpan(text, cursor, text.Length))
            {
                yield return piece;
            }
        }
    }

    /// <summary>Trims a <c>[start, end)</c> slice and reports its span, or nothing if it trims to empty.</summary>
    private static IEnumerable<(string Text, TextSpan Span)> TrimToSpan(string text, int start, int end)
    {
        var raw = text[start..end];
        var trimmed = raw.Trim();

        if (trimmed.Length == 0)
        {
            yield break;
        }

        var leadingWhitespace = raw.Length - raw.TrimStart().Length;

        yield return (trimmed, new TextSpan(start + leadingWhitespace, trimmed.Length));
    }

    /// <summary>
    /// The sentence-boundary matches themselves, rather than the split pieces —
    /// <see cref="EntryCoverage"/> needs the boundary's own span to chunk a surviving
    /// uncovered run without losing its offset into the entry's original text.
    /// </summary>
    internal static IReadOnlyList<Match> SentenceBoundaryMatches(string text) => SentenceBoundary().Matches(text);

    /// <summary>Parses "(Recharge 5-6)", "(Recharge 6)", "(3/Day)" and "(Recharge after a ... Rest)".</summary>
    private static UsageLimit? ParseUsageLimit(string name)
    {
        if (RechargeRestPattern().IsMatch(name))
        {
            return new UsageLimit(UsageLimitKind.RechargeAfterRest);
        }

        if (RechargePattern().Match(name) is { Success: true } recharge)
        {
            return new UsageLimit(
                UsageLimitKind.Recharge,
                RechargeMinimum: int.Parse(recharge.Groups["min"].Value, CultureInfo.InvariantCulture));
        }

        if (PerDayPattern().Match(name) is { Success: true } perDay)
        {
            return new UsageLimit(
                UsageLimitKind.PerDay,
                UsesPerDay: int.Parse(perDay.Groups["uses"].Value, CultureInfo.InvariantCulture));
        }

        return null;
    }

    private static string StripUsage(string name) => UsageSuffix().Replace(name, string.Empty).Trim();

    /// <summary>Parses "Trigger: ... Response: ...".</summary>
    private static ReactionEffect? ParseReaction(string text, EntryCoverage coverage)
    {
        var match = ReactionPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        // Only the two literal labels are claimed. The trigger and response prose is
        // stored verbatim on ReactionEffect for narration, but no resolver executes a
        // reaction (Encounter has none), so storing it is not expressing it (design
        // §2.2) and it is left as residue.
        coverage.Claim(new TextSpan(match.Index, "Trigger:".Length), "reaction.trigger_label");

        var responseIndex = text.IndexOf("Response:", match.Index, StringComparison.Ordinal);

        if (responseIndex >= 0)
        {
            coverage.Claim(new TextSpan(responseIndex, "Response:".Length), "reaction.response_label");
        }

        return new ReactionEffect(match.Groups["trigger"].Value.Trim(), match.Groups["response"].Value.Trim());
    }

    /// <summary>
    /// Parses "The bandit makes two attacks, using Scimitar and Pistol in any
    /// combination." and "The armor makes two Slam attacks."
    /// </summary>
    private static MultiattackEffect? ParseMultiattack(string text) =>
        ParseMultiattack(text, new EntryCoverage(text), out _);

    /// <summary>
    /// The same parse, plus whatever alternative-composition text was set aside.
    /// </summary>
    /// <param name="alternativeClause">
    /// Empty unless <paramref name="text"/> printed a second composition joined by "or
    /// it/he/she/they makes" (the Clay Golem's "or it makes three Slam attacks if it
    /// used Hasten this turn"). When set, it holds that clause verbatim so the caller
    /// can record it rather than lose it — see the remarks below.
    /// </param>
    /// <remarks>
    /// <para>
    /// A composition can print two whole alternatives rather than one: "the golem makes
    /// two Slam attacks, or it makes three Slam attacks if it used Hasten this turn."
    /// Before this fix, the "every &lt;count&gt; &lt;Name&gt; attack(s) clause, summed"
    /// rule below read both clauses and recorded five Slams — a real engine bug, not
    /// just an accounting one, since <c>AttackCount</c> drives how many the creature
    /// actually swings. The Barbed Devil and Medusa print the same shape.
    /// </para>
    /// <para>
    /// This is a choice the model does not make (which branch, or — for the Golem —
    /// state the model does not track at all, whether Hasten was used this turn), so the
    /// designer's standing reading applies: take the first-printed, unconditional branch
    /// as the recorded composition, and account the alternative rather than approximate
    /// it. The alternative is deliberately not folded into <see cref="MechanicalSentences"/>
    /// or <see cref="LeftoverMechanicalSentences"/> — the whole entry is one sentence in
    /// all three cases, so sentence-level splitting would not find it, and it would go
    /// the way of the Kraken's bundled "and uses Fling..." (#341) if this method did not
    /// hand it back explicitly.
    /// </para>
    /// </remarks>
    private static MultiattackEffect? ParseMultiattack(string text, EntryCoverage coverage, out string alternativeClause)
    {
        alternativeClause = string.Empty;

        var alternative = AlternativeCompositionPattern().Match(text);
        if (alternative.Success)
        {
            // Deliberately not claimed (design §7.4): the model does not express the
            // second branch — the standing designer reading on this method — so it
            // falls out as residue rather than being absorbed by a glue entry for
            // "alternatives", which would be the keyword-filter bug in a new shape.
            alternativeClause = text[alternative.Index..].TrimStart(',', ' ');
            text = text[..alternative.Index];
        }

        // This method has several exit points that turn out not to be a Multiattack at
        // all (the Hydra's "as many Bite attacks as it has heads" has no fixed count
        // and falls through to Unmodelled). Claims accumulate here, into a throwaway
        // coverage, and are absorbed into the caller's real one only on a successful
        // return — an abandoned attempt must not leak a claim into an entry graded
        // Unmodelled, which claims nothing at all (design §2.7).
        var pending = new EntryCoverage(text);

        // The composition clause's own subject and verb — "The mummy makes" —
        // anchored at the entry's start rather than bridged to with a wildcard
        // (design §2.3's corollary and §7.4): NamedMultiattackPattern only ever finds
        // "two Rotting Fist attacks" and says nothing about who makes it, so without
        // this anchor every Multiattack entry would emit its own subject as residue.
        var subject = MultiattackSubjectPattern().Match(text);
        if (subject.Success)
        {
            pending.Claim(MultiattackSubjectPattern(), subject, "multiattack.subject");
        }

        // "... makes two attacks, using Scimitar and Pistol in any combination."
        var combination = CombinationMultiattackPattern().Match(text);
        if (combination.Success && WordToNumber(combination.Groups["count"].Value) is { } count)
        {
            // The "attacks" group's content is consumed whole into AttackNames, so
            // nothing here is excluded from the claim.
            pending.Claim(new TextSpan(combination.Index, combination.Length), "multiattack.combination");
            coverage.Absorb(pending);

            return new MultiattackEffect(count, SplitAttackNames(combination.Groups["attacks"].Value), true);
        }

        // Every "<count> <Name> attack(s)" clause, summed.
        //
        // Matching only the first was wrong twice over: the Bearded Devil "makes one
        // Beard attack and one Infernal Glaive attack", which is two attacks and not
        // one, and the Bugbear Stalker "makes two Javelin or Morningstar attacks", whose
        // name is a choice rather than a weapon called "Javelin or Morningstar".
        // Anchored on the entry actually describing attacks being made, because the
        // clause pattern below deliberately does not require "makes" — the Bearded Devil
        // "makes one Beard attack and one Infernal Glaive attack", and the second clause
        // has no verb of its own.
        if (!text.Contains("makes", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var total = 0;
        var names = new List<string>();

        foreach (Match match in NamedMultiattackPattern().Matches(text))
        {
            if (WordToNumber(match.Groups["count"].Value) is not { } clauseCount)
            {
                continue;
            }

            total += clauseCount;

            foreach (var name in SplitAttackNames(match.Groups["attack"].Value))
            {
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }

            pending.Claim(new TextSpan(match.Index, match.Length), "multiattack.count_clause");

            // A second (or later) composition clause carries its own "makes" — the
            // Roper's ", and makes two Bite attacks" after its first "makes two
            // Tentacle attacks" — which the subject anchor above never reaches. Claimed
            // only when "makes" is bridged to this clause by glue alone (design §7.4:
            // "adjacency is judged modulo glue"); a "makes" separated by real words —
            // the Bearded Devil's second clause has none of its own — is left alone.
            var adjacentMakes = AdjacentMakesPattern().Match(text[..match.Index]);
            if (adjacentMakes.Success)
            {
                pending.Claim(new TextSpan(adjacentMakes.Index, adjacentMakes.Length), "multiattack.adjacent_makes");
            }
        }

        if (total < 2 || names.Count == 0)
        {
            return null;
        }

        coverage.Absorb(pending);

        // Several named attacks means the creature picks between them, whether the text
        // said "in any combination" or listed them as alternatives.
        return new MultiattackEffect(total, names, names.Count > 1);
    }

    /// <summary>
    /// Bundled uses folded into the same sentence as the composition: "and uses Dreadful
    /// Glare", "or uses Holy Burst twice", ", uses Reel,". Returns each verbatim, ready
    /// to record as an unmodelled clause.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Mummy prints "The mummy makes two Rotting Fist attacks and uses Dreadful
    /// Glare." — one sentence, and <see cref="DescribesTheComposition"/> matches it in
    /// full, because <see cref="NamedMultiattackPattern"/> finds exactly the "two
    /// Rotting Fist attacks" the recorded composition expects and says nothing about the
    /// rest. The composition sentence itself is genuinely fully expressed; "uses
    /// Dreadful Glare" is a second rule riding inside it, the same way a rider condition
    /// can ride inside an attack's Hit clause. Before this fix it vanished with an empty
    /// <c>UnmodelledClauses</c> — the fourteenth-and-counting instance of the goblin's
    /// conditional-damage bug, and the reason #341 exists.
    /// </para>
    /// <para>
    /// The connector before "uses"/"can use" is what marks a bundled use rather than
    /// part of the composition: a bare comma (the Roper's "makes two Tentacle attacks,
    /// uses Reel, and makes two Bite attacks" — the bundled use sits between two
    /// composition clauses), or "and"/"or" (the Kraken's "and uses Fling, Lightning
    /// Strike, or Swallow", the Planetar's "or uses Holy Burst twice"). The lazy capture
    /// stops at the next composition clause (", and makes ...", the Roper's case again)
    /// or at the sentence's end — not at every comma, because a bundled use can itself
    /// print a list ("Fling, Lightning Strike, or Swallow").
    /// </para>
    /// </remarks>
    private static IEnumerable<string> BundledMultiattackUseClauses(string text)
    {
        foreach (Match match in BundledUsePattern().Matches(text))
        {
            var connector = match.Groups["connector"].Value;
            var clause = match.Groups["clause"].Value.Trim();

            yield return connector switch
            {
                "and" => $"And {clause}.",
                "or" => $"Or {clause}.",
                _ => CapitalizeFirst(clause) + ".",
            };
        }
    }

    private static IReadOnlyList<string> SplitAttackNames(string text) => text
        .Split([" and ", " or ", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(candidate => candidate.Length > 0)
        .ToArray();

    /// <summary>
    /// Parses "Dexterity Saving Throw: DC 12, each creature in a 30-foot Cone.
    /// Failure: 14 (4d6) Acid damage. Success: Half damage."
    /// </summary>
    private static SaveEffect? ParseSave(string text, IReadOnlyList<AppliedCondition> conditions, EntryCoverage coverage)
    {
        var header = SaveHeaderPattern().Match(text);
        if (!header.Success)
        {
            return null;
        }

        // Fully literal — the ability-name alternation, "Saving Throw: DC", the digits
        // — nothing permissive in this pattern to exclude.
        coverage.Claim(new TextSpan(header.Index, header.Length), "save.header");

        var ability = Enum.Parse<Ability>(header.Groups["ability"].Value, ignoreCase: true);
        var dc = int.Parse(header.Groups["dc"].Value, CultureInfo.InvariantCulture);

        // The clause naming who rolls — "each creature in a 30-foot Cone", "one
        // creature" — claimed exactly as far as UseSaveEntry's own targeting reaches
        // (design §7.6). Anchored at both ends: it must start immediately after the
        // header (checked below, since Match(text, start) searches from there rather
        // than requiring it) and each shape ends at its own literal last word, so a
        // clause carrying a printed distance, a sight requirement, a gate or an
        // exclusion fails to match rather than matching the part that looks familiar
        // — every one of those qualifiers is a rule this engine does not enforce and
        // is left to residue.
        var target = SaveTargetClausePattern().Match(text, header.Index + header.Length);
        if (target.Success && target.Index == header.Index + header.Length)
        {
            coverage.Claim(SaveTargetClausePattern(), target, "save.target_clause");
        }

        var failureIndex = text.IndexOf("Failure", StringComparison.Ordinal);
        IReadOnlyList<AttackDamage> failureDamage;

        if (failureIndex < 0)
        {
            failureDamage = [];
        }
        else
        {
            // The bare word — what ParseSave's own code keys on
            // (EntryMechanicsParser.cs, text.IndexOf("Failure", ...)) — not the colon
            // that follows it, so "Failure or Success:" still fails the glue test on
            // its own "Success" (#370's side clause is unaffected either way).
            coverage.Claim(new TextSpan(failureIndex, "Failure".Length), "save.failure_label");
            failureDamage = ParseDamageList(text, failureIndex, coverage);
        }

        var success = text.Contains("Failure or Success:", StringComparison.Ordinal)
            ? SaveSuccessOutcome.SameAsFailure
            : text.Contains("Success: Half damage", StringComparison.OrdinalIgnoreCase)
                ? SaveSuccessOutcome.HalfDamage
                : SaveSuccessOutcome.NoEffect;

        if (success == SaveSuccessOutcome.HalfDamage)
        {
            var halfDamageIndex = text.IndexOf("Success: Half damage", StringComparison.OrdinalIgnoreCase);

            if (halfDamageIndex >= 0)
            {
                coverage.Claim(new TextSpan(halfDamageIndex, "Success: Half damage".Length), "save.success_half");
            }
        }

        return new SaveEffect(ability, dc, ParseArea(text, coverage), failureDamage, success, conditions);
    }

    /// <summary>Parses "30-foot Cone", "30-foot-long, 5-foot-wide Line", "5-foot Emanation".</summary>
    private static EffectArea? ParseArea(string text, EntryCoverage coverage)
    {
        var match = AreaPattern().Match(text);

        if (!match.Success || !Enum.TryParse<AreaShape>(match.Groups["shape"].Value, ignoreCase: true, out var shape))
        {
            return null;
        }

        // Fully literal but for the digits it already reads into structure — nothing
        // permissive to exclude.
        coverage.Claim(new TextSpan(match.Index, match.Length), "save.area");

        return new EffectArea(
            shape,
            int.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture),
            match.Groups["width"].Success
                ? int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture)
                : null);
    }

    /// <summary>
    /// Reads every "N (XdY) Type damage" in a clause, starting at <paramref name="start"/>
    /// and running to the end of the sentence. Operates on the whole entry's text with a
    /// start offset, rather than a pre-sliced substring, so every match's own
    /// <c>Index</c> is already an offset into that text.
    /// </summary>
    private static IReadOnlyList<AttackDamage> ParseDamageList(string text, int start, EntryCoverage coverage)
    {
        var sentenceEnd = text.IndexOf('.', start);
        var clauseEnd = sentenceEnd < 0 ? text.Length : sentenceEnd;

        var damage = new List<AttackDamage>();

        foreach (Match match in SaveDamagePattern().Matches(text[start..clauseEnd]))
        {
            if (!Enum.TryParse<DamageType>(match.Groups["type"].Value, ignoreCase: true, out var type))
            {
                continue;
            }

            var average = int.Parse(match.Groups["average"].Value, CultureInfo.InvariantCulture);

            var dice = match.Groups["dice"].Success && DiceExpression.TryParse(match.Groups["dice"].Value, out var rolled)
                ? rolled
                : DiceExpression.Flat(average);

            // SaveDamagePattern is fully literal, exactly like the attack grammar's
            // own DamagePattern — nothing permissive to exclude.
            coverage.Claim(new TextSpan(start + match.Index, match.Length), "save.damage_component");

            damage.Add(new AttackDamage(dice, type, average));
        }

        return damage;
    }

    /// <summary>
    /// Finds every condition the entry imposes, with its escape DC, its size gate, and
    /// whatever else was printed with it that the model cannot express.
    /// </summary>
    private static IReadOnlyList<AppliedCondition> ParseAppliedConditions(
        string text,
        EntryCoverage coverage,
        bool attackEntry = false) =>
        ParseAppliedConditionsWithClaims(text, coverage, attackEntry).Conditions;

    /// <summary>
    /// Finds every condition, and separately the span each fully-modelled one would
    /// claim if the entry's own mechanics turns out to be one the engine imposes
    /// riders from (design §2.5) — <see cref="EntryMechanics.Attack"/> and
    /// <see cref="EntryMechanics.SavingThrow"/> only.
    /// </summary>
    /// <remarks>
    /// This is called before <c>Classify</c>/<c>ClassifyTrait</c> know which branch the
    /// entry will resolve to — <c>Multiattack</c>, <c>Reaction</c> and
    /// <c>Unmodelled</c> all still carry <c>conditions</c> on the returned entry, but
    /// none of them ever has a rider imposed by <c>Encounter.UseEntry</c>, so claiming
    /// a rider's text there would be a false claim under §2.2. The caller commits
    /// <c>ClaimableRiders</c> into its own <see cref="EntryCoverage"/> only once it
    /// knows which branch it is building.
    /// </remarks>
    private static (IReadOnlyList<AppliedCondition> Conditions, IReadOnlyList<(AppliedCondition Condition, TextSpan Span)> ClaimableRiders)
        ParseAppliedConditionsWithClaims(
        string text,
        EntryCoverage coverage,
        bool attackEntry = false)
    {
        var conditions = new List<AppliedCondition>();
        var claimableRiders = new List<(AppliedCondition Condition, TextSpan Span)>();

        // The two-tier gaze — the one "First Failure: ... Second Failure: ..." pair the
        // model expresses, carved as an exact template the way Hold Person's clock was:
        // the corpus prints this wording three times, on the Basilisk's, the Medusa's
        // and the Gorgon's Petrifying Breath's Petrifying Gaze, and it structures as a
        // single escalating rider — Restrained, repeated at the end of the bearer's
        // next turn, ending on a success and deepening to Petrified on the failure. The
        // matched sentences are claimed and masked out of the text the general pass
        // below reads, whose tiered-failure rule would otherwise rightly refuse them;
        // any tiered sentence that does not match this template to the letter still
        // falls to that rule.
        if (text.Contains(PetrifyingTierSentences, StringComparison.Ordinal))
        {
            conditions.Add(new AppliedCondition(
                ConditionType.Restrained,
                Duration: ConditionDuration.UntilSavedOrEscalated,
                EscalatesTo: ConditionType.Petrified));

            var petrifyingIndex = text.IndexOf(PetrifyingTierSentences, StringComparison.Ordinal);
            coverage.Claim(
                new TextSpan(petrifyingIndex, PetrifyingTierSentences.Length),
                "condition.petrifying_tier");
            text = coverage.Masked;
        }

        // Sentence by sentence, because the rider's gate and its duration are the words
        // on either side of the condition within its own sentence, and nowhere else. A
        // sentence imposing two conditions — "... it has the Grappled condition (escape
        // DC 19), and it has the Restrained condition until the grapple ends" — is split
        // into one clause per rider first, so each condition is judged on its own words
        // rather than the first tripping over the second's clause as trailing text.
        //
        // The split carries spans and is walked by index rather than foreach, because
        // ReadRider needs to see one sentence ahead: the Quasit's printed form is two
        // separate sentences ("Failure: The target has the Frightened condition." then
        // "At the end of each of its turns, the target repeats the save, ending the
        // effect on itself on a success."), and the annex rule (design §5.2) reads the
        // second without rewriting the text to join them first.
        var sentences = SplitSentencesWithSpans(text).ToArray();

        for (var sentenceIndex = 0; sentenceIndex < sentences.Length; sentenceIndex++)
        {
            var (sentence, sentenceSpan) = sentences[sentenceIndex];
            var nextSentence = sentenceIndex + 1 < sentences.Length ? sentences[sentenceIndex + 1] : ((string Text, TextSpan Span)?)null;

            var added = new List<AppliedCondition>();
            var addedSpans = new List<TextSpan?>();
            var clauses = RiderClausePattern().Split(sentence);

            // A head clause carrying no rider may stand before the riders only when the
            // entry's other grammar accounts for it in full — a "Hit:" or "Failure:"
            // damage clause. Anything else — "the balor pulls the target up to 25 feet
            // straight toward itself", "the target becomes Stable" — is a companion
            // effect the model does not express, and imposing the riders without it
            // would fire part of a printed sentence. Every rider in the sentence is
            // refused with it.
            var unmodelledCompanion = clauses.Length > 1
                && !ConditionPattern().IsMatch(clauses[0])
                && HasResidualEffect(clauses[0]);

            // Each clause is a verbatim piece of `sentence` (RiderClausePattern's own
            // separator is a comma-and-lookahead split, consuming no clause text), so
            // its absolute start is found by searching forward from a running cursor
            // — the same trick SplitSentencesWithSpans' own spans are built on.
            var clauseCursor = 0;

            foreach (var clause in clauses)
            {
                var clauseOffset = sentence.IndexOf(clause, clauseCursor, StringComparison.Ordinal);
                var clauseAbsoluteStart = clauseOffset >= 0 ? sentenceSpan.Start + clauseOffset : sentenceSpan.Start;

                if (clauseOffset >= 0)
                {
                    clauseCursor = clauseOffset + clause.Length;
                }

                foreach (Match match in ConditionPattern().Matches(clause))
                {
                    if (!Enum.TryParse<ConditionType>(match.Groups["condition"].Value, ignoreCase: true, out var condition))
                    {
                        continue;
                    }

                    if (conditions.Any(existing => existing.Condition == condition)
                        || added.Any(existing => existing.Condition == condition))
                    {
                        continue;
                    }

                    int? escapeDc = match.Groups["escape"].Success
                        ? int.Parse(match.Groups["escape"].Value, CultureInfo.InvariantCulture)
                        : null;

                    var (size, duration, unmodelled, claimableSpan) = unmodelledCompanion
                        ? (null, null, sentence, (TextSpan?)null)
                        : ReadRider(sentence, clause, match, attackEntry, text, nextSentence, coverage, clauseAbsoluteStart);

                    added.Add(new AppliedCondition(condition, escapeDc, size, duration, unmodelled));
                    addedSpans.Add(claimableSpan);
                }
            }

            // "until the grapple ends" is a tie to the sibling grapple, so it is only as
            // modelled as the grapple it hangs off. When the Grappled rider in the same
            // sentence is refused — "from one of ten tentacles" is limb bookkeeping the
            // model does not express — the dependent rider must not ride a grapple that
            // can never land, and is refused whole-sentence with it.
            var grappled = added.FirstOrDefault(rider => rider.Condition == ConditionType.Grappled);

            for (var i = 0; i < added.Count; i++)
            {
                if (added[i].Duration is { WhileGrappleHolds: true } && grappled is not { IsFullyModelled: true })
                {
                    added[i] = added[i] with { Duration = null, UnmodelledRequirement = sentence };
                    addedSpans[i] = null;
                }
            }

            // Only a rider the engine will actually impose is claimable (design §2.5):
            // fully modelled, and on ConditionRules' own executable allowlist —
            // exactly ConditionRules.CanBeImposed's own test, checked here on the
            // rider as the grapple-tie sweep above left it.
            for (var i = 0; i < added.Count; i++)
            {
                if (addedSpans[i] is { } span && ConditionRules.CanBeImposed(added[i]))
                {
                    claimableRiders.Add((added[i], span));
                }
            }

            conditions.AddRange(added);
        }

        return (conditions, claimableRiders);
    }

    /// <summary>
    /// Reads what is printed on either side of a condition: the size gate in front of it,
    /// and whatever is left over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately strict, and strict in the safe direction. The clause leading into the
    /// condition has to reduce to nothing more than an optional size gate, and nothing may
    /// follow the condition at all. Everything else — a charge requirement, a duration, a
    /// pull, a second condition chained onto the first — comes back as the whole sentence,
    /// which stops the rider being imposed and keeps it counted.
    /// </para>
    /// <para>
    /// Recognising these approximately is the failure to avoid. "If the target is a Large
    /// or smaller creature <b>and the gorgon moved 20+ feet straight toward it</b>" is not
    /// a size gate with decoration; treating it as one knocks targets Prone on every hit
    /// instead of on a charge.
    /// </para>
    /// </remarks>
    private static (CreatureSize? Size, ConditionDuration? Duration, string? Unmodelled, TextSpan? ClaimableSpan) ReadRider(
        string sentence,
        string clause,
        Match condition,
        bool attackEntry,
        string entryText,
        (string Text, TextSpan Span)? nextSentence,
        EntryCoverage coverage,
        int clauseAbsoluteStart)
    {
        var trailing = clause[(condition.Index + condition.Length)..].Trim().TrimEnd('.').Trim();
        var duration = ParseDuration(trailing);

        // "and repeats the save at the end of each of its turns, ending the effect on
        // itself on a success" — the way out inside the rider's own sentence, modelled
        // since the repeat-save slice. This is the Doppelganger's printing: one
        // sentence, the escape already joined onto the rider by the SRD itself. Only
        // alongside the printed cap: without "After 1 minute, it succeeds
        // automatically." somewhere in the entry, the repeat has no clock and the
        // conservative answer is still refusal.
        if (duration is null
            && RepeatSaveTrailingPattern().IsMatch(trailing)
            && entryText.Contains(AutomaticSuccessSentence, StringComparison.Ordinal))
        {
            duration = ConditionDuration.RepeatSaveUpToOneMinute;
        }

        // The Quasit's printing instead: two separate sentences — "Failure: The target
        // has the Frightened condition." then, on its own, "At the end of each of its
        // turns, the target repeats the save, ending the effect on itself on a
        // success." Sentence-scoped parsing cannot attach the second to the rider it
        // frees, so this rider's own trailing text has to be empty (design §5.2's
        // annex rule — "empty" rather than "carries no duration", so a rider that
        // trails off with an early out the model cannot express, e.g. "for 1 minute,
        // until it takes damage", still refuses rather than annexing a sentence that
        // does not belong to it). When every condition holds, the claim annexes the
        // next sentence's own span rather than rewriting the text to join them —
        // there is only ever one coordinate space (design §5).
        if (duration is null
            && trailing.Length == 0
            && sentence.Contains("Failure:", StringComparison.Ordinal)
            && nextSentence is { } next
            && next.Text.TrimEnd('.') == RepeatSaveStandaloneSentence
            && entryText.Contains(AutomaticSuccessSentence, StringComparison.Ordinal))
        {
            duration = ConditionDuration.RepeatSaveUpToOneMinute;
            coverage.Claim(next.Span, "condition.repeat_save_annex");
        }

        // RepeatSaveUpToOneMinute's engine meaning is the ten-turn cap the printed
        // "After 1 minute, it succeeds automatically." sentence states, whichever of
        // the two shapes above produced it — so that sentence is the duration's own
        // clause, not a second rule left over, and its span is claimed too.
        if (duration is { RepeatSaveAtTurnEnd: true })
        {
            var automaticSuccessIndex = entryText.IndexOf(AutomaticSuccessSentence, StringComparison.Ordinal);

            if (automaticSuccessIndex >= 0)
            {
                coverage.Claim(
                    new TextSpan(automaticSuccessIndex, AutomaticSuccessSentence.Length),
                    "condition.repeat_save_cap");
            }
        }

        // Anything after the condition that is not a duration this engine can run — "from
        // one of two claws", "until the web is destroyed", "at which point it repeats the
        // save" — is a rule of its own, and the rider is unusable until it is modelled.
        if (trailing.Length > 0 && duration is null)
        {
            return (null, null, sentence, null);
        }

        // "Second Failure: The target has the Unconscious condition for 1 minute." — a
        // save outcome tier the save model does not express. The engine imposes riders
        // on the plain failure, so a rider printed behind a deeper tier would land a
        // whole tier early — the Brass Dragon Wyrmling's sleep on a first failed save.
        if (TieredFailurePattern().IsMatch(sentence))
        {
            return (null, null, sentence, null);
        }

        // The label rules look at the whole sentence, not the clause: a label always
        // precedes the riders it governs, whichever clause they sit in.
        if (sentence.Contains("Failure:", StringComparison.Ordinal))
        {
            // A "Failure:" sentence inside an attack entry belongs to an embedded
            // saving throw — the Ghast's Claw carries "Constitution Saving Throw: DC
            // 10. Failure: The target has the Paralyzed condition ..." — and its DC and
            // its "non-Undead creature" gate live in sentences of their own. Riding the
            // attack with it would paralyze on every hit with no save rolled.
            if (attackEntry)
            {
                return (null, null, sentence, null);
            }

            // And a "Failure:" rider must state its end within its own sentence. When
            // it does not, the end is almost always printed separately — the Quasit's
            // "Failure: The target has the Frightened condition." is followed by "At
            // the end of each of its turns, the target repeats the save ..." — and
            // sentence-scoped parsing cannot attach it, so imposing the rider would
            // make the condition permanent. A duration-less rider in a sentence of its
            // own — the Gladiator's "If the target is a Medium or smaller creature, it
            // has the Prone condition." — is untouched by this: those conditions carry
            // their own printed way out.
            if (duration is null)
            {
                return (null, null, sentence, null);
            }
        }

        var beforeCondition = clause[..condition.Index];
        var failure = beforeCondition.LastIndexOf("Failure:", StringComparison.Ordinal);

        var leading = failure >= 0
            ? StripDamage(beforeCondition[(failure + "Failure:".Length)..])
            : StripAttackPreamble(beforeCondition);
        var gate = RiderLeadInPattern().Match(leading);

        if (!gate.Success)
        {
            return (null, null, sentence, null);
        }

        // The claim runs from the start of this gate match — which RiderLeadInPattern
        // anchors to the whole of `leading`, so its own start coincides with where
        // `leading` itself starts within the clause — through the end of the clause,
        // covering the size gate, the condition and its duration whole (design §7.3).
        // `leading`'s own length is what locates its start: it always ends exactly
        // where the condition match begins, whichever of StripDamage or
        // StripAttackPreamble produced it.
        var claimStart = clauseAbsoluteStart + condition.Index - leading.Length;
        var claimEnd = clauseAbsoluteStart + clause.Length;
        var claimableSpan = claimEnd > claimStart ? new TextSpan(claimStart, claimEnd - claimStart) : (TextSpan?)null;

        return gate.Groups["size"].Success
            && Enum.TryParse<CreatureSize>(gate.Groups["size"].Value, ignoreCase: true, out var size)
                ? (size, duration, null, claimableSpan)
                : (null, duration, null, claimableSpan);
    }

    /// <summary>
    /// Parses "until the start of its next turn", "until the end of the devil's next
    /// turn", "for 1 minute" and "for 1 hour".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The possessive is the whole of the turn-boundary distinction. "its" is the
    /// creature carrying the condition; a name is the creature that imposed it, and
    /// every printed name in the bestiary is the stat block's own creature. Reading one
    /// as the other moves the end of the condition by most of a round.
    /// </para>
    /// <para>
    /// A timed duration must be the whole of the trailing text, exactly like a
    /// turn-boundary one: "for 1 minute, until it takes damage, or ..." carries an early
    /// out the model cannot express, and the anchored pattern refuses it rather than
    /// matching the part that looks familiar.
    /// </para>
    /// </remarks>
    private static ConditionDuration? ParseDuration(string trailing)
    {
        var boundary = TurnBoundaryDurationPattern().Match(trailing);

        if (boundary.Success)
        {
            var clock = boundary.Groups["when"].Value.Equals("start", StringComparison.OrdinalIgnoreCase)
                ? ConditionClock.StartOfTurn
                : ConditionClock.EndOfTurn;

            var owner = boundary.Groups["bearer"].Success
                ? ConditionDurationOwner.Bearer
                : ConditionDurationOwner.Source;

            return new ConditionDuration(clock, owner);
        }

        var timed = TimedDurationPattern().Match(trailing);

        if (timed.Success)
        {
            var count = int.Parse(timed.Groups["count"].Value, CultureInfo.InvariantCulture);

            return timed.Groups["unit"].Value.StartsWith("minute", StringComparison.OrdinalIgnoreCase)
                ? ConditionDuration.ForMinutes(count)
                : ConditionDuration.BeyondTheFight;
        }

        return GrappleEndDurationPattern().IsMatch(trailing)
            ? ConditionDuration.UntilTheGrappleEnds
            : null;
    }

    /// <summary>
    /// Drops the part of a sentence the attack grammar has already accounted for, so what
    /// is examined is the rider alone.
    /// </summary>
    /// <remarks>
    /// A rider is often joined to its attack by a comma rather than a full stop — "Hit: 7
    /// (1d8 + 3) Piercing damage, and the target has the Poisoned condition ..." — so the
    /// sentence splitter cannot separate them and this has to. Cutting after the last
    /// printed damage leaves exactly the words between the attack and the condition.
    /// </remarks>
    private static string StripAttackPreamble(string leading)
    {
        var hit = leading.LastIndexOf("Hit:", StringComparison.Ordinal);

        return hit < 0 ? leading : StripDamage(leading[(hit + "Hit:".Length)..]);
    }

    /// <summary>
    /// Cuts after the last printed damage, leaving exactly the words between the effect
    /// and the condition — shared by the "Hit:" and "Failure:" preambles, whose damage
    /// reads the same: "Failure: 10 (2d6 + 3) Psychic damage, and the target has ...".
    /// </summary>
    private static string StripDamage(string afterLabel)
    {
        var lastDamage = afterLabel.LastIndexOf("damage", StringComparison.Ordinal);

        return lastDamage < 0 ? afterLabel : afterLabel[(lastDamage + "damage".Length)..];
    }

    /// <summary>
    /// Whether a rider-free clause carries anything beyond a "Hit:" or "Failure:" damage
    /// statement — an effect of its own that the model does not express.
    /// </summary>
    private static bool HasResidualEffect(string clause)
    {
        var failure = clause.LastIndexOf("Failure:", StringComparison.Ordinal);

        var reduced = failure >= 0
            ? StripDamage(clause[(failure + "Failure:".Length)..])
            : StripAttackPreamble(clause);

        return reduced.Trim().TrimEnd('.', ',', ';').Trim().Length > 0;
    }

    /// <summary>The conditions a sentence carries, out of those parsed from the whole entry.</summary>
    /// <summary>
    /// The exact printed pair the escalating-rider template matches — the Basilisk's
    /// and the Medusa's Petrifying Gaze, word for word. Anchored to the letter so a
    /// third tiered wording arriving from a new direction is refused rather than
    /// approximated.
    /// </summary>
    private const string PetrifyingTierSentences =
        "First Failure: The target has the Restrained condition and repeats the save at the end of its " +
        "next turn if it is still Restrained, ending the effect on itself on a success. " +
        "Second Failure: The target has the Petrified condition instead of the Restrained condition.";

    private static IEnumerable<AppliedCondition> ConditionsIn(
        string sentence,
        IReadOnlyList<AppliedCondition> conditions) =>
        ConditionPattern()
            .Matches(sentence)
            .Select(match => match.Groups["condition"].Value)
            .Select(name => conditions.FirstOrDefault(candidate =>
                string.Equals(candidate.Condition.ToString(), name, StringComparison.OrdinalIgnoreCase)))
            .Where(condition => condition is not null)
            .Select(condition => condition!);

    private static int? WordToNumber(string word) => word.ToLowerInvariant() switch
    {
        "one" => 1,
        "two" => 2,
        "three" => 3,
        "four" => 4,
        "five" => 5,
        "six" => 6,
        _ => int.TryParse(word, CultureInfo.InvariantCulture, out var value) ? value : null,
    };

    [GeneratedRegex(@"\(Recharge\s+(?<min>\d)(?:\s*-\s*\d)?\)")]
    private static partial Regex RechargePattern();

    [GeneratedRegex(@"\(Recharge after")]
    private static partial Regex RechargeRestPattern();

    [GeneratedRegex(@"\((?<uses>\d+)/Day\)")]
    private static partial Regex PerDayPattern();

    [GeneratedRegex(@"\s*\((?:Recharge[^)]*|\d+/Day)\)\s*$")]
    private static partial Regex UsageSuffix();

    [GeneratedRegex(@"Trigger:\s*(?<trigger>.+?)\s*Response:\s*(?<response>.+)$", RegexOptions.Singleline)]
    private static partial Regex ReactionPattern();

    // Deliberately does not require "makes" on each clause: the Bearded Devil "makes one
    // Beard attack and one Infernal Glaive attack", and the second clause has no verb of
    // its own. The caller anchors on the entry containing "makes" at all.
    [GeneratedRegex(@"\b(?<count>one|two|three|four|five|six|\d+)\s+(?<attack>[A-Z][\w' ]*?)\s+attacks?\b")]
    private static partial Regex NamedMultiattackPattern();

    [GeneratedRegex(@"makes\s+(?<count>one|two|three|four|five|six|\d+)\s+attacks?,\s*using\s+(?<attacks>[^.]+?)\s+in any combination")]
    private static partial Regex CombinationMultiattackPattern();

    // The composition sentence's own subject and verb, anchored at the entry's start —
    // every Multiattack entry is one sentence and prints "The <creature> makes" first
    // (design §2.3's corollary, §7.4). The creature name is claimed whole rather than
    // excluded: the anchoring itself, not a new structured field, is what justifies it,
    // exactly as a single-target save's bare "one creature" is claimed under §7.6.
    [GeneratedRegex(@"^The\s+(?<subject>[\w' ]+?)\s+makes\b")]
    private static partial Regex MultiattackSubjectPattern();

    // A later composition clause's own "makes", bridged to it by glue alone — the
    // Roper's ", and makes two Bite attacks" — matched against the text preceding a
    // count clause's own match. The lookahead requires everything after "makes" up to
    // that point to be glue (whitespace, a comma, "and"/"or"); a "makes" separated by
    // real words fails it and is left unclaimed, exactly like the Bearded Devil's
    // second clause, which never had one of its own.
    [GeneratedRegex(@"\bmakes\b(?=[\s,]*(?:and|or)?[\s,]*$)")]
    private static partial Regex AdjacentMakesPattern();

    // "The golem makes two Slam attacks, or it makes three Slam attacks if it used
    // Hasten this turn." Two whole compositions joined by a repeated subject and verb —
    // as opposed to "using X or Y in any combination" or "Claw or Nightmare Ray attacks",
    // where "or" separates names within one composition — are a choice the model does not
    // make, not attacks to be summed. See ParseMultiattack's remarks.
    [GeneratedRegex(@",?\s*or\s+(?:it|he|she|they)\s+makes\s+.*$", RegexOptions.Singleline)]
    private static partial Regex AlternativeCompositionPattern();

    // See BundledMultiattackUseClauses' remarks for why the lazy capture stops where it
    // does: at the next composition clause or the sentence's end, not at every comma.
    [GeneratedRegex(
        @"(?<connector>,|\band\b|\bor\b)\s+(?<clause>(?:uses|can use)\s+.+?)(?=,\s*and\s+makes\b|\.\s*$|$)",
        RegexOptions.Singleline)]
    private static partial Regex BundledUsePattern();

    [GeneratedRegex(@"(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\s+Saving\s+Throw:\s*DC\s*(?<dc>\d+)")]
    private static partial Regex SaveHeaderPattern();

    // The save's target clause, claimed exactly as far as UseSaveEntry's own targeting
    // reaches (design §7.6): the printed comma, then one of the shapes the engine
    // actually builds — an area (ending at the literal word that names it, or "point"
    // for a Sphere, whose own "the <creature> can see within <N> feet" is deliberately
    // left unmatched) or the head noun of a single target, "one creature". No shape
    // reaches past that word: a printed distance, a sight requirement, a size gate, a
    // state or an exclusion is a rule of its own this engine does not enforce, so a
    // clause carrying one fails every branch here rather than matching the part that
    // looks familiar — which is also why the Emanation's origin creature name is read
    // rather than skipped over with a wildcard: "originating from the doppelganger" is
    // the printed anchor to the area's own origin point, one word short of a qualifier.
    //
    // The origin capture is bounded word by word rather than with a lazy [\w' ]+? run
    // to the next punctuation — the Doppelganger's own printing, "originating from the
    // doppelganger that can see the doppelganger", is exactly the trap: a lazy class
    // freely crosses spaces and would expand straight through "that can see the" to
    // reach the period after the second "doppelganger", claiming the sight qualifier
    // design §7.6 says must never be claimed. Each word after the first is admitted
    // only when it is not one of the words that open a qualifying clause in this
    // corpus's own printings — a genuine multi-word name (none currently printed, but
    // the shape a "young red dragon" would take) still matches; a name followed by
    // "that"/"who"/"which"/"within"/"can" stops exactly there.
    [GeneratedRegex(
        @",\s*(?:" +
        @"each\s+creature\s+in\s+a\s+\d+-foot\s+Cone" +
        @"|each\s+creature\s+in\s+a\s+\d+-foot-long,?\s*\d+-foot-?\s?wide\s+Line" +
        @"|each\s+creature\s+in\s+a\s+\d+-foot\s+Emanation\s+originating\s+from\s+the\s+" +
            @"(?<origin>[\w']+(?:\s+(?!(?:that|who|which|within|can)\b)[\w']+)*)" +
        @"|each\s+creature\s+in\s+a\s+\d+-foot-radius\s+Sphere\s+centered\s+on\s+a\s+point\b" +
        @"|one\s+creature\b" +
        @")")]
    private static partial Regex SaveTargetClausePattern();

    [GeneratedRegex(@"(?<size>\d+)-foot(?:-long,?\s*(?<width>\d+)-foot-?\s?wide)?\s+(?<shape>Cone|Line|Emanation|Cube|Sphere|Cylinder)")]
    private static partial Regex AreaPattern();

    [GeneratedRegex(@"(?<average>\d+)\s*(?:\((?<dice>\d+d\d+(?:\s*[+-]\s*\d+)?)\))?\s*(?<type>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)\s+damage")]
    private static partial Regex SaveDamagePattern();

    // What may stand between the attack's structured part and the condition: nothing, a
    // conjunction, or a size gate — and then the subject that receives it. Anchored at
    // both ends on purpose, so a clause carrying anything more fails to match rather than
    // matching the part that looks familiar.
    [GeneratedRegex(
        @"^\s*(?:[,.]?\s*and\s+)?" +
        @"(?:If\s+(?:the\s+)?target\s+is\s+(?:a\s+)?" +
        @"(?<size>Tiny|Small|Medium|Large|Huge|Gargantuan)\s+or\s+smaller(?:\s+creature)?\s*,\s*)?" +
        @"(?:the\s+target|it)\s+has\s+$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RiderLeadInPattern();

    // Anchored at both ends: "until the end of its next turn, at which point it repeats
    // the save" carries a rule this engine has no answer for, and must not match the part
    // that looks familiar.
    [GeneratedRegex(
        @"^until\s+the\s+(?<when>start|end)\s+of\s+(?:(?<bearer>its)|the\s+[\w' ]+?'s)\s+next\s+turn$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TurnBoundaryDurationPattern();

    // Anchored at both ends for the same reason: "for 1 minute, until it takes damage"
    // is an early out the model cannot express, and must not match on the timer alone.
    [GeneratedRegex(@"^for\s+(?<count>\d+)\s+(?<unit>minutes?|hours?|days?)$", RegexOptions.IgnoreCase)]
    private static partial Regex TimedDurationPattern();

    // "Second Failure:" and its kin — a save outcome tier the save model does not
    // express, so a rider printed behind one must not ride the plain failure.
    [GeneratedRegex(@"\b(?:First|Second|Third)\s+Failure:", RegexOptions.IgnoreCase)]
    private static partial Regex TieredFailurePattern();

    // Anchored like every duration: the whole of the trailing text, or nothing.
    [GeneratedRegex(@"^until\s+the\s+grapple\s+ends$", RegexOptions.IgnoreCase)]
    private static partial Regex GrappleEndDurationPattern();

    // Splits a two-condition sentence into one clause per rider: "... it has the
    // Grappled condition (escape DC 19), and it has the Restrained condition until the
    // grapple ends". The lookahead keeps "it has the ..." in the second clause, so each
    // rider still carries its own subject for the lead-in check.
    [GeneratedRegex(@",\s+and\s+(?=(?:the\s+target|it)\s+has\s+the\s+)", RegexOptions.IgnoreCase)]
    private static partial Regex RiderClausePattern();

    [GeneratedRegex(@"the\s+(?<condition>Blinded|Charmed|Deafened|Frightened|Grappled|Incapacitated|Invisible|Paralyzed|Petrified|Poisoned|Prone|Restrained|Stunned|Unconscious)\s+condition(?:\s*\(escape\s+DC\s*(?<escape>\d+)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionPattern();

    // Sentence boundaries, avoiding the abbreviations the SRD actually uses: "5 ft.",
    // "DC 12.", and decimal-free numbers are safe, but "ft." is everywhere.
    [GeneratedRegex(@"(?<!\bft)(?<!\bMr)(?<!\bDr)\.\s+(?=[A-Z0-9])")]
    private static partial Regex SentenceBoundary();

    /// <summary>
    /// Reads the Ghast Claw's whole embedded-save package, or nothing: the printed
    /// creature-type gate, the save line with its DC, and a Failure rider the model
    /// imposes to the letter. Anything less than the whole — the Ghoul's "or elf",
    /// the Cockatrice's failure tiers, a lycanthrope's curse — matches nothing and
    /// stays refused with its sentences counted.
    /// </summary>
    private static (EmbeddedAttackSave Save, TextSpan MatchedSpan)? ParseEmbeddedSave(string text)
    {
        var match = EmbeddedSavePattern().Match(text);

        if (!match.Success
            || !Enum.TryParse<Ability>(match.Groups["ability"].Value, ignoreCase: true, out var ability)
            || !Enum.TryParse<ConditionType>(match.Groups["condition"].Value, ignoreCase: true, out var condition)
            || !Enum.TryParse<CreatureType>(match.Groups["exempt"].Value, ignoreCase: true, out var exempt))
        {
            return null;
        }

        var rider = new AppliedCondition(
            condition,
            Duration: new ConditionDuration(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer));

        if (!ConditionRules.CanBeImposed(rider))
        {
            return null;
        }

        var save = new SaveEffect(
            ability,
            int.Parse(match.Groups["dc"].Value, CultureInfo.InvariantCulture),
            Area: null,
            FailureDamage: [],
            SuccessOutcome: SaveSuccessOutcome.NoEffect,
            AppliedConditions: [rider]);

        return (new EmbeddedAttackSave(save, exempt), new TextSpan(match.Index, match.Length));
    }

    // The Ghast's Claw, whole: gate, save line and Failure rider, at the end of the
    // entry. "non-Undead" is the one gate shape whose exemption the stats can test.
    [GeneratedRegex(
        @"If the target is a non-(?<exempt>[A-Z][a-z]+) creature, it is subjected to the following effect\. " +
        @"(?<ability>[A-Z][a-z]+) Saving Throw: DC (?<dc>\d+)\. " +
        @"Failure: The target has the (?<condition>[A-Z][a-z]+) condition until the end of its next turn\.$")]
    private static partial Regex EmbeddedSavePattern();

    /// <summary>
    /// The printed cap on a repeated save, exactly as the stat blocks phrase it. Its
    /// engine reading is the ten-turn expiry <c>RepeatSaveUpToOneMinute</c> carries.
    /// </summary>
    internal const string AutomaticSuccessSentence = "After 1 minute, it succeeds automatically.";

    /// <summary>
    /// The repeat-save escape printed as a sentence of its own — the Quasit's printing,
    /// two sentences where the Doppelganger's is one. Compared against a sentence's own
    /// text with its trailing period trimmed (design §5.2's annex rule), since the
    /// sentence splitter only leaves that period attached when nothing follows it in
    /// the entry.
    /// </summary>
    private const string RepeatSaveStandaloneSentence =
        "At the end of each of its turns, the target repeats the save, ending the effect on itself on a success";

    // The in-sentence escape, the whole of the rider's trailing text — the
    // Doppelganger's printing, already one sentence.
    [GeneratedRegex(
        @"^and repeats the save at the end of each of its turns, " +
        @"ending the effect on itself on a success$")]
    private static partial Regex RepeatSaveTrailingPattern();
}
