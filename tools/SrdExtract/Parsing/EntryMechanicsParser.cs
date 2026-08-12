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
    /// Deliberately tiny and grown one deliberate decision at a time. Anything not on it
    /// is Unmodelled and counted, which is the safe direction to be wrong in. Several
    /// traits that look inert are not: Pack Tactics grants Advantage on attack rolls,
    /// Sunlight Sensitivity imposes Disadvantage, and Flyby removes Opportunity Attacks —
    /// none of those belong here.
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
    public static TraitEntry ClassifyTrait(string name, string text)
    {
        var usage = ParseUsageLimit(name);
        var bareName = StripUsage(name);
        var conditions = ParseAppliedConditions(text);

        if (ParseSave(text, conditions) is { } save)
        {
            return new TraitEntry(
                bareName,
                text,
                EntryMechanics.SavingThrow,
                save,
                usage,
                conditions,
                LeftoverMechanicalSentences(text, EntryMechanics.SavingThrow, conditions));
        }

        if (KnownInertEntries.Contains(bareName))
        {
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
    public static MonsterEntry Classify(string name, MonsterEntrySection section, string text)
    {
        var usage = ParseUsageLimit(name);
        var bareName = StripUsage(name);
        var attack = StatBlockLineGrammar.ParseAttack(text);
        var conditions = ParseAppliedConditions(text);

        if (attack is not null)
        {
            return Build(bareName, section, text, EntryMechanics.Attack, usage, conditions, attack: attack);
        }

        if (ParseReaction(text) is { } reaction)
        {
            return Build(bareName, section, text, EntryMechanics.Reaction, usage, conditions, reaction: reaction);
        }

        if (ParseMultiattack(text) is { } multiattack)
        {
            return Build(
                bareName,
                section,
                text,
                EntryMechanics.Multiattack,
                usage,
                conditions,
                multiattack: multiattack);
        }

        if (ParseSave(text, conditions) is { } save)
        {
            return Build(bareName, section, text, EntryMechanics.SavingThrow, usage, conditions, save: save);
        }

        if (section == MonsterEntrySection.Trait && MonsterTraitRegistry.Implements(bareName))
        {
            // The engine executes this trait by its printed name. MonsterTraitRegistry
            // is the curated list, and the reading each name rests on — including where
            // it is deliberately narrower than the printed sentence — is recorded there.
            return new MonsterEntry(bareName, section, text, Mechanics: EntryMechanics.Passive, Usage: usage);
        }

        if (KnownInertEntries.Contains(bareName))
        {
            // No unmodelled clauses by definition — this is a recorded decision that the
            // entry does nothing in a fight.
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
            LeftoverMechanicalSentences(text, mechanics, conditions));

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
        IReadOnlyList<AppliedCondition> conditions) =>
        SplitSentences(text)
            .Where(sentence => !IsAccountedFor(sentence, mechanics, conditions))
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
        IReadOnlyList<AppliedCondition> conditions)
    {
        var carried = ConditionsIn(sentence, conditions).ToArray();

        if (carried.Any(condition => !ConditionRules.CanBeImposed(condition)))
        {
            return false;
        }

        // A sentence that is nothing but an imposable rider is now fully expressed, even
        // though the attack or saving-throw grammar itself says nothing about it. Save
        // entries joined attacks here when the engine began imposing their riders on a
        // failed save (#6).
        return MatchesStructuredForm(sentence, mechanics)
            || (mechanics is EntryMechanics.Attack or EntryMechanics.SavingThrow && carried.Length > 0);
    }

    private static bool MatchesStructuredForm(string sentence, EntryMechanics mechanics) => mechanics switch
    {
        EntryMechanics.Attack =>
            sentence.Contains("Attack Roll:", StringComparison.Ordinal)
            || sentence.StartsWith("Hit:", StringComparison.Ordinal),

        EntryMechanics.SavingThrow =>
            sentence.Contains("Saving Throw:", StringComparison.Ordinal)
            || sentence.StartsWith("Failure", StringComparison.Ordinal)
            || sentence.StartsWith("Success", StringComparison.Ordinal),

        EntryMechanics.Multiattack => true,

        EntryMechanics.Reaction =>
            sentence.StartsWith("Trigger:", StringComparison.Ordinal)
            || sentence.StartsWith("Response:", StringComparison.Ordinal),

        _ => false,
    };


    private static IEnumerable<string> SplitSentences(string text) =>
        SentenceBoundary()
            .Split(text)
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0);

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
    private static ReactionEffect? ParseReaction(string text)
    {
        var match = ReactionPattern().Match(text);

        return match.Success
            ? new ReactionEffect(match.Groups["trigger"].Value.Trim(), match.Groups["response"].Value.Trim())
            : null;
    }

    /// <summary>
    /// Parses "The bandit makes two attacks, using Scimitar and Pistol in any
    /// combination." and "The armor makes two Slam attacks."
    /// </summary>
    private static MultiattackEffect? ParseMultiattack(string text)
    {
        // "... makes two attacks, using Scimitar and Pistol in any combination."
        var combination = CombinationMultiattackPattern().Match(text);
        if (combination.Success && WordToNumber(combination.Groups["count"].Value) is { } count)
        {
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
        }

        if (total < 2 || names.Count == 0)
        {
            return null;
        }

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
    private static SaveEffect? ParseSave(string text, IReadOnlyList<AppliedCondition> conditions)
    {
        var header = SaveHeaderPattern().Match(text);
        if (!header.Success)
        {
            return null;
        }

        var ability = Enum.Parse<Ability>(header.Groups["ability"].Value, ignoreCase: true);
        var dc = int.Parse(header.Groups["dc"].Value, CultureInfo.InvariantCulture);

        var failureIndex = text.IndexOf("Failure", StringComparison.Ordinal);
        var failureDamage = failureIndex < 0
            ? []
            : ParseDamageList(text[failureIndex..]);

        var success = text.Contains("Failure or Success:", StringComparison.Ordinal)
            ? SaveSuccessOutcome.SameAsFailure
            : text.Contains("Success: Half damage", StringComparison.OrdinalIgnoreCase)
                ? SaveSuccessOutcome.HalfDamage
                : SaveSuccessOutcome.NoEffect;

        return new SaveEffect(ability, dc, ParseArea(text), failureDamage, success, conditions);
    }

    /// <summary>Parses "30-foot Cone", "30-foot-long, 5-foot-wide Line", "5-foot Emanation".</summary>
    private static EffectArea? ParseArea(string text)
    {
        var match = AreaPattern().Match(text);

        if (!match.Success || !Enum.TryParse<AreaShape>(match.Groups["shape"].Value, ignoreCase: true, out var shape))
        {
            return null;
        }

        return new EffectArea(
            shape,
            int.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture),
            match.Groups["width"].Success
                ? int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture)
                : null);
    }

    /// <summary>Reads every "N (XdY) Type damage" in a clause, up to the end of the sentence.</summary>
    private static IReadOnlyList<AttackDamage> ParseDamageList(string text)
    {
        var sentenceEnd = text.IndexOf('.');
        var clause = sentenceEnd < 0 ? text : text[..sentenceEnd];

        var damage = new List<AttackDamage>();

        foreach (Match match in SaveDamagePattern().Matches(clause))
        {
            if (!Enum.TryParse<DamageType>(match.Groups["type"].Value, ignoreCase: true, out var type))
            {
                continue;
            }

            var average = int.Parse(match.Groups["average"].Value, CultureInfo.InvariantCulture);

            var dice = match.Groups["dice"].Success && DiceExpression.TryParse(match.Groups["dice"].Value, out var rolled)
                ? rolled
                : DiceExpression.Flat(average);

            damage.Add(new AttackDamage(dice, type, average));
        }

        return damage;
    }

    /// <summary>
    /// Finds every condition the entry imposes, with its escape DC, its size gate, and
    /// whatever else was printed with it that the model cannot express.
    /// </summary>
    private static IReadOnlyList<AppliedCondition> ParseAppliedConditions(string text)
    {
        var conditions = new List<AppliedCondition>();

        // Sentence by sentence, because the rider's gate and its duration are the words
        // on either side of the condition within its own sentence, and nowhere else.
        foreach (var sentence in SplitSentences(text))
        {
            foreach (Match match in ConditionPattern().Matches(sentence))
            {
                if (!Enum.TryParse<ConditionType>(match.Groups["condition"].Value, ignoreCase: true, out var condition))
                {
                    continue;
                }

                if (conditions.Any(existing => existing.Condition == condition))
                {
                    continue;
                }

                int? escapeDc = match.Groups["escape"].Success
                    ? int.Parse(match.Groups["escape"].Value, CultureInfo.InvariantCulture)
                    : null;

                var (size, duration, unmodelled) = ReadRider(sentence, match);

                conditions.Add(new AppliedCondition(condition, escapeDc, size, duration, unmodelled));
            }
        }

        return conditions;
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
    private static (CreatureSize? Size, ConditionDuration? Duration, string? Unmodelled) ReadRider(
        string sentence,
        Match condition)
    {
        var trailing = sentence[(condition.Index + condition.Length)..].Trim().TrimEnd('.').Trim();
        var duration = ParseDuration(trailing);

        // Anything after the condition that is not a duration this engine can run — "from
        // one of two claws", "until the grapple ends", "at which point it repeats the
        // save" — is a rule of its own, and the rider is unusable until it is modelled.
        if (trailing.Length > 0 && duration is null)
        {
            return (null, null, sentence);
        }

        var leading = StripAttackPreamble(sentence[..condition.Index]);
        var gate = RiderLeadInPattern().Match(leading);

        if (!gate.Success)
        {
            return (null, null, sentence);
        }

        return gate.Groups["size"].Success
            && Enum.TryParse<CreatureSize>(gate.Groups["size"].Value, ignoreCase: true, out var size)
                ? (size, duration, null)
                : (null, duration, null);
    }

    /// <summary>
    /// Parses "until the start of its next turn" and "until the end of the devil's next
    /// turn".
    /// </summary>
    /// <remarks>
    /// The possessive is the whole of the distinction. "its" is the creature carrying the
    /// condition; a name is the creature that imposed it, and every printed name in the
    /// bestiary is the stat block's own creature. Reading one as the other moves the end
    /// of the condition by most of a round.
    /// </remarks>
    private static ConditionDuration? ParseDuration(string trailing)
    {
        var match = TurnBoundaryDurationPattern().Match(trailing);

        if (!match.Success)
        {
            return null;
        }

        var clock = match.Groups["when"].Value.Equals("start", StringComparison.OrdinalIgnoreCase)
            ? ConditionClock.StartOfTurn
            : ConditionClock.EndOfTurn;

        var owner = match.Groups["bearer"].Success
            ? ConditionDurationOwner.Bearer
            : ConditionDurationOwner.Source;

        return new ConditionDuration(clock, owner);
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

        if (hit < 0)
        {
            return leading;
        }

        var afterHit = leading[(hit + "Hit:".Length)..];
        var lastDamage = afterHit.LastIndexOf("damage", StringComparison.Ordinal);

        return lastDamage < 0 ? afterHit : afterHit[(lastDamage + "damage".Length)..];
    }

    /// <summary>The conditions a sentence carries, out of those parsed from the whole entry.</summary>
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

    [GeneratedRegex(@"(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\s+Saving\s+Throw:\s*DC\s*(?<dc>\d+)")]
    private static partial Regex SaveHeaderPattern();

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

    [GeneratedRegex(@"the\s+(?<condition>Blinded|Charmed|Deafened|Frightened|Grappled|Incapacitated|Invisible|Paralyzed|Petrified|Poisoned|Prone|Restrained|Stunned|Unconscious)\s+condition(?:\s*\(escape\s+DC\s*(?<escape>\d+)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionPattern();

    // Sentence boundaries, avoiding the abbreviations the SRD actually uses: "5 ft.",
    // "DC 12.", and decimal-free numbers are safe, but "ft." is everywhere.
    [GeneratedRegex(@"(?<!\bft)(?<!\bMr)(?<!\bDr)\.\s+(?=[A-Z0-9])")]
    private static partial Regex SentenceBoundary();

}
