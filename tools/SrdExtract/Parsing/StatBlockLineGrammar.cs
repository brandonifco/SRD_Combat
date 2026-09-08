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

    /// <summary>
    /// Parses the Legendary Actions section's own preamble paragraph, e.g. <c>Legendary
    /// Action Uses: 3 (4 in Lair). Immediately after another creature's turn, the
    /// aboleth can expend a use to take one of the following actions. The aboleth
    /// regains all expended uses at the start of each of its turns.</c> Every printed
    /// instance in the corpus uses this exact sentence shape (#423) — only the use
    /// counts and the bearer's own lowercase noun vary — so the whole sentence is
    /// matched and only the counts are extracted; a mismatch means the printed shape
    /// changed or the wrong text reached this parser, either of which the caller must
    /// treat as a failure rather than silently dropping the paragraph.
    /// </summary>
    public static (int Uses, int? UsesInLair)? ParseLegendaryActionUses(string text)
    {
        var match = LegendaryActionUsesPattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var uses = int.Parse(match.Groups["uses"].Value, CultureInfo.InvariantCulture);
        int? usesInLair = match.Groups["lair"].Success
            ? int.Parse(match.Groups["lair"].Value, CultureInfo.InvariantCulture)
            : null;

        return (uses, usesInLair);
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
    public static MonsterAttack? ParseAttack(string text, EntryCoverage coverage)
    {
        var header = AttackHeaderPattern().Match(text);
        if (!header.Success)
        {
            return null;
        }

        // The filler between the attack bonus and the reach/range clause is a
        // permissive [^.]* — it is what let nine printed conditional-Advantage
        // parentheticals (and the Ancient Gold Dragon's Rend, whose "to hit" sits in
        // the same slot) go unread while the whole header still looked claimed. Named
        // and excluded from the claim, per design §2.3 and §7.1.
        coverage.Claim(AttackHeaderPattern(), header, "attack.header", "unread");

        // Eight of those nine are one of two recognised attack-roll circumstances
        // (#666) — claimed back out of the "unread" hole when they match. The ninth,
        // the Doppelganger's "(with Advantage during the first round of each combat)",
        // is an encounter-clock predicate this deliberately does not recognise, so it
        // stays residue.
        var advantageCondition = ParseAdvantageCondition(header.Groups["unread"], coverage);

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
        IReadOnlyList<AttackDamage> damage;
        AlternativeAttackDamage? alternative = null;

        if (hitIndex < 0)
        {
            damage = [];
        }
        else
        {
            coverage.Claim(new TextSpan(hitIndex, "Hit:".Length), "attack.hit_label");
            (damage, alternative) = ParseDamage(text, hitIndex + "Hit:".Length, coverage);
        }

        return new MonsterAttack(kind, bonus, reach, normalRange, longRange, damage)
        {
            Alternative = alternative,
            AdvantageCondition = advantageCondition,
        };
    }

    /// <summary>
    /// Reads the header's "(with Advantage if the target …)" parenthetical (#666) out
    /// of the filler <see cref="AttackHeaderPattern"/> leaves unread, claiming it when
    /// it is one of the two circumstances this shape prints. Anchored start-to-end
    /// against the whole unread span rather than searched for within it, so a
    /// parenthetical this does not recognise — the Doppelganger's "(with Advantage
    /// during the first round of each combat)" — cannot partially match and leave a
    /// claimed fragment beside unclaimed residue; it fails whole and stays whole
    /// residue.
    /// </summary>
    private static AttackRollAdvantageCondition? ParseAdvantageCondition(Group unread, EntryCoverage coverage)
    {
        if (!unread.Success)
        {
            return null;
        }

        var match = AttackRollAdvantageConditionPattern().Match(unread.Value);
        if (!match.Success)
        {
            return null;
        }

        coverage.Claim(new TextSpan(unread.Index, unread.Length), "attack.advantage_condition");

        return match.Groups["grappler"].Success
            ? AttackRollAdvantageCondition.TargetIsGrappledByAttacker
            : AttackRollAdvantageCondition.TargetIsMissingHitPoints;
    }

    /// <summary>
    /// Reads the damage components starting at <paramref name="start"/>, stopping at
    /// the first thing that is not one, then checks whether an "or…if" alternative
    /// tier (#371) follows the last one. Riders after the damage ("If the target is a
    /// Large or smaller creature, ...") are left to the entry's prose. Operates on the
    /// whole entry's text with a start offset, rather than a pre-sliced substring, so
    /// every match's own <c>Index</c> is already an offset into that text — the
    /// coordinate space <see cref="EntryCoverage"/> claims into.
    /// </summary>
    private static (IReadOnlyList<AttackDamage> Damage, AlternativeAttackDamage? Alternative) ParseDamage(
        string text, int start, EntryCoverage coverage)
    {
        var damage = new List<AttackDamage>();
        var searchFrom = start;

        while (searchFrom < text.Length)
        {
            var match = DamagePattern().Match(text, searchFrom);
            if (!match.Success)
            {
                break;
            }

            // Only keep a run of components joined by "plus"; anything further into the
            // prose is a rider, not part of this attack's damage. A component the loop
            // breaks on here is never claimed by this loop — an or-alternative
            // (#371) is picked up below instead, once the loop settles.
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

            // DamagePattern is fully literal — digits, an optional literal-parenthesised
            // dice expression, one of the named damage types, the word "damage" — so
            // there is nothing permissive to exclude, and the whole match is claimed.
            coverage.Claim(new TextSpan(match.Index, match.Length), "attack.damage_component");

            damage.Add(new AttackDamage(dice, type, average, ReadCondition(text, componentEnd, limit, coverage)));

            searchFrom = componentEnd;
        }

        // searchFrom now sits exactly where the additive loop stopped consuming —
        // right after the last base component, whether the loop broke on a
        // non-"plus" continuation or ran out of DamagePattern matches entirely. An
        // alternative only makes sense relative to an established base, so this is
        // skipped when nothing was found above.
        AlternativeAttackDamage? alternative = null;
        if (damage.Count > 0)
        {
            // The em-dash "X—or Y if Z—plus W" chain (#409) is tried first, because it
            // consumes more than #371's comma-"or": both an alternative that replaces
            // the base component it follows and an unconditional "plus" component that
            // survives that swap. Only the Mimic's Bite and the Swarm of Venomous
            // Snakes' Bites print it. When it matches, the alternative is tagged with
            // the index of the base component it replaces — the last base the loop read,
            // which is index 0 in both corpus entries — and the "plus" is appended to the
            // Damage list so AttackRules.RollDamage always rolls it.
            var emDash = ReadEmDashAlternative(text, searchFrom, coverage);
            if (emDash is { } combined)
            {
                alternative = combined.Alternative with { ReplacesComponentIndex = damage.Count - 1 };
                damage.Add(combined.Plus);
            }
            else
            {
                alternative = ReadAlternative(text, searchFrom, coverage);
            }
        }

        return (damage, alternative);
    }

    /// <summary>
    /// Reads the em-dash "X—or Y if Z—plus W" chain (#409) — the Mimic's Bite
    /// ("…Piercing damage—or 12 (2d8 + 3) Piercing damage if the target is Grappled by
    /// the mimic—plus 4 (1d8) Acid damage", SRD 5.2.1 p. 309) and the Swarm of Venomous
    /// Snakes' Bites ("…Piercing damage—or 6 (1d4 + 4) Piercing damage if the swarm is
    /// Bloodied—plus 10 (3d6) Poison damage", p. 363) — when it starts exactly at
    /// <paramref name="start"/>, right after the base component.
    /// </summary>
    /// <remarks>
    /// These are the corpus's only two em-dash instances (design §11.1), and the em dash
    /// itself survives text extraction as an ASCII hyphen — <c>damage-or</c>,
    /// <c>mimic-plus</c> — so the pattern joins on <c>[-–—]</c> rather than a comma, the
    /// one shape #371's <see cref="AlternativeDamagePattern"/> deliberately does not
    /// reach. Unlike that pattern, this one also reads the trailing "plus" component and
    /// returns it, because the whole point of #409 is that the Acid/Poison is dealt
    /// regardless of which Piercing tier applies. The condition is one of exactly the two
    /// the corpus prints in this shape — the swarm's own Bloodied state and the target
    /// being Grappled by the attacker — both checkable at the moment the attack hits;
    /// nothing wider is attempted, so any other condition would simply fail to match and
    /// fall to residue, per <see cref="ReadAlternative"/>'s own reasoning (design §4.3).
    /// The grappler's bare-word name (<c>the mimic</c>) is claimed whole, bounded on both
    /// sides by literal text and unable to swallow an adjacent clause, the same reading
    /// <see cref="ReadAlternative"/> already states for the Advantage branch's creature
    /// name.
    /// </remarks>
    private static (AlternativeAttackDamage Alternative, AttackDamage Plus)? ReadEmDashAlternative(
        string text, int start, EntryCoverage coverage)
    {
        var match = EmDashAlternativeDamagePattern().Match(text, start);

        if (!match.Success || match.Index != start)
        {
            return null;
        }

        if (!Enum.TryParse<DamageType>(match.Groups["altType"].Value, ignoreCase: true, out var altType)
            || !Enum.TryParse<DamageType>(match.Groups["plusType"].Value, ignoreCase: true, out var plusType))
        {
            return null;
        }

        if (!TryReadAmount(match.Groups["altAverage"], match.Groups["altDice"], out var altDice, out var altAverage)
            || !TryReadAmount(match.Groups["plusAverage"], match.Groups["plusDice"], out var plusDice, out var plusAverage))
        {
            return null;
        }

        var condition = match.Groups["bloodiedSelf"].Success
            ? AttackDamageCondition.AttackerIsBloodied
            : AttackDamageCondition.TargetIsGrappledByAttacker;

        // The whole chain is claimed as one span — leading "-or", the "if…" condition
        // (grappler name included, per this method's remarks), and the "-plus" tail — so
        // both the alternative and the unconditional plus leave residue together.
        coverage.Claim(EmDashAlternativeDamagePattern(), match, "attack.emdash_alternative_damage");

        return (
            new AlternativeAttackDamage(altDice, altType, altAverage, condition),
            new AttackDamage(plusDice, plusType, plusAverage));
    }

    /// <summary>
    /// Reads a damage amount from a matched average group and its optional dice group,
    /// the shape shared by <see cref="DamagePattern"/> and its siblings: dice when
    /// present, otherwise a flat amount equal to the printed average.
    /// </summary>
    private static bool TryReadAmount(Group averageGroup, Group diceGroup, out DiceExpression dice, out int average)
    {
        average = int.Parse(averageGroup.Value, CultureInfo.InvariantCulture);

        // No parenthesised dice means a flat amount — "Hit: 1 Piercing damage".
        dice = DiceExpression.Flat(average);

        if (diceGroup.Success)
        {
            if (!DiceExpression.TryParse(diceGroup.Value, out var rolled))
            {
                return false;
            }

            dice = rolled;
        }

        return true;
    }

    /// <summary>
    /// Reads an "or…if" alternative damage tier (#371) — "or 18 (4d6 + 4) Piercing
    /// damage if the chimera had Advantage on the attack roll" — when one of the
    /// three conditions this engine can check at the moment an attack hits follows
    /// the base damage. Every other printed alternative condition (a charge — "if the
    /// goat moved 20+ feet straight toward the target immediately before the hit" —
    /// the Goat's and the Giant Seahorse's Ram) is not a matched shape at all: the
    /// engine tracks no movement history to check it against, so the pattern does not
    /// reach for it, and the whole clause falls to residue exactly as an unclaimed
    /// span always does (design §4.3 — doubt lands in residue, never a false claim).
    /// </summary>
    private static AlternativeAttackDamage? ReadAlternative(string text, int start, EntryCoverage coverage)
    {
        var match = AlternativeDamagePattern().Match(text, start);

        if (!match.Success || match.Index != start)
        {
            return null;
        }

        if (!Enum.TryParse<DamageType>(match.Groups["type"].Value, ignoreCase: true, out var type))
        {
            return null;
        }

        var average = int.Parse(match.Groups["average"].Value, CultureInfo.InvariantCulture);

        DiceExpression dice;
        if (match.Groups["dice"].Success)
        {
            if (!DiceExpression.TryParse(match.Groups["dice"].Value, out var rolled))
            {
                return null;
            }

            dice = rolled;
        }
        else
        {
            dice = DiceExpression.Flat(average);
        }

        var condition = match.Groups["bloodiedSelf"].Success ? AttackDamageCondition.AttackerIsBloodied
            : match.Groups["bloodiedTarget"].Success ? AttackDamageCondition.TargetIsBloodied
            : AttackDamageCondition.AttackRollHadAdvantage;

        // The creature's own name in the Advantage branch ("the chimera had
        // Advantage...") is claimed whole rather than excluded, the same reasoning
        // MultiattackSubjectPattern's own creature name claim states (design §7.4):
        // the anchoring — bounded on both sides by literal text, one bare word, unable
        // to reach past its own slot — is what justifies the claim, not that the
        // specific name is read into structure. Unlike a genuinely permissive
        // subexpression, \w+ here cannot swallow an adjacent unmodelled clause, so
        // there is no unread group to exclude — the same shape as
        // MultiattackSubjectPattern's own claim call.
        coverage.Claim(AlternativeDamagePattern(), match, "attack.alternative_damage");

        return new AlternativeAttackDamage(dice, type, average, condition);
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
    private static AttackDamageCondition? ReadCondition(string text, int from, int limit, EntryCoverage coverage)
    {
        if (from >= limit)
        {
            return null;
        }

        var sentenceEnd = text.IndexOf('.', from);
        var clauseEnd = sentenceEnd < 0 ? limit : Math.Min(sentenceEnd, limit);
        var clause = text[from..clauseEnd];

        var qualifierOffset = clause.IndexOf("if the attack roll had Advantage", StringComparison.OrdinalIgnoreCase);

        if (qualifierOffset < 0)
        {
            return null;
        }

        // Only this literal qualifier phrase is claimed — the read structure is just
        // the enum value it decides, not the surrounding punctuation or whitespace.
        coverage.Claim(
            new TextSpan(from + qualifierOffset, "if the attack roll had Advantage".Length),
            "attack.damage_condition");

        return AttackDamageCondition.AttackRollHadAdvantage;
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

    // Anchored at both ends deliberately: the whole paragraph is boilerplate except the
    // two counts and the bearer's own noun, verified identical across all 30 printed
    // instances (#423), so a mismatch is meant to be loud rather than a partial match
    // that quietly accepts a changed sentence.
    [GeneratedRegex(@"^Legendary Action Uses:\s*(?<uses>\d+)(?:\s*\((?<lair>\d+)\s+in Lair\))?\.\s*" +
        @"Immediately after another creature's turn, the [a-z]+ can expend a use to take one of the following actions\.\s*" +
        @"The [a-z]+ regains all expended uses at the start of each of its turns\.$")]
    private static partial Regex LegendaryActionUsesPattern();

    [GeneratedRegex(@"(?<type>Blindsight|Darkvision|Tremorsense|Truesight)\s+(?<feet>\d+)\s*ft\.?")]
    private static partial Regex SensePattern();

    [GeneratedRegex(@"Passive\s+Perception\s+(?<value>\d+)")]
    private static partial Regex PassivePerceptionPattern();

    // "Melee or Ranged" must precede the single-word alternatives: regex alternation is
    // ordered, so listing "Melee" first would match it and then fail on the "or" that
    // follows, losing all 19 of the SRD's dual-mode attacks.
    // Distances are written both "5 ft." and "5 feet" in the source; both are accepted.
    // The (?<unread>[^.]*?) filler between the bonus and the reach/range clause is
    // matched but never inspected by this pattern itself — ParseAdvantageCondition
    // reads it back out for the eight recognised attack-roll circumstances (#666);
    // the Ancient Gold Dragon's Rend's bare "to hit" and the Doppelganger's "(with
    // Advantage during the first round of each combat)" sit in the same slot and are
    // read by nobody, so the group stays named and excluded from AttackHeaderPattern's
    // own claim (design §2.3, §7.1).
    [GeneratedRegex(@"(?<kind>Melee or Ranged|Melee|Ranged)\s+Attack\s+Roll:\s*(?<bonus>[+-]\s?\d+)(?<unread>[^.]*?),\s*(?:reach\s+(?<reach>\d+)\s*(?:ft\.?|feet))?(?:\s*,?\s*(?:or|and)\s*)?(?:range\s+(?<range>\d+)(?:\s*/\s*(?<longRange>\d+))?\s*(?:ft\.?|feet))?")]
    private static partial Regex AttackHeaderPattern();

    // The two attack-roll Advantage circumstances (#666), matched against the whole of
    // AttackHeaderPattern's "unread" capture rather than searched for within it —
    // anchored start-to-end so a parenthetical this does not recognise (the
    // Doppelganger's "during the first round of each combat") fails whole rather than
    // matching a fragment and leaving the rest as a suspicious partial claim. The
    // grappler's bare-word name is claimed anyway, on AlternativeDamagePattern's and
    // EmDashAlternativeDamagePattern's own stated precedent (design §2.3) — out of the
    // wildcard convention's scope regardless, and pinned by CorpusRoundTripTests'
    // trip-wire (#412's pattern) rather than trusted silently.
    [GeneratedRegex(
        @"^\s*\(with\s+Advantage\s+if\s+the\s+target\s+(?:" +
        @"is\s+Grappled\s+by\s+the\s+(?<grappler>\w+)" +
        @"|doesn't\s+have\s+all\s+its\s+Hit\s+Points" +
        @")\)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex AttackRollAdvantageConditionPattern();

    // The parenthesised dice are optional: a few weak attacks deal a flat amount, which
    // the SRD prints as "Hit: 1 Piercing damage" with no dice and no average.
    [GeneratedRegex(@"(?<average>\d+)\s*(?:\((?<dice>\d+d\d+(?:\s*[+-]\s*\d+)?)\))?\s*(?<type>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)\s+damage")]
    private static partial Regex DamagePattern();

    // "or 18 (4d6 + 4) Piercing damage if the chimera had Advantage on the attack
    // roll", "or 2 (1d4) Piercing damage if the swarm is Bloodied", "or 6 (1d8 + 2)
    // Piercing damage if the target is Bloodied" (#371). The damage half repeats
    // DamagePattern's own literal shape rather than reusing it, because this claim
    // covers the leading ", or " and the trailing "if…" condition too — a single
    // combined match, claimed whole by ReadAlternative, keyed on which of the three
    // condition branches matched. Anchored at the ready-made choice of exactly three
    // literal condition phrases: nothing wider is attempted, so a charge condition
    // ("if the goat moved 20+ feet straight toward the target immediately before the
    // hit") simply fails to match and its clause falls to residue, per this method's
    // own remarks. The Advantage branch's creature name is a bare \w+ — out of the
    // wildcard convention's scope regardless (design §2.3), and claimed anyway: see
    // ReadAlternative's own remarks for why, the same reasoning
    // MultiattackSubjectPattern's creature-name claim already states.
    [GeneratedRegex(
        @",\s*or\s+(?<average>\d+)\s*(?:\((?<dice>\d+d\d+(?:\s*[+-]\s*\d+)?)\))?\s*" +
        @"(?<type>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)" +
        @"\s+damage\s+if\s+(?:" +
        @"(?<bloodiedSelf>the\s+swarm\s+is\s+Bloodied)" +
        @"|(?<bloodiedTarget>the\s+target\s+is\s+Bloodied)" +
        @"|the\s+(?<subject>\w+)\s+had\s+Advantage\s+on\s+the\s+attack\s+roll" +
        @")",
        RegexOptions.IgnoreCase)]
    private static partial Regex AlternativeDamagePattern();

    // The em-dash "X—or Y if Z—plus W" chain (#409): the Mimic's Bite and the Swarm of
    // Venomous Snakes' Bites, the corpus's only two em-dash instances (design §11.1). The
    // em dash survives PDF text extraction as an ASCII hyphen (damage-or, mimic-plus), so
    // both joins accept [-–—] — hyphen, en dash or em dash — rather than the comma
    // AlternativeDamagePattern requires; that comma-vs-dash difference is the whole reason
    // #371 left these two as residue. The condition is one of exactly two branches — the
    // swarm's own Bloodied state and the target being Grappled by the attacker, both
    // checkable at hit time — and no wider; the grappler's name is a bare \w+, out of the
    // wildcard convention's scope (design §2.3) and claimed anyway, the same reasoning
    // AlternativeDamagePattern's own subject group states. The trailing "-plus W"
    // component is read and returned because it is dealt regardless of which tier applies.
    [GeneratedRegex(
        @"[-–—]\s*or\s+(?<altAverage>\d+)\s*(?:\((?<altDice>\d+d\d+(?:\s*[+-]\s*\d+)?)\))?\s*" +
        @"(?<altType>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)" +
        @"\s+damage\s+if\s+(?:" +
        @"(?<bloodiedSelf>the\s+swarm\s+is\s+Bloodied)" +
        @"|the\s+target\s+is\s+Grappled\s+by\s+the\s+(?<grappler>\w+)" +
        @")" +
        @"[-–—]\s*plus\s+(?<plusAverage>\d+)\s*(?:\((?<plusDice>\d+d\d+(?:\s*[+-]\s*\d+)?)\))?\s*" +
        @"(?<plusType>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)" +
        @"\s+damage",
        RegexOptions.IgnoreCase)]
    private static partial Regex EmDashAlternativeDamagePattern();
}
