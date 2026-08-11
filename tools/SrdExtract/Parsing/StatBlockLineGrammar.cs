using System.Globalization;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SrdExtract.Parsing;

/// <summary>
/// The regular grammars inside a stat block's individual lines.
/// </summary>
/// <remarks>
/// SRD 5.2.1's stat blocks are far more regular than 5.1's prose ones — attacks in
/// particular follow a fixed shape — which is what makes structured extraction
/// realistic rather than a pile of special cases.
/// </remarks>
internal static partial class StatBlockLineGrammar
{
    /// <summary>Parses <c>Huge Dragon (Chromatic), Chaotic Evil</c>.</summary>
    public static (IReadOnlyList<CreatureSize> Sizes, CreatureType Type, string? Subtype, string Alignment)?
        ParseMeta(string text)
    {
        var match = MetaPattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var sizes = match.Groups["sizes"].Value
            .Split(" or ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(size => Enum.TryParse<CreatureSize>(size, ignoreCase: true, out var parsed)
                ? parsed
                : (CreatureSize?)null)
            .OfType<CreatureSize>()
            .ToArray();

        if (sizes.Length == 0 || !Enum.TryParse<CreatureType>(match.Groups["type"].Value, ignoreCase: true, out var type))
        {
            return null;
        }

        var subtype = match.Groups["subtype"].Success ? match.Groups["subtype"].Value.Trim() : null;

        return (sizes, type, subtype, match.Groups["alignment"].Value.Trim());
    }

    /// <summary>Parses the speed list from <c>Speed 40 ft., Fly 80 ft. (hover), Swim 40 ft.</c>.</summary>
    public static (IReadOnlyDictionary<MovementMode, int> Speeds, bool CanHover) ParseSpeeds(string text)
    {
        var speeds = new Dictionary<MovementMode, int>();
        var canHover = false;

        foreach (Match match in SpeedPattern().Matches(text))
        {
            var mode = match.Groups["mode"].Success
                ? Enum.TryParse<MovementMode>(match.Groups["mode"].Value, ignoreCase: true, out var parsed)
                    ? parsed
                    : (MovementMode?)null
                : MovementMode.Walk;

            if (mode is null)
            {
                continue;
            }

            speeds[mode.Value] = int.Parse(match.Groups["feet"].Value, CultureInfo.InvariantCulture);

            if (mode == MovementMode.Fly && match.Groups["hover"].Success)
            {
                canHover = true;
            }
        }

        return (speeds, canHover);
    }

    /// <summary>
    /// Parses the three score/modifier/save triples on one row of the ability table.
    /// </summary>
    /// <remarks>
    /// The small-caps font splits ability names oddly — <c>De x 12 +1 +1</c>,
    /// <c>Co n 15 +2 +4</c> — so the names are not matched at all. The row's position
    /// determines which abilities it holds: the first row is Strength/Dexterity/
    /// Constitution and the second Intelligence/Wisdom/Charisma.
    /// </remarks>
    public static IReadOnlyList<(int Score, int PrintedModifier, int Save)> ParseAbilityRow(string text) =>
        AbilityTriplePattern()
            .Matches(text)
            .Select(match => (
                Score: int.Parse(match.Groups["score"].Value, CultureInfo.InvariantCulture),
                PrintedModifier: ParseSigned(match.Groups["mod"].Value),
                Save: ParseSigned(match.Groups["save"].Value)))
            .ToArray();

    /// <summary>Parses <c>CR 14 (XP 11,500, or 13,000 in lair; PB +5)</c>.</summary>
    public static (decimal Rating, int Experience, int? LairExperience, int ProficiencyBonus)? ParseChallenge(string text)
    {
        var match = ChallengePattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var rating = match.Groups["fraction"].Success
            ? decimal.Parse(match.Groups["numerator"].Value, CultureInfo.InvariantCulture)
              / decimal.Parse(match.Groups["denominator"].Value, CultureInfo.InvariantCulture)
            : decimal.Parse(match.Groups["whole"].Value, CultureInfo.InvariantCulture);

        var experience = ParseGroupedNumber(
            match.Groups["xp"].Success ? match.Groups["xp"].Value : match.Groups["xp2"].Value);
        int? lair = match.Groups["lair"].Success ? ParseGroupedNumber(match.Groups["lair"].Value) : null;
        var proficiency = ParseSigned(match.Groups["pb"].Value);

        return (rating, experience, lair, proficiency);
    }

    /// <summary>Parses <c>Senses Blindsight 60 ft., Darkvision 120 ft.; Passive Perception 21</c>.</summary>
    public static (IReadOnlyList<MonsterSense> Senses, int? PassivePerception) ParseSenses(string text)
    {
        var senses = SensePattern()
            .Matches(text)
            .Select(match => new MonsterSense(
                Enum.Parse<SenseType>(match.Groups["type"].Value, ignoreCase: true),
                int.Parse(match.Groups["feet"].Value, CultureInfo.InvariantCulture)))
            .ToArray();

        var passive = PassivePerceptionPattern().Match(text);

        return (
            senses,
            passive.Success ? int.Parse(passive.Groups["value"].Value, CultureInfo.InvariantCulture) : null);
    }

    /// <summary>
    /// Parses the attack grammar:
    /// <c>Melee Attack Roll: +6, reach 5 ft. Hit: 10 (2d6 + 3) Piercing damage plus 3 (1d6) Fire damage.</c>
    /// Returns null for entries that resolve some other way — a saving throw, or no
    /// mechanics at all — which keep their prose and gain no structured attack.
    /// </summary>
    public static MonsterAttack? ParseAttack(string text)
    {
        var header = AttackHeaderPattern().Match(text);
        if (!header.Success)
        {
            return null;
        }

        // "Melee or Ranged" is recorded as Melee, because what actually distinguishes a
        // dual-mode attack is that it carries both a reach and a range — Kind alone
        // cannot express it, and the two distance fields can.
        var kind = header.Groups["kind"].Value.StartsWith("Melee", StringComparison.OrdinalIgnoreCase)
            ? AttackKind.Melee
            : AttackKind.Ranged;

        var bonus = ParseSigned(header.Groups["bonus"].Value);

        int? reach = header.Groups["reach"].Success
            ? int.Parse(header.Groups["reach"].Value, CultureInfo.InvariantCulture)
            : null;

        int? normalRange = header.Groups["range"].Success
            ? int.Parse(header.Groups["range"].Value, CultureInfo.InvariantCulture)
            : null;

        int? longRange = header.Groups["longRange"].Success
            ? int.Parse(header.Groups["longRange"].Value, CultureInfo.InvariantCulture)
            : null;

        // Damage is only counted after "Hit:" — an entry's trailing prose can mention
        // other dice ("takes 13 (3d8) Fire damage" in a rider) that are not this
        // attack's own damage.
        var hitIndex = text.IndexOf("Hit:", StringComparison.Ordinal);
        var damage = hitIndex < 0
            ? []
            : ParseDamage(text[(hitIndex + "Hit:".Length)..]);

        return new MonsterAttack(kind, bonus, reach, normalRange, longRange, damage);
    }

    /// <summary>
    /// Reads the damage components at the start of a hit clause, stopping at the first
    /// thing that is not one. Riders after the damage ("If the target is a Large or
    /// smaller creature, ...") are left to the entry's prose.
    /// </summary>
    private static IReadOnlyList<AttackDamage> ParseDamage(string text)
    {
        var damage = new List<AttackDamage>();
        var searchFrom = 0;

        while (searchFrom < text.Length)
        {
            var match = DamagePattern().Match(text, searchFrom);
            if (!match.Success)
            {
                break;
            }

            // Only keep a run of components joined by "plus"; anything further into the
            // prose is a rider, not part of this attack's damage.
            if (damage.Count > 0 && !LooksLikeContinuation(text, searchFrom, match.Index))
            {
                break;
            }

            if (!Enum.TryParse<DamageType>(match.Groups["type"].Value, ignoreCase: true, out var type))
            {
                break;
            }

            var average = int.Parse(match.Groups["average"].Value, CultureInfo.InvariantCulture);

            // No parenthesised dice means a flat amount — "Hit: 1 Piercing damage".
            DiceExpression dice;
            if (match.Groups["dice"].Success)
            {
                if (!DiceExpression.TryParse(match.Groups["dice"].Value, out var rolled))
                {
                    break;
                }

                dice = rolled;
            }
            else
            {
                dice = DiceExpression.Flat(average);
            }

            var componentEnd = match.Index + match.Length;

            // A component's qualifier runs from the end of the component to whichever
            // comes first: the end of the sentence, or the start of the next component.
            // Without the second bound, "5 (1d6 + 2) Slashing damage, plus 2 (1d4)
            // Slashing damage if the attack roll had Advantage" marks *both* components
            // conditional, because the first one's scan swallows the second one's clause.
            var next = DamagePattern().Match(text, componentEnd);
            var limit = next.Success ? next.Index : text.Length;

            damage.Add(new AttackDamage(dice, type, average, ReadCondition(text, componentEnd, limit)));

            searchFrom = componentEnd;
        }

        return damage;
    }

    private static bool LooksLikeContinuation(string text, int from, int matchIndex) =>
        text[from..matchIndex].Contains("plus", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a qualifier attached to a damage component, such as the goblins'
    /// "plus 2 (1d4) Slashing damage <em>if the attack roll had Advantage</em>".
    /// </summary>
    /// <remarks>
    /// Deliberately looks only as far as the end of the component's own sentence. The
    /// Mummy's Rotting Fist is why: it reads "... plus 10 (3d6) Necrotic damage. If the
    /// target is a creature, it is cursed." That "If" opens a new sentence describing a
    /// rider, and treating it as a condition on the damage would wrongly make the
    /// necrotic damage conditional.
    /// </remarks>
    private static AttackDamageCondition? ReadCondition(string text, int from, int limit)
    {
        if (from >= limit)
        {
            return null;
        }

        var sentenceEnd = text.IndexOf('.', from);
        var clauseEnd = sentenceEnd < 0 ? limit : Math.Min(sentenceEnd, limit);
        var clause = text[from..clauseEnd];

        return clause.Contains("if the attack roll had Advantage", StringComparison.OrdinalIgnoreCase)
            ? AttackDamageCondition.AttackRollHadAdvantage
            : null;
    }

    private static int ParseSigned(string value) =>
        int.Parse(value.Replace(" ", string.Empty), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static int ParseGroupedNumber(string value) =>
        int.Parse(value.Replace(",", string.Empty), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<sizes>(?:Tiny|Small|Medium|Large|Huge|Gargantuan)(?:\s+or\s+(?:Tiny|Small|Medium|Large|Huge|Gargantuan))?)\s+(?<type>\w+)(?:\s*\((?<subtype>[^)]+)\))?\s*,\s*(?<alignment>.+)$")]
    private static partial Regex MetaPattern();

    [GeneratedRegex(@"(?:(?<mode>Burrow|Climb|Fly|Swim)\s+)?(?<feet>\d+)\s*ft\.?(?<hover>\s*\(hover\))?")]
    private static partial Regex SpeedPattern();

    // The save's sign is optional because a handful of minus glyphs do not survive text
    // extraction — the Young White Dragon's Int save renders as "2" where the block
    // means -2. Parsing it unsigned keeps the row intact; KnownCorrections repairs the
    // value, and the validator's save_unexplained check is what surfaces any others.
    [GeneratedRegex(@"(?<score>\d+)\s+(?<mod>[+-]\s?\d+)\s+(?<save>[+-]?\s?\d+)")]
    private static partial Regex AbilityTriplePattern();

    // The XP field is written both ways in the source: "(XP 450; PB +2)" for all but
    // four blocks, which print "(700 XP; PB +2)" instead. Both orders are accepted.
    [GeneratedRegex(@"^CR\s+(?:(?<fraction>(?<numerator>\d+)/(?<denominator>\d+))|(?<whole>\d+))\s*\(\s*(?:XP\s+(?<xp>[\d,]+)|(?<xp2>[\d,]+)\s+XP)(?:\s*,\s*or\s+(?<lair>[\d,]+)\s+in\s+lair)?\s*;\s*PB\s+(?<pb>[+-]\s?\d+)\s*\)")]
    private static partial Regex ChallengePattern();

    [GeneratedRegex(@"(?<type>Blindsight|Darkvision|Tremorsense|Truesight)\s+(?<feet>\d+)\s*ft\.?")]
    private static partial Regex SensePattern();

    [GeneratedRegex(@"Passive\s+Perception\s+(?<value>\d+)")]
    private static partial Regex PassivePerceptionPattern();

    // "Melee or Ranged" must precede the single-word alternatives: regex alternation is
    // ordered, so listing "Melee" first would match it and then fail on the "or" that
    // follows, losing all 19 of the SRD's dual-mode attacks.
    // Distances are written both "5 ft." and "5 feet" in the source; both are accepted.
    [GeneratedRegex(@"(?<kind>Melee or Ranged|Melee|Ranged)\s+Attack\s+Roll:\s*(?<bonus>[+-]\s?\d+)[^.]*?,\s*(?:reach\s+(?<reach>\d+)\s*(?:ft\.?|feet))?(?:\s*,?\s*(?:or|and)\s*)?(?:range\s+(?<range>\d+)(?:\s*/\s*(?<longRange>\d+))?\s*(?:ft\.?|feet))?")]
    private static partial Regex AttackHeaderPattern();

    // The parenthesised dice are optional: a few weak attacks deal a flat amount, which
    // the SRD prints as "Hit: 1 Piercing damage" with no dice and no average.
    [GeneratedRegex(@"(?<average>\d+)\s*(?:\((?<dice>\d+d\d+(?:\s*[+-]\s*\d+)?)\))?\s*(?<type>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)\s+damage")]
    private static partial Regex DamagePattern();
}
