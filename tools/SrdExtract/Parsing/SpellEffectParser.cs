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
            conditions,
            // "The target gains no benefit from Half Cover or Three-Quarters Cover for
            // this save" — Sacred Flame. Structured rather than left as prose, because
            // the day cover landed this sentence was the difference between the spell as
            // printed and a quietly weaker one.
            CoverIgnored: CoverIgnoredPattern().IsMatch(text),
            // "A Construct has Disadvantage on the save" — Shatter, exactly once in the
            // book, and the same lesson: Constructs are real opponents, and leaving the
            // sentence as prose would execute the spell weaker than print against
            // exactly the creatures it names.
            ConstructsSaveAtDisadvantage: ConstructDisadvantagePattern().IsMatch(text));
    }

    /// <summary>
    /// Reads an attack spell's condition rider: "On a hit, the target takes 2d8 Poison
    /// damage and has the Poisoned condition until the end of your next turn."
    /// </summary>
    /// <remarks>
    /// The shared stat-block grammar refuses this sentence — its head-clause rule wants
    /// the damage accounted for by a <c>Hit:</c> it never finds, because spells print
    /// "On a hit," where stat blocks print <c>Hit:</c> — so the spell grammar reads it
    /// here, deliberately whole: the damage half must match the spell's own damage
    /// grammar and the duration must be the one modelled shape ("until the end of your
    /// next turn" — the caster's, <c>ConditionDurationOwner.Source</c>), or no rider is
    /// produced and the shared grammar's refusal stands.
    /// </remarks>
    public static AppliedCondition? ParseAttackRider(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = AttackRiderPattern().Match(text);

        if (!match.Success
            || !Enum.TryParse<ConditionType>(match.Groups["condition"].Value, ignoreCase: false, out var condition))
        {
            return null;
        }

        return new AppliedCondition(
            condition,
            Duration: new ConditionDuration(ConditionClock.EndOfTurn, ConditionDurationOwner.Source));
    }

    /// <summary>
    /// True when the text prints Guiding Bolt's rider — "and the next attack roll made
    /// against it before the end of your next turn has Advantage" — whole.
    /// </summary>
    public static bool ParseNextAttackAdvantage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return NextAttackAdvantagePattern().IsMatch(text);
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

    /// <summary>
    /// Reads a revival sentence: "You touch a creature that has died within the last
    /// minute. That creature revives with 1 Hit Point." — Revivify's, whole, both
    /// sentences anchored so a looser resurrection (Raise Dead's ten days) stays
    /// unstructured rather than borrowing a window it does not print.
    /// </summary>
    public static SpellRevival? ParseRevival(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = RevivalPattern().Match(text);

        return match.Success
            ? new SpellRevival(int.Parse(match.Groups["hp"].Value, CultureInfo.InvariantCulture))
            : null;
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

    // "The target gains no benefit from Half Cover or Three-Quarters Cover for this
    // save." Deliberately whole: a looser pattern could match prose about cover that
    // grants rather than denies the benefit.
    [GeneratedRegex(@"gains\s+no\s+benefit\s+from\s+Half\s+Cover\s+or\s+Three-Quarters\s+Cover\s+for\s+this\s+save")]
    private static partial Regex CoverIgnoredPattern();

    // "A Construct has Disadvantage on the save." — whole, for the same reason.
    [GeneratedRegex(@"A\s+Construct\s+has\s+Disadvantage\s+on\s+the\s+save")]
    private static partial Regex ConstructDisadvantagePattern();

    // "and the next attack roll made against it before the end of your next turn has
    // Advantage" — Guiding Bolt, exactly once in the book. Whole, because a looser
    // pattern could match a rider whose window or beneficiary is different, and the
    // engine executes exactly this window on exactly this roll.
    [GeneratedRegex(
        @"the\s+next\s+attack\s+roll\s+made\s+against\s+it\s+before\s+the\s+end\s+of\s+your\s+next\s+turn\s+has\s+Advantage")]
    private static partial Regex NextAttackAdvantagePattern();

    // Revivify's two sentences, whole: the one-minute window and the revive-with-N
    // must both be printed for the shape to structure.
    [GeneratedRegex(
        @"You\s+touch\s+a\s+creature\s+that\s+has\s+died\s+within\s+the\s+last\s+minute\.\s+" +
        @"That\s+creature\s+revives\s+with\s+(?<hp>\d+)\s+Hit\s+Point")]
    private static partial Regex RevivalPattern();

    // "On a hit, the target takes 2d8 Poison damage and has the Poisoned condition
    // until the end of your next turn." The whole sentence, both halves anchored: the
    // damage must be the spell damage grammar's own shape, and the duration must be the
    // exactly modelled one.
    [GeneratedRegex(
        @"On\s+a\s+hit,\s+the\s+target\s+takes\s+\d+d\d+\s+\w+\s+damage\s+and\s+has\s+the\s+" +
        @"(?<condition>[A-Z][a-z]+)\s+condition\s+until\s+the\s+end\s+of\s+your\s+next\s+turn\.")]
    private static partial Regex AttackRiderPattern();

    /// <summary>
    /// Structures the Hold Person shape after the spell is built: a save whose failure
    /// imposes a condition "for the duration", with the printed repeat-save way out —
    /// and defuses the one spell whose effect is a menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The template is exact and matches the corpus exactly twice — Hold Person and
    /// Hold Monster — both Concentration up to 1 minute, so the structured duration is
    /// <see cref="ConditionDuration.ConcentrationUpToOneMinuteWithRepeatSave"/>: three
    /// ways out, whichever comes first. Hold Person's "Choose a Humanoid that you can
    /// see within range" is the only printed target-type gate in the book's own words,
    /// and it rides <see cref="SpellDefinition.TargetCreatureType"/>.
    /// </para>
    /// <para>
    /// Eyebite is the defusal: its effects are a per-turn menu — "one of the following
    /// effects of your choice" — and the shared grammar read each menu entry's bare
    /// sentence as a clean rider. Harmless while the casting path imposed nothing from
    /// a spell's save, armmed the day it started to: three conditions at once, none
    /// chosen, none ending. A chooser's-choice menu is unmodelled, and now says so on
    /// every rider it holds.
    /// </para>
    /// </remarks>
    public static SpellDefinition StructureHeldConditions(SpellDefinition spell)
    {
        if (spell.Save is not { } save)
        {
            return spell;
        }

        if (ChooserMenuPattern().IsMatch(spell.Text))
        {
            return spell with
            {
                Save = save with
                {
                    AppliedConditions = [.. save.AppliedConditions.Select(rider => rider with
                    {
                        UnmodelledRequirement = rider.UnmodelledRequirement
                            ?? "one of several effects of the caster's choice",
                    })],
                },
            };
        }

        var match = HeldConditionPattern().Match(spell.Text);

        if (!match.Success
            || !spell.RequiresConcentration
            || !spell.DurationText.Contains("1 minute", StringComparison.Ordinal)
            || !Enum.TryParse<ConditionType>(match.Groups["condition"].Value, ignoreCase: false, out var condition))
        {
            return spell;
        }

        return spell with
        {
            Save = save with
            {
                AppliedConditions =
                [
                    new AppliedCondition(
                        condition,
                        Duration: ConditionDuration.ConcentrationUpToOneMinuteWithRepeatSave),
                ],
            },
            TargetCreatureType = HumanoidTargetPattern().IsMatch(spell.Text)
                ? CreatureType.Humanoid
                : spell.TargetCreatureType,
        };
    }

    // "The target must succeed on a Wisdom saving throw or have the Paralyzed
    // condition for the duration. At the end of each of its turns, the target repeats
    // the save, ending the spell on itself on a success." Both sentences, whole: the
    // way out must be printed for the hold to be structured at all.
    [GeneratedRegex(
        @"must\s+succeed\s+on\s+a\s+\w+\s+saving\s+throw\s+or\s+have\s+the\s+" +
        @"(?<condition>[A-Z][a-z]+)\s+condition\s+for\s+the\s+duration\.\s+" +
        @"At\s+the\s+end\s+of\s+each\s+of\s+its\s+turns,\s+the\s+target\s+repeats\s+the\s+save,\s+" +
        @"ending\s+the\s+spell\s+on\s+itself\s+on\s+a\s+success\.")]
    private static partial Regex HeldConditionPattern();

    // Hold Person's printed target-type gate, the only one in the book's own words.
    [GeneratedRegex(@"Choose\s+a\s+Humanoid\s+that\s+you\s+can\s+see\s+within\s+range\.")]
    private static partial Regex HumanoidTargetPattern();

    // Eyebite: a menu of effects rather than an effect.
    [GeneratedRegex(@"be\s+affected\s+by\s+one\s+of\s+the\s+following\s+effects\s+of\s+your\s+choice")]
    private static partial Regex ChooserMenuPattern();
}
