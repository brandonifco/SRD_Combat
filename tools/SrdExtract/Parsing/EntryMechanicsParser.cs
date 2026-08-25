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
    /// way — the census tool's own entry point, and since the stage 4 switchover also
    /// the coverage whose residue becomes the entry's UnmodelledClauses.
    /// </summary>
    internal static TraitEntry ClassifyTrait(string name, string text, bool consultInertList, out EntryCoverage coverage)
    {
        var usage = ParseUsageLimit(name);
        var bareName = StripUsage(name);
        coverage = new EntryCoverage(text);
        var (conditions, claimableRiders) = ParseAppliedConditionsWithClaims(text);

        if (ParseSave(text, conditions, coverage) is { } save)
        {
            // The entry's mechanics is SavingThrow — one of the two the engine
            // imposes riders from (design §2.5) — so every fully-modelled rider's
            // claim (its own clause, and any repeat-save annex/cap or petrifying-tier
            // span) is committed now, not before.
            foreach (var (_, span, note) in claimableRiders)
            {
                coverage.Claim(span, note);
            }

            return new TraitEntry(
                bareName,
                text,
                EntryMechanics.SavingThrow,
                save,
                usage,
                conditions,
                coverage.Residue());
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
    /// way — the census tool's own entry point, and since the stage 4 switchover also
    /// the coverage whose residue becomes the entry's UnmodelledClauses.
    /// </summary>
    internal static MonsterEntry Classify(string name, MonsterEntrySection section, string text, out EntryCoverage coverage)
    {
        var usage = ParseUsageLimit(name);
        var bareName = StripUsage(name);
        coverage = new EntryCoverage(text);
        var attack = StatBlockLineGrammar.ParseAttack(text, coverage);

        // A second, throwaway coverage computes riderText and must reflect only the
        // embedded-save claim. The rider pass below reads text by matching literal
        // labels ("Attack Roll:", "Hit:") that ParseAttack's own claims into
        // `coverage` would mask out from under it if riderText were computed from
        // `coverage` directly — so this stays a separate, narrower mask purely as an
        // input to rider parsing. Residue itself is computed from `coverage` alone
        // (design §10), which already carries the embedded-save claim committed below.
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
            attackEntry: attack is not null);

        if (attack is not null)
        {
            // The entry's mechanics is Attack — one of the two the engine imposes
            // riders from (design §2.5) — so every fully-modelled rider's claim (its
            // own clause, and any repeat-save annex/cap span) is committed now, not
            // before.
            foreach (var (_, span, note) in claimableRiders)
            {
                coverage.Claim(span, note);
            }

            return Build(
                bareName,
                section,
                text,
                EntryMechanics.Attack,
                usage,
                conditions,
                coverage,
                attack: attack);
        }

        if (ParseReaction(text, coverage) is { } reaction)
        {
            return Build(bareName, section, text, EntryMechanics.Reaction, usage, conditions, coverage, reaction: reaction);
        }

        if (ParseMultiattack(text, coverage) is { } multiattack)
        {
            return Build(bareName, section, text, EntryMechanics.Multiattack, usage, conditions, coverage, multiattack: multiattack);
        }

        if (ParseSave(text, conditions, coverage) is { } save)
        {
            // The entry's mechanics is SavingThrow — the other of the two the engine
            // imposes riders from (design §2.5) — but only when Encounter.UseEntry can
            // actually reach it. UseEntry refuses by section before it ever reads
            // Mechanics (entry.not_an_action): a Trait, LegendaryAction or Reaction
            // entry never fires through it, so a rider parsed on one of those sections
            // is imposed by nothing and claiming its span would be the exact false
            // claim design §2.5's own rule forbids for Multiattack — promoted here
            // from a stated, dated exception (five entries, one regeneration) to the
            // rule itself (#373). The section gate applies to the rider claim alone:
            // the save's own header, target clause and damage are still claimed the
            // same as any other SavingThrow entry — the model does express that shape,
            // whichever section prints it — only the condition it would impose is not.
            if (section is MonsterEntrySection.Action or MonsterEntrySection.BonusAction)
            {
                foreach (var (_, span, note) in claimableRiders)
                {
                    coverage.Claim(span, note);
                }
            }

            return Build(bareName, section, text, EntryMechanics.SavingThrow, usage, conditions, coverage, save: save);
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

    /// <summary>
    /// Assembles a structured entry with its residue computed by subtraction from
    /// <paramref name="coverage"/> (design §3, §10 stage 4) — the uncovered characters
    /// left in <paramref name="coverage"/>'s own text once every matcher that
    /// contributed to this entry's mechanics has claimed what it read. The caller
    /// commits every claim — the mechanics-defining match, any imposed riders, the
    /// embedded-save mask — before calling this, so there is nothing left for
    /// <c>Build</c> itself to decide.
    /// </summary>
    private static MonsterEntry Build(
        string name,
        MonsterEntrySection section,
        string text,
        EntryMechanics mechanics,
        UsageLimit? usage,
        IReadOnlyList<AppliedCondition> conditions,
        EntryCoverage coverage,
        MonsterAttack? attack = null,
        SaveEffect? save = null,
        MultiattackEffect? multiattack = null,
        ReactionEffect? reaction = null) =>
        new(
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
            coverage.Residue());

    /// <summary>
    /// Every sentence of an entry the model could not classify at all — used only for
    /// the <see cref="EntryMechanics.Unmodelled"/> fallback, whole-text and unfiltered
    /// rather than computed by subtraction (design §2.7): an <c>Unmodelled</c> entry's
    /// coverage carries no claims by construction (every earlier matcher that could
    /// have contributed either succeeded, in which case it returned through a
    /// different branch, or failed without committing anything), so this and
    /// <c>coverage.Residue()</c> would agree in substance — this form is kept because
    /// it is what the design names as authoritative for this one branch, byte-stable
    /// with the pre-refactor output.
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

    private static IEnumerable<string> SplitSentences(string text) =>
        SentenceBoundary()
            .Split(text)
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0);

    /// <summary>
    /// The same split as <see cref="SplitSentences"/>, with each piece's span into
    /// <paramref name="text"/> alongside it. The rider-parsing loop in
    /// <see cref="ParseAppliedConditionsWithClaims"/> needs this to look one sentence ahead
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
    /// <remarks>
    /// <para>
    /// A composition can print two whole alternatives rather than one: "the golem makes
    /// two Slam attacks, or it makes three Slam attacks if it used Hasten this turn."
    /// Before this was recognised, the "every &lt;count&gt; &lt;Name&gt; attack(s)
    /// clause, summed" rule below read both clauses and recorded five Slams — a real
    /// engine bug, not just an accounting one, since <c>AttackCount</c> drives how many
    /// the creature actually swings. The Barbed Devil and Medusa print the same shape.
    /// </para>
    /// <para>
    /// This is a choice the model does not make (which branch, or — for the Golem —
    /// state the model does not track at all, whether Hasten was used this turn), so the
    /// designer's standing reading applies: take the first-printed, unconditional branch
    /// as the recorded composition. <paramref name="text"/> is sliced to just that
    /// branch for the rest of this parse, but the slice is local — the alternative text
    /// is never claimed against <paramref name="coverage"/> (design §7.4: "the model
    /// does not express the second branch"), so it falls out as residue by subtraction
    /// rather than through any hand-back this method used to make.
    /// </para>
    /// </remarks>
    private static MultiattackEffect? ParseMultiattack(string text, EntryCoverage coverage)
    {
        var alternative = AlternativeCompositionPattern().Match(text);
        if (alternative.Success)
        {
            // Deliberately not claimed (design §7.4): the model does not express the
            // second branch — the standing designer reading on this method — so it
            // falls out as residue rather than being absorbed by a glue entry for
            // "alternatives", which would be the keyword-filter bug in a new shape.
            // The slice below only narrows what this parse *reads*; every offset
            // computed against the narrowed `text` is still a valid offset into the
            // original entry text, since slicing only ever removes a suffix.
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
            // nothing here is excluded from the claim. Routed through the
            // Claim(Regex, Match, ...) overload — with no unread groups, so the whole
            // match is still claimed exactly as before — so this pattern's own
            // permissive [^.]+? registers with the wildcard-convention scan (design
            // §2.3) the same as every other claiming pattern, rather than escaping it
            // by claiming through the raw-span overload alone.
            pending.Claim(CombinationMultiattackPattern(), combination, "multiattack.combination");
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

            // Routed through Claim(Regex, Match, ...) for the same reason as
            // CombinationMultiattackPattern above — this pattern's own [\w' ]*? is one
            // of §2.3's own motivating examples, and claiming it only through the raw
            // TextSpan overload would leave it permanently unscanned.
            pending.Claim(NamedMultiattackPattern(), match, "multiattack.count_clause");

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

        // The tier attaches to the clause it governs, not the whole entry (#370). A
        // printed "Failure or Success:" clause is, in every one of the 24 entries that
        // print it, a side effect layered on top of the entry's own Failure/Success
        // tier — "Being underwater doesn't grant Resistance to this Fire damage", "The
        // dragon can't take this action again until the start of its next turn" — never
        // a restatement of the Failure damage itself. Reading it as `Contains` anywhere
        // in the text let a trailing side clause override an unrelated, already-printed
        // "Success: Half damage" into `SameAsFailure`, so a successful save dealt full
        // damage instead of half (Steam Mephit's Steam Breath) or full damage instead of
        // none (the sixteen entries with no Success line at all, e.g. the dragons'
        // "can't take this action again" legendary actions). The label that actually
        // governs the printed outcome is `Success:` on its own — anchored exactly as
        // `save.success_half` claims it below — so that is the only signal consulted
        // here. `Failure or Success:` is claimed by nobody (design §4.1) and its side
        // clause is left to residue, same as the Failure-line riders this method has
        // never structured.
        var success = text.Contains("Success: Half damage", StringComparison.OrdinalIgnoreCase)
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
    /// Finds every condition, and separately the spans each fully-modelled one would
    /// claim if the entry's own mechanics turns out to be one the engine imposes
    /// riders from (design §2.5) — <see cref="EntryMechanics.Attack"/> and
    /// <see cref="EntryMechanics.SavingThrow"/> only.
    /// </summary>
    /// <remarks>
    /// This is called before <c>Classify</c>/<c>ClassifyTrait</c> know which branch the
    /// entry will resolve to — <c>Multiattack</c>, <c>Reaction</c> and
    /// <c>Unmodelled</c> all still carry <c>conditions</c> on the returned entry, but
    /// none of them ever has a rider imposed by <c>Encounter.UseEntry</c>, so claiming
    /// a rider's text there would be a false claim under §2.2. This method therefore
    /// takes no <see cref="EntryCoverage"/> of its own and commits nothing: every span
    /// a rider would claim — its own clause, a repeat-save annex, the automatic-success
    /// cap, the petrifying-tier template — travels out through
    /// <c>ClaimableRiders</c> and is committed by the caller into its own coverage only
    /// once it knows which branch it is building. A claim made here, ahead of that
    /// gate, would be safe only by wording coincidence rather than by construction.
    /// </remarks>
    private static (IReadOnlyList<AppliedCondition> Conditions, IReadOnlyList<(AppliedCondition Condition, TextSpan Span, string Note)> ClaimableRiders)
        ParseAppliedConditionsWithClaims(
        string text,
        bool attackEntry = false)
    {
        var conditions = new List<AppliedCondition>();
        var claimableRiders = new List<(AppliedCondition Condition, TextSpan Span, string Note)>();

        // The two-tier gaze — the one "First Failure: ... Second Failure: ..." pair the
        // model expresses, carved as an exact template the way Hold Person's clock was:
        // the corpus prints this wording three times, on the Basilisk's and the
        // Medusa's Petrifying Gaze and the Gorgon's Petrifying Breath, and it
        // structures as a single escalating rider — Restrained, repeated at the end of
        // the bearer's next turn, ending on a success and deepening to Petrified on
        // the failure. The matched sentences are masked out of the text the general
        // pass below reads (locally, not into the caller's coverage — this rider's own
        // claim is deferred like every other one, below), whose tiered-failure rule
        // would otherwise rightly refuse them; any tiered sentence that does not match
        // this template to the letter still falls to that rule.
        if (text.Contains(PetrifyingTierSentences, StringComparison.Ordinal))
        {
            var petrifyingCondition = new AppliedCondition(
                ConditionType.Restrained,
                Duration: ConditionDuration.UntilSavedOrEscalated,
                EscalatesTo: ConditionType.Petrified);

            conditions.Add(petrifyingCondition);

            var petrifyingIndex = text.IndexOf(PetrifyingTierSentences, StringComparison.Ordinal);
            var petrifyingSpan = new TextSpan(petrifyingIndex, PetrifyingTierSentences.Length);
            claimableRiders.Add((petrifyingCondition, petrifyingSpan, "condition.petrifying_tier"));

            // A throwaway coverage masks the template out of this method's own local
            // `text` only, so the sentence-splitting pass below cannot re-read
            // "Restrained"/"Petrified" as separate, wrong riders — this never reaches
            // the caller and carries no claim of its own.
            var maskOnly = new EntryCoverage(text);
            maskOnly.Claim(petrifyingSpan, "condition.petrifying_tier");
            text = maskOnly.Masked;
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
            var addedClaims = new List<IReadOnlyList<(TextSpan Span, string Note)>>();
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
                && !PluralConditionPattern().IsMatch(clauses[0])
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

                    var (size, duration, unmodelled, claims) = unmodelledCompanion
                        ? (null, null, sentence, (IReadOnlyList<(TextSpan Span, string Note)>)[])
                        : ReadRider(sentence, clause, match, attackEntry, text, nextSentence, clauseAbsoluteStart);

                    added.Add(new AppliedCondition(condition, escapeDc, size, duration, unmodelled));
                    addedClaims.Add(claims);
                }

                // "the target has the Blinded and Deafened conditions ..." (#372): two
                // names sharing one lead-in and one trailing duration, which
                // ConditionPattern's singular "condition" literal cannot see at all
                // (confirmed empty against every one of the 13 printings — see
                // PluralConditionPattern's own remarks). ReadRider is called once, since
                // the lead-in, duration and every refusal check it runs depend only on
                // the shared match span, never on which name is being credited — then
                // each name gets its own AppliedCondition, so a rider whose sibling is
                // refused (Deafened is not on ConditionRules.Executable, and always
                // fails CanBeImposed below regardless of what this loop does) still
                // stands or falls on its own imposability, exactly like every other
                // rider in this method.
                foreach (Match match in PluralConditionPattern().Matches(clause))
                {
                    int? escapeDc = match.Groups["escape"].Success
                        ? int.Parse(match.Groups["escape"].Value, CultureInfo.InvariantCulture)
                        : null;

                    var (size, duration, unmodelled, sharedClaims) = unmodelledCompanion
                        ? (null, null, sentence, (IReadOnlyList<(TextSpan Span, string Note)>)[])
                        : ReadRider(sentence, clause, match, attackEntry, text, nextSentence, clauseAbsoluteStart);

                    foreach (var first in new[] { true, false })
                    {
                        var group = match.Groups[first ? "first" : "second"];

                        if (!Enum.TryParse<ConditionType>(group.Value, ignoreCase: true, out var condition))
                        {
                            continue;
                        }

                        if (conditions.Any(existing => existing.Condition == condition)
                            || added.Any(existing => existing.Condition == condition))
                        {
                            continue;
                        }

                        // The shared claim ReadRider returned covers the whole clause —
                        // lead-in through both names through the trailing duration — and
                        // splitting it per name is what keeps an executable sibling's
                        // claim from swallowing an inexecutable one's own word (design
                        // §2.2: a claim says the model expresses these characters, and
                        // "Deafened" is never expressed just because "Blinded" sits next
                        // to it). See SplitPluralConditionClaim's own remarks for the
                        // exact split and what each half's residue looks like when only
                        // one name lands.
                        var claims = unmodelled is null
                            ? SplitPluralConditionClaim(sharedClaims, match, clauseAbsoluteStart, first)
                            : sharedClaims;

                        added.Add(new AppliedCondition(condition, escapeDc, size, duration, unmodelled));
                        addedClaims.Add(claims);
                    }
                }
            }

            // "until the grapple ends" is a tie to the sibling grapple, so it is only as
            // modelled as the grapple it hangs off. When the Grappled rider in the same
            // sentence is refused — "from one of ten tentacles" is limb bookkeeping the
            // model does not express — the dependent rider must not ride a grapple that
            // can never land, and is refused whole-sentence with it. (Its claims, if
            // any, are dropped by the CanBeImposed gate below regardless — this
            // annotation is belt-and-braces, since a WhileGrappleHolds duration can
            // never also carry a repeat-save annex or cap.)
            var grappled = added.FirstOrDefault(rider => rider.Condition == ConditionType.Grappled);

            for (var i = 0; i < added.Count; i++)
            {
                if (added[i].Duration is { WhileGrappleHolds: true } && grappled is not { IsFullyModelled: true })
                {
                    added[i] = added[i] with { Duration = null, UnmodelledRequirement = sentence };
                    addedClaims[i] = [];
                }
            }

            // Only a rider the engine will actually impose is claimable (design §2.5):
            // fully modelled, and on ConditionRules' own executable allowlist —
            // exactly ConditionRules.CanBeImposed's own test, checked here on the
            // rider as the grapple-tie sweep above left it. Every claim ReadRider
            // returned for this rider — its own clause, and any repeat-save annex or
            // automatic-success cap — travels through the same gate together: a
            // refused rider (empty Claims, per every early return in ReadRider) offers
            // nothing to add.
            for (var i = 0; i < added.Count; i++)
            {
                if (!ConditionRules.CanBeImposed(added[i]))
                {
                    continue;
                }

                foreach (var (span, note) in addedClaims[i])
                {
                    claimableRiders.Add((added[i], span, note));
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
    private static (CreatureSize? Size, ConditionDuration? Duration, string? Unmodelled, IReadOnlyList<(TextSpan Span, string Note)> Claims) ReadRider(
        string sentence,
        string clause,
        Match condition,
        bool attackEntry,
        string entryText,
        (string Text, TextSpan Span)? nextSentence,
        int clauseAbsoluteStart)
    {
        var trailing = clause[(condition.Index + condition.Length)..].Trim().TrimEnd('.').Trim();
        var duration = ParseDuration(trailing);

        // Every span this rider would claim — its own clause, and (for a repeat-save
        // duration) the annexed sentence and the automatic-success cap — accumulates
        // here rather than committing to any coverage directly. None of it is real
        // until the rider reaches a successful return below, and even then it is only
        // claimable once the caller's own §2.5 gate (mechanics is Attack or
        // SavingThrow, ConditionRules.CanBeImposed) passes — see
        // ParseAppliedConditionsWithClaims. A claim made here, ahead of either gate,
        // would be safe only by wording coincidence rather than by construction (the
        // exact gap a rider later refused by the tiered-failure check, the
        // attack-entry check, or the lead-in gate below would otherwise leave behind).
        var extraClaims = new List<(TextSpan Span, string Note)>();

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
        // does not belong to it). The clause must also be the whole sentence — no
        // sibling rider sharing it — because that is what the deleted join pattern's
        // own lookbehind required (anchored to the entire text up to the period, not
        // just this rider's own trailing text): a sentence printing two conditions
        // where an earlier one's clause happens to end exactly at "condition" must not
        // annex a repeat-save sentence that, on the SRD's own page, describes the
        // clause after it too. When every condition holds, the claim annexes the next
        // sentence's own span rather than rewriting the text to join them — there is
        // only ever one coordinate space (design §5).
        if (duration is null
            && trailing.Length == 0
            && clause == sentence
            && sentence.Contains("Failure:", StringComparison.Ordinal)
            && nextSentence is { } next
            && next.Text.TrimEnd('.') == RepeatSaveStandaloneSentence
            && entryText.Contains(AutomaticSuccessSentence, StringComparison.Ordinal))
        {
            duration = ConditionDuration.RepeatSaveUpToOneMinute;
            extraClaims.Add((next.Span, "condition.repeat_save_annex"));
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
                extraClaims.Add((
                    new TextSpan(automaticSuccessIndex, AutomaticSuccessSentence.Length),
                    "condition.repeat_save_cap"));
            }
        }

        // Anything after the condition that is not a duration this engine can run — "from
        // one of two claws", "until the web is destroyed", "at which point it repeats the
        // save" — is a rule of its own, and the rider is unusable until it is modelled.
        if (trailing.Length > 0 && duration is null)
        {
            return (null, null, sentence, []);
        }

        // "Second Failure: The target has the Unconscious condition for 1 minute." — a
        // save outcome tier the save model does not express. The engine imposes riders
        // on the plain failure, so a rider printed behind a deeper tier would land a
        // whole tier early — the Brass Dragon Wyrmling's sleep on a first failed save.
        if (TieredFailurePattern().IsMatch(sentence))
        {
            return (null, null, sentence, []);
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
                return (null, null, sentence, []);
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
                return (null, null, sentence, []);
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
            return (null, null, sentence, []);
        }

        // The claim runs from the start of this gate match — which RiderLeadInPattern
        // anchors to the whole of `leading`, so its own start coincides with where
        // `leading` itself starts within the clause — through the end of the clause,
        // covering the size gate, the condition and its duration whole (design §7.3).
        // `leading`'s own length is what locates its start: it always ends exactly
        // where the condition match begins, whichever of StripDamage or
        // StripAttackPreamble produced it. Added last, alongside whatever annex/cap
        // claims already accumulated above — all of it travels out together, gated as
        // one unit by the caller.
        var claimStart = clauseAbsoluteStart + condition.Index - leading.Length;
        var claimEnd = clauseAbsoluteStart + clause.Length;

        if (claimEnd > claimStart)
        {
            extraClaims.Add((new TextSpan(claimStart, claimEnd - claimStart), "condition.rider"));
        }

        return gate.Groups["size"].Success
            && Enum.TryParse<CreatureSize>(gate.Groups["size"].Value, ignoreCase: true, out var size)
                ? (size, duration, null, extraClaims)
                : (null, duration, null, extraClaims);
    }

    /// <summary>
    /// Narrows <see cref="ReadRider"/>'s single combined "condition.rider" claim — the
    /// lead-in through both names through the shared trailing duration — to the half a
    /// plural conjunction's <paramref name="first"/> or second name actually earns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReadRider</c> is called once per plural match, because its lead-in, duration
    /// and every refusal check depend only on the shared span, never on which name is
    /// being credited. That means its one "condition.rider" claim — always the last
    /// entry, added right before both of its return statements — covers the whole
    /// clause under either name equally, and crediting it to both names verbatim would
    /// let an executable sibling's claim swallow an inexecutable one's own word: two
    /// names sharing one claim is exactly the sentence-level credit bug design
    /// §2.2 exists to end, rebuilt one level down.
    /// </para>
    /// <para>
    /// Each name keeps two pieces, never touching the sibling's own word: its own
    /// name, and whichever of the shared lead-in or the shared trailing duration sits
    /// on the far side of the sibling — the first name reaches past the second's own
    /// word to the trailing duration ("the target has the Blinded" plus " conditions
    /// until the start of the giant's next turn"); the second reaches past the
    /// first's own word back to the lead-in (" the target has the" plus "Frightened
    /// conditions until the end of its next turn"). When every name in the
    /// conjunction is imposable the two names' claims meet with only the bare "and "
    /// connective between them, which ordinary glue absorption closes (design §4.1),
    /// so a fully-imposable conjunction still claims edge to edge with no residue at
    /// all — the Rakshasa's Baleful Command. When one name is refused, the gap left
    /// behind is exactly that name's own word plus its bordering "and" — never the
    /// shared text either side of it, which the surviving name has already claimed —
    /// so the residue reads as tightly as the printed conjunction allows: "and
    /// Deafened" on the Storm Giant's Thunderbolt, "Deafened and" on the Tarrasque's
    /// Thunderous Bellow. Both verbatim substrings, per §6.2.
    /// </para>
    /// <para>
    /// Any other claim <c>ReadRider</c> returned (a repeat-save annex or cap) travels
    /// to both names unchanged: neither depends on which name is being credited, and
    /// duplicating them is a harmless union under §2.4, not a false claim — the corpus
    /// prints no repeat-save wording alongside a plural conjunction today, so this is
    /// belt-and-braces rather than a shape that is actually exercised.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<(TextSpan Span, string Note)> SplitPluralConditionClaim(
        IReadOnlyList<(TextSpan Span, string Note)> claims, Match pluralMatch, int clauseAbsoluteStart, bool first)
    {
        if (claims.Count == 0 || claims[^1].Note != "condition.rider")
        {
            return claims;
        }

        var (combined, note) = claims[^1];
        var firstGroup = pluralMatch.Groups["first"];
        var secondGroup = pluralMatch.Groups["second"];
        var firstStart = clauseAbsoluteStart + firstGroup.Index;
        var firstEnd = firstStart + firstGroup.Length;
        var secondStart = clauseAbsoluteStart + secondGroup.Index;
        var secondEnd = secondStart + secondGroup.Length;

        // The name itself, bundled with whichever shared text is contiguous with it
        // and does not cross the sibling's own word: the first name sits right after
        // the shared lead-in, so it claims both together; the second name sits right
        // before the shared trailing duration, so it claims both together.
        var own = first
            ? new TextSpan(combined.Start, firstEnd - combined.Start)
            : new TextSpan(secondStart, combined.End - secondStart);

        // The shared text on the far side of the sibling's own word — never crosses
        // into it, so an unclaimed sibling's name is never swallowed by this claim.
        var reach = first
            ? new TextSpan(secondEnd, combined.End - secondEnd)
            : new TextSpan(combined.Start, firstStart - combined.Start);

        var replacement = new List<(TextSpan Span, string Note)> { (own, note) };

        if (reach.Length > 0)
        {
            replacement.Add((reach, note));
        }

        return [.. claims.Take(claims.Count - 1), .. replacement];
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

    /// <summary>
    /// The exact printed pair the escalating-rider template matches — the Basilisk's
    /// and the Medusa's Petrifying Gaze, word for word. Anchored to the letter so a
    /// third tiered wording arriving from a new direction is refused rather than
    /// approximated. The corpus prints this wording three times — the Basilisk's and
    /// the Medusa's Petrifying Gaze, and the Gorgon's Petrifying Breath.
    /// </summary>
    private const string PetrifyingTierSentences =
        "First Failure: The target has the Restrained condition and repeats the save at the end of its " +
        "next turn if it is still Restrained, ending the effect on itself on a success. " +
        "Second Failure: The target has the Petrified condition instead of the Restrained condition.";

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
    //
    // "one creature" is claimed as a single target's head noun only when nothing but
    // a sight/distance qualifier follows it — a printed distance narrows an otherwise
    // arbitrary target and the engine's own any-creature targeting is still an honest
    // (if unenforced-range) description of that. A state or participle gate is a
    // different claim: "one creature within 5 feet that has the Prone condition"
    // (Gorgon/Elephant/Mammoth Trample) and "one creature Grappled by the chuul"
    // (Chuul's Paralyzing Tentacles) both name which creatures are eligible at all in
    // a way this engine does not filter on, so calling even the head noun "one
    // creature" would assert the model expresses a selection rule it does not run —
    // the same false claim §7.6's own rule forbids for the qualifiers themselves. The
    // negative lookahead excludes exactly the two gate shapes the corpus prints (an
    // optional "within N feet" ahead of "that" or "Grappled"); a bare sight/distance
    // qualifier with nothing else after it still leaves the lookahead unmatched, so
    // "one creature" still claims there.
    [GeneratedRegex(
        @",\s*(?:" +
        @"each\s+creature\s+in\s+a\s+\d+-foot\s+Cone" +
        @"|each\s+creature\s+in\s+a\s+\d+-foot-long,?\s*\d+-foot-?\s?wide\s+Line" +
        @"|each\s+creature\s+in\s+a\s+\d+-foot\s+Emanation\s+originating\s+from\s+the\s+" +
            @"(?<origin>[\w']+(?:\s+(?!(?:that|who|which|within|can)\b)[\w']+)*)" +
        @"|each\s+creature\s+in\s+a\s+\d+-foot-radius\s+Sphere\s+centered\s+on\s+a\s+point\b" +
        @"|one\s+creature\b(?!\s+(?:within\s+\d+\s+feet\s+)?(?:that\b|Grappled\b))" +
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

    // "the Blinded and Deafened conditions" (#372) — the plural conjunction
    // ConditionPattern cannot see: its own literal "condition" never matches "conditions"
    // ($-less, so it CAN slice a prefix of the plural word, but nothing in the monster
    // stat blocks ever prints "the" directly before the second name — "and" always sits
    // there instead — so the singular pattern finds nothing in either name; confirmed
    // empty on all 13 monster printings). Two names only, and joined by "and": the
    // monster stat blocks print no three-name conjunction ("X, Y, and Z conditions") and
    // no "or"-joined pair, so this pattern is anchored to exactly that shape rather than
    // generalised past what the book's bestiary prints.
    //
    // This method also runs over species traits, class features and spells (shared via
    // ClassifyTrait, design §8), where the corpus is wider and the claim above does not
    // hold: Divine Word prints a three-name row ("the Blinded, Deafened, and Stunned
    // conditions") and Protection from Evil and Good prints an "or"-joined pair ("the
    // Charmed or Frightened conditions"). Both are deliberate, known fall-throughs —
    // this pattern requires a literal "and" between exactly two names, so a three-name
    // list's middle comma and an "or" both fail to match anywhere, and the whole clause
    // falls to residue exactly as it did before this pattern existed. Verified: neither
    // shape produces a partial match on any of the names it contains.
    [GeneratedRegex(@"the\s+(?<first>Blinded|Charmed|Deafened|Frightened|Grappled|Incapacitated|Invisible|Paralyzed|Petrified|Poisoned|Prone|Restrained|Stunned|Unconscious)\s+and\s+(?<second>Blinded|Charmed|Deafened|Frightened|Grappled|Incapacitated|Invisible|Paralyzed|Petrified|Poisoned|Prone|Restrained|Stunned|Unconscious)\s+conditions(?:\s*\(escape\s+DC\s*(?<escape>\d+)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex PluralConditionPattern();

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
