using System.Globalization;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SrdExtract.Parsing;

/// <summary>
/// Reads the mechanics out of a spell's description.
/// </summary>
/// <remarks>
/// <para>
/// Spells need their own grammar rather than the stat block one, and the difference is
/// not cosmetic. A monster prints <c>Dexterity Saving Throw: DC 12</c> and
/// <c>14 (4d6) Acid damage</c>: an explicit DC and a precomputed average. A spell prints
/// <c>must succeed on a Dexterity saving throw</c> and <c>8d6 Fire damage</c> — no DC,
/// because it comes from the caster, and no average, because there is no single caster
/// to compute one for.
/// </para>
/// <para>
/// Reusing the monster classifier on spells was tried first and found every metadata
/// field correctly while detecting <em>zero</em> of 300 saving throws. Worth recording:
/// the failure was silent, and only visible because the extraction report counts what it
/// modelled.
/// </para>
/// </remarks>
internal static partial class SpellEffectParser
{
    /// <summary>Reads the saving throw a spell calls for, if any.</summary>
    public static SaveEffect? ParseSave(string text, IReadOnlyList<AppliedCondition> conditions)
    {
        var match = SavePattern().Match(text);

        if (!match.Success
            || !Enum.TryParse<Ability>(match.Groups["ability"].Value, ignoreCase: true, out var ability))
        {
            return null;
        }

        var damage = ParseDamage(text);

        // "or half as much damage on a successful one", "takes half as much damage only".
        var success = HalfDamagePattern().IsMatch(text)
            ? SaveSuccessOutcome.HalfDamage
            : SaveSuccessOutcome.NoEffect;

        return new SaveEffect(
            ability,
            // Null on purpose: a spell's DC is the caster's, not the spell's.
            DifficultyClass: null,
            ParseArea(text),
            damage,
            success,
            conditions);
    }

    /// <summary>
    /// Reads a spell's damage dice. Unlike a stat block there is no printed average, so
    /// the expression's own average stands in — which keeps the validator's
    /// average-matches-dice check meaningful for monsters without weakening it here.
    /// </summary>
    public static IReadOnlyList<AttackDamage> ParseDamage(string text)
    {
        var damage = new List<AttackDamage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in DamagePattern().Matches(text))
        {
            if (!Enum.TryParse<DamageType>(match.Groups["type"].Value, ignoreCase: true, out var type)
                || !DiceExpression.TryParse(match.Groups["dice"].Value, out var dice))
            {
                continue;
            }

            // A spell often restates its damage in the upcast clause; keep each distinct
            // dice/type pair once.
            if (seen.Add($"{dice}|{type}"))
            {
                damage.Add(new AttackDamage(dice, type, dice.Average));
            }
        }

        return damage;
    }

    /// <summary>
    /// Reads a spell's area: "20-foot-radius Sphere", "15-foot Cone", "60-foot Line",
    /// "10-foot Emanation".
    /// </summary>
    public static EffectArea? ParseArea(string text)
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

    // "must succeed on a Dexterity saving throw", "makes a Constitution saving throw",
    // "must make a Wisdom saving throw". Deliberately case-sensitive on the ability so it
    // cannot match prose about a "dexterity" score.
    /// <summary>
    /// Reads a single-target healing sentence: "regains a number of Hit Points equal to
    /// 2d8 plus your spellcasting ability modifier".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately narrow. It matches only where <em>one</em> creature regains hit
    /// points, because the mass spells choose several and the engine's casting call takes
    /// one target; approximating "up to six creatures" as one would heal a sixth of what
    /// the page promises and say nothing about it.
    /// </para>
    /// <para>
    /// The plural forms are excluded by requiring the singular verb "regains" preceded by
    /// no "creatures", which is how the printed sentences actually differ: Cure Wounds
    /// says "A creature you touch regains", Mass Cure Wounds says "Each target regains"
    /// after choosing six.
    /// </para>
    /// </remarks>
    public static SpellHeal? ParseHeal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (MultipleTargetsPattern().IsMatch(text))
        {
            return null;
        }

        var match = HealPattern().Match(text);

        if (!match.Success || !DiceExpression.TryParse(match.Groups["dice"].Value, out var dice))
        {
            return null;
        }

        return new SpellHeal(dice, match.Groups["modifier"].Success);
    }

    // "regains a number of Hit Points equal to 2d8 plus your spellcasting ability
    // modifier", and the same sentence without the modifier.
    [GeneratedRegex(
        @"regains?\s+(?:a\s+number\s+of\s+)?Hit\s+Points(?:\s+equal\s+to)?\s+(?<dice>\d+d\d+)" +
        @"(?<modifier>\s+plus\s+your\s+spellcasting\s+ability\s+modifier)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex HealPattern();

    // "Choose up to six creatures", "Up to five creatures of your choice" — a chosen set
    // the single-target casting call cannot express.
    [GeneratedRegex(@"\b(?:up\s+to\s+\w+\s+creatures|each\s+target)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MultipleTargetsPattern();

    [GeneratedRegex(@"\b(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\s+saving\s+throw\b")]
    private static partial Regex SavePattern();

    [GeneratedRegex(@"(?<dice>\d+d\d+)\s+(?<type>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)\s+damage")]
    private static partial Regex DamagePattern();

    [GeneratedRegex(@"(?<size>\d+)-foot(?:-radius)?(?:-long,?\s*(?<width>\d+)-foot-?\s?wide)?\s+(?<shape>Cone|Line|Emanation|Cube|Sphere|Cylinder)")]
    private static partial Regex AreaPattern();

    [GeneratedRegex(@"half as much damage", RegexOptions.IgnoreCase)]
    private static partial Regex HalfDamagePattern();
}
