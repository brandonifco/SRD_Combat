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
    /// <remarks>
    /// Runs the alignment-alternative pre-pass first (see
    /// <see cref="EvilCasterAlternativePattern"/> and
    /// <see cref="ParseEvilCasterDamageType"/> for the reading): Spirit Guardians'
    /// "takes 3d8 Radiant damage (if you are good or neutral) or 3d8 Necrotic damage
    /// (if you are evil)" is an either/or on one damage roll, not two rolls, and the
    /// matched span is masked out of <paramref name="text"/> before the generic
    /// <see cref="DamagePattern"/> harvest runs, so it is structurally unable to
    /// re-add the Necrotic branch as a second component.
    /// </remarks>
    public static IReadOnlyList<AttackDamage> ParseDamage(string text)
    {
        var damage = new List<AttackDamage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var (maskedText, primary) = MaskEvilCasterAlternative(text);

        if (primary is not null && seen.Add($"{primary.Amount}|{primary.Type}"))
        {
            damage.Add(primary);
        }

        foreach (Match match in DamagePattern().Matches(maskedText))
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
    /// Reads the damage type Spirit Guardians deals when the caster is evil — the
    /// non-null branch of the same alignment-alternative grammar <see cref="ParseDamage"/>
    /// masks out. Null for every spell that does not print this exact shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Spirit Guardians (SRD 5.2.1 p. 164) prints one damage roll with an
    /// alignment-gated type</b>: "On a failed save, the creature takes 3d8 Radiant
    /// damage (if you are good or neutral) or 3d8 Necrotic damage (if you are evil)."
    /// That "or" selects between two types; it never adds them. The generic damage
    /// harvest used to read the sentence as two components and the engine dealt both —
    /// 6d8 of two types against a printed 3d8 of one, a double print (#375, the same
    /// or-as-and shape as #371 wearing a spell). The gate is the caster's alignment, a
    /// dial this game does not model: character creation never asks for one, no
    /// <c>CharacterSheet</c> carries an alignment field, and no monster casts spells
    /// yet (F4).
    /// </para>
    /// <para>
    /// <b>The stated reading (designer sign-off, #375): a caster with no alignment is
    /// non-evil, so every casting the game can currently produce deals 3d8 Radiant</b>
    /// — the printed "good or neutral" branch, executed exactly. This is an
    /// interpretation where print branches on state the model lacks, not a divergence
    /// from a printed sentence: the Necrotic branch is <em>unreachable</em>, not
    /// unimplemented, and no reachable casting deviates from print by a die or a type.
    /// The branch is still not dropped: it is structured here as
    /// <see cref="SpellDefinition.EvilCasterDamageType"/> so the data states what print
    /// offers, rather than silently forgetting the alternative existed. Monsters carry
    /// their printed alignment (<c>MonsterDefinition.Alignment</c>), so when F4 puts
    /// spellcasting enemies in the pool, selecting Necrotic for an evil caster off that
    /// field is the implementation — and re-opening this reading is part of that work.
    /// </para>
    /// <para>
    /// The grammar requires the two parentheticals matched verbatim, both types parsing
    /// as <see cref="DamageType"/>, and the two dice expressions equal (print says 3d8
    /// both sides — the equal-dice requirement is part of the grammar, not an
    /// assumption). A half-match — unequal dice, or a type that fails to parse — is not
    /// this shape at all: nothing here falls back to summing the two clauses, which
    /// would re-create the exact or-as-and bug this reading closes. In this fixed
    /// source a half-match cannot occur; <c>SpellValidator</c>'s exact-count check is
    /// the tripwire if that ever stops being true.
    /// </para>
    /// </remarks>
    public static DamageType? ParseEvilCasterDamageType(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = EvilCasterAlternativePattern().Match(text);

        if (!match.Success
            || !string.Equals(match.Groups["diceA"].Value, match.Groups["diceB"].Value, StringComparison.Ordinal)
            || !Enum.TryParse<DamageType>(match.Groups["typeB"].Value, ignoreCase: true, out var typeB))
        {
            return null;
        }

        return typeB;
    }

    /// <summary>
    /// Matches Spirit Guardians' alignment-alternative sentence and, when it fully
    /// matches the grammar (equal dice on both branches, both types parseable), returns
    /// the text with that span masked out — spaces, so no other pattern's offsets shift
    /// — alongside the single primary-branch component it stands for. Returns the
    /// original text unchanged and no component when the sentence is absent or a
    /// half-match, so the generic harvest runs exactly as it always has.
    /// </summary>
    private static (string MaskedText, AttackDamage? Primary) MaskEvilCasterAlternative(string text)
    {
        var match = EvilCasterAlternativePattern().Match(text);

        if (!match.Success
            || !string.Equals(match.Groups["diceA"].Value, match.Groups["diceB"].Value, StringComparison.Ordinal)
            || !Enum.TryParse<DamageType>(match.Groups["typeA"].Value, ignoreCase: true, out var typeA)
            || !DiceExpression.TryParse(match.Groups["diceA"].Value, out var dice))
        {
            return (text, null);
        }

        var masked = string.Concat(
            text.AsSpan(0, match.Index),
            new string(' ', match.Length),
            text.AsSpan(match.Index + match.Length));

        return (masked, new AttackDamage(dice, typeA, dice.Average));
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

    // Spirit Guardians' printed either/or, exactly once in the book: "takes 3d8
    // Radiant damage (if you are good or neutral) or 3d8 Necrotic damage (if you are
    // evil)". Both parentheticals matched verbatim so a looser pattern cannot pick up
    // a differently-gated alternative this project has not read against print.
    [GeneratedRegex(
        @"takes\s+(?<diceA>\d+d\d+)\s+(?<typeA>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)" +
        @"\s+damage\s+\(if\s+you\s+are\s+good\s+or\s+neutral\)\s+or\s+" +
        @"(?<diceB>\d+d\d+)\s+(?<typeB>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)" +
        @"\s+damage\s+\(if\s+you\s+are\s+evil\)")]
    private static partial Regex EvilCasterAlternativePattern();

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
