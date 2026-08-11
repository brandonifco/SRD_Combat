using System.Globalization;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

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
            LeftoverMechanicalSentences(text, mechanics));

    /// <summary>
    /// Finds sentences carrying mechanics that the entry's own structured form does not
    /// account for.
    /// </summary>
    /// <remarks>
    /// This is what catches the partly-structured case. An attack entry's header and its
    /// Hit clause are covered by <see cref="MonsterAttack"/>; a rider sentence after them
    /// ("If the target is a Large or smaller creature, it has the Grappled condition") is
    /// not, and is reported even though the condition itself was extracted, because the
    /// gate on it was not.
    /// </remarks>
    private static IReadOnlyList<string> LeftoverMechanicalSentences(string text, EntryMechanics mechanics) =>
        SplitSentences(text)
            .Where(sentence => !IsAccountedFor(sentence, mechanics))
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
    private static bool IsAccountedFor(string sentence, EntryMechanics mechanics) => mechanics switch
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
        var named = NamedMultiattackPattern().Match(text);
        if (named.Success && WordToNumber(named.Groups["count"].Value) is { } namedCount)
        {
            return new MultiattackEffect(namedCount, [named.Groups["attack"].Value.Trim()], AnyCombination: false);
        }

        var combination = CombinationMultiattackPattern().Match(text);
        if (combination.Success && WordToNumber(combination.Groups["count"].Value) is { } count)
        {
            var names = combination.Groups["attacks"].Value
                .Split([" and ", " or ", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(candidate => candidate.Length > 0)
                .ToArray();

            return new MultiattackEffect(count, names, AnyCombination: true);
        }

        return null;
    }

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

    /// <summary>Finds every condition the entry imposes, with its escape DC where printed.</summary>
    private static IReadOnlyList<AppliedCondition> ParseAppliedConditions(string text)
    {
        var conditions = new List<AppliedCondition>();

        foreach (Match match in ConditionPattern().Matches(text))
        {
            if (!Enum.TryParse<ConditionType>(match.Groups["condition"].Value, ignoreCase: true, out var condition))
            {
                continue;
            }

            int? escapeDc = match.Groups["escape"].Success
                ? int.Parse(match.Groups["escape"].Value, CultureInfo.InvariantCulture)
                : null;

            if (!conditions.Any(existing => existing.Condition == condition))
            {
                conditions.Add(new AppliedCondition(condition, escapeDc));
            }
        }

        return conditions;
    }

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

    [GeneratedRegex(@"makes\s+(?<count>one|two|three|four|five|six|\d+)\s+(?<attack>[A-Z][\w' ]*?)\s+attacks?\b")]
    private static partial Regex NamedMultiattackPattern();

    [GeneratedRegex(@"makes\s+(?<count>one|two|three|four|five|six|\d+)\s+attacks?,\s*using\s+(?<attacks>[^.]+?)\s+in any combination")]
    private static partial Regex CombinationMultiattackPattern();

    [GeneratedRegex(@"(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\s+Saving\s+Throw:\s*DC\s*(?<dc>\d+)")]
    private static partial Regex SaveHeaderPattern();

    [GeneratedRegex(@"(?<size>\d+)-foot(?:-long,?\s*(?<width>\d+)-foot-?\s?wide)?\s+(?<shape>Cone|Line|Emanation|Cube|Sphere|Cylinder)")]
    private static partial Regex AreaPattern();

    [GeneratedRegex(@"(?<average>\d+)\s*(?:\((?<dice>\d+d\d+(?:\s*[+-]\s*\d+)?)\))?\s*(?<type>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)\s+damage")]
    private static partial Regex SaveDamagePattern();

    [GeneratedRegex(@"the\s+(?<condition>Blinded|Charmed|Deafened|Frightened|Grappled|Incapacitated|Invisible|Paralyzed|Petrified|Poisoned|Prone|Restrained|Stunned|Unconscious)\s+condition(?:\s*\(escape\s+DC\s*(?<escape>\d+)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionPattern();

    // Sentence boundaries, avoiding the abbreviations the SRD actually uses: "5 ft.",
    // "DC 12.", and decimal-free numbers are safe, but "ft." is everywhere.
    [GeneratedRegex(@"(?<!\bft)(?<!\bMr)(?<!\bDr)\.\s+(?=[A-Z0-9])")]
    private static partial Regex SentenceBoundary();

}
