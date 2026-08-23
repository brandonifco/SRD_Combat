using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SrdExtract.Pdf;
using SRDCombat.Core.Dice;

namespace SrdExtract.Parsing;

/// <summary>The result of parsing the Spell Descriptions.</summary>
public sealed record SpellParseResult(
    IReadOnlyList<SpellDefinition> Spells,
    IReadOnlyList<ParseDiagnostic> Diagnostics);

/// <summary>
/// Parses the SRD's Spell Descriptions.
/// </summary>
/// <remarks>
/// The most regular section in the book: a heading, a level/school/classes line in
/// italic, four labelled lines, then prose. A spell is detected structurally — a heading
/// followed by a line matching the level/school grammar — rather than from a list of
/// expected names, the same approach the species and background parsers use.
/// </remarks>
public static partial class SpellParser
{
    private const string HeadingFont = "GillSans-SemiBold";

    /// <summary>
    /// The one spell heading the book sets in small caps, and what it should read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Acid Splash's heading is <c>GillSans-SemiBold-SC700</c>, and small caps come out
    /// of the text layer as <c>Ac i d Sp lASh</c> — letters split into runs and the case
    /// scrambled, with no way to tell a letter gap from a word gap. It is the only spell
    /// heading in the chapter set this way; every other SC700 line is a sidebar title or
    /// a stat block's ability row.
    /// </para>
    /// <para>
    /// So it is repaired from a curated map rather than by a rule inferred from one
    /// example, and the map is keyed on the exact mangled text: if a better reader ever
    /// renders it correctly, the key stops matching and the spell arrives under its real
    /// name rather than being silently rewritten. This is the same bargain
    /// <c>KnownCorrections</c> makes.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> SmallCapsHeadings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Ac i d Sp lASh"] = "Acid Splash",
        };
    private const double MinimumHeadingHeight = 7.6;
    private const double MaximumHeadingHeight = 9.2;

    private static readonly string[] Labels = ["Casting Time:", "Range:", "Components:", "Duration:"];

    public static SpellParseResult Parse(IReadOnlyList<SourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var spells = new List<SpellDefinition>();
        var diagnostics = new List<ParseDiagnostic>();

        SpellBuilder? current = null;

        lines = JoinWrappedTypeLines(lines);

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            if (current.TryBuild(out var spell, out var reason))
            {
                spells.Add(spell);
            }
            else
            {
                diagnostics.Add(new ParseDiagnostic(current.Name, reason));
            }

            current = null;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (IsHeading(line) && NextLineIsSpellType(lines, index))
            {
                Flush();
                current = new SpellBuilder(HeadingName(line), line.Page);
                continue;
            }

            current?.Accept(line);
        }

        Flush();

        return new SpellParseResult(spells, diagnostics);
    }

    private static bool IsHeading(SourceLine line) =>
        (line.Font == HeadingFont || SmallCapsHeadings.ContainsKey(line.Text.Trim()))
        && line.Height >= MinimumHeadingHeight
        && line.Height <= MaximumHeadingHeight
        && line.Text.Length > 0;

    /// <summary>The spell's name, repairing the one heading the book sets in small caps.</summary>
    private static string HeadingName(SourceLine line)
    {
        var text = line.Text.Trim();

        return SmallCapsHeadings.TryGetValue(text, out var repaired) ? repaired : text;
    }

    private static bool NextLineIsSpellType(IReadOnlyList<SourceLine> lines, int index) =>
        index + 1 < lines.Count && SpellTypePattern().IsMatch(lines[index + 1].Text.Trim());

    /// <summary>
    /// Joins a level/school/classes line that wrapped onto a second line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A spell with enough classes to overflow the column prints them across two lines —
    /// <c>Level 1 Abjuration (Bard, Cleric, Druid, Paladin,</c> then <c>Ranger)</c> — and
    /// the type grammar is anchored on its closing bracket, so the wrapped ones matched
    /// nothing and their spells were never detected at all. That dropped <b>38 of the
    /// book's 339 spells</b>, Cure Wounds among them, and nothing noticed because a spell
    /// that is never detected produces no diagnostic to report.
    /// </para>
    /// <para>
    /// Rejoining before parsing keeps the fix in one place: every later test of the type
    /// line sees a single complete one, exactly as an unwrapped spell always did.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<SourceLine> JoinWrappedTypeLines(IReadOnlyList<SourceLine> lines)
    {
        var joined = new List<SourceLine>(lines.Count);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (index + 1 < lines.Count && UnterminatedSpellType().IsMatch(line.Text.Trim()))
            {
                var continuation = lines[index + 1];

                // Only a line that closes the bracket, so an unterminated line followed by
                // something else is left alone to fail as loudly as it did before.
                if (continuation.Text.Contains(')', StringComparison.Ordinal))
                {
                    // Constructed rather than cloned with `with`: SourceLine.Text is a
                    // property initialiser, and a record's copy constructor copies
                    // backing fields without re-running initialisers — so a `with` clone
                    // would carry the *unjoined* text and change nothing.
                    joined.Add(new SourceLine(
                        line.Page,
                        line.Column,
                        line.Baseline,
                        [.. line.Words, .. continuation.Words]));
                    index++;
                    continue;
                }
            }

            joined.Add(line);
        }

        return joined;
    }

    /// <summary>Accumulates one spell as its lines arrive.</summary>
    private sealed class SpellBuilder(string name, int page)
    {
        private readonly Dictionary<string, StringBuilder> _labelled = new(StringComparer.Ordinal);
        private readonly StringBuilder _body = new();
        private readonly StringBuilder _scaling = new();
        private string? _typeLine;
        private string? _currentLabel;
        private bool _inScaling;

        public string Name { get; } = name;

        public void Accept(SourceLine line)
        {
            var text = line.Text.Trim();

            if (text.Length == 0)
            {
                return;
            }

            if (_typeLine is null && SpellTypePattern().IsMatch(text))
            {
                _typeLine = text;
                return;
            }

            foreach (var label in Labels)
            {
                if (text.StartsWith(label, StringComparison.Ordinal))
                {
                    _currentLabel = label;
                    _labelled[label] = new StringBuilder(text[label.Length..].Trim());
                    return;
                }
            }

            // A labelled value can wrap; anything in the body font ends the header block.
            if (_currentLabel is not null && line.Font == HeadingFont)
            {
                AppendWrapped(_labelled[_currentLabel], text);
                return;
            }

            _currentLabel = null;

            // "Using a Higher-Level Spell Slot." and "Cantrip Upgrade." open the scaling
            // clause, which runs to the end of the spell.
            if (ScalingHeading().IsMatch(text))
            {
                _inScaling = true;
            }

            AppendWrapped(_inScaling ? _scaling : _body, text);
        }

        public bool TryBuild(out SpellDefinition spell, out string reason)
        {
            spell = null!;

            if (_typeLine is null)
            {
                reason = "no level/school line — probably not a spell heading.";
                return false;
            }

            var type = SpellTypePattern().Match(_typeLine);

            var school = type.Groups["school"].Success
                ? type.Groups["school"].Value
                : type.Groups["cantripSchool"].Value;

            if (!Enum.TryParse<MagicSchool>(school, ignoreCase: true, out var parsedSchool))
            {
                reason = $"'{school}' is not a school of magic.";
                return false;
            }

            var level = type.Groups["level"].Success
                ? int.Parse(type.Groups["level"].Value, CultureInfo.InvariantCulture)
                : 0;

            var body = _body.ToString().Trim();
            var castingTime = Value("Casting Time:");
            var duration = Value("Duration:");
            var range = Value("Range:");

            var (components, material) = ParseComponents(Value("Components:"));

            // Conditions use the shared grammar; saves, damage and areas need the spell
            // grammar, which differs from the stat block one in substance rather than
            // wording. See SpellEffectParser.
            //
            // consultInertList: false — the bestiary's KnownInertEntries is curated
            // about stat block and trait text, not spell prose. Water Breathing shared
            // its exact name with that list's own "Water Breathing" (a monster trait)
            // and rode it to EntryMechanics.Narrative by accident rather than by anyone
            // reading the spell (#349). A spell that does nothing in a fight already has
            // a home: Unmodelled, the same place Light, Alarm and Comprehend Languages
            // land, with no completeness claim attached either way (see
            // SpellDefinition.UnclassifiedClauses). See the doc comment on
            // EntryMechanicsParser.KnownInertEntries for why a spell-specific curated
            // list was rejected instead.
            var classified = EntryMechanicsParser.ClassifyTrait(Name, body, consultInertList: false);
            var isSpellAttack = SpellAttackPattern().IsMatch(body);
            var save = SpellEffectParser.ParseSave(body, classified.AppliedConditions);

            // An attack spell's rider is read by the spell grammar — the shared
            // grammar's head-clause rule refuses "On a hit," sentences it cannot
            // account for — and only a rider parsed whole replaces the refused one.
            var attackRider = isSpellAttack ? SpellEffectParser.ParseAttackRider(body) : null;

            // Healing is read only when the spell neither attacks nor forces a save, so
            // a spell that damages on a save and heals on a success cannot be mistaken
            // for a healing spell. Revival is gated the same way.
            var heal = save is null && !isSpellAttack ? SpellEffectParser.ParseHeal(body) : null;
            var revival = save is null && !isSpellAttack ? SpellEffectParser.ParseRevival(body) : null;

            var mechanics = save is not null
                ? EntryMechanics.SavingThrow
                : isSpellAttack
                    ? EntryMechanics.Attack
                    : heal is not null || revival is not null
                        ? EntryMechanics.Healing
                        : classified.Mechanics;

            spell = new SpellDefinition
            {
                Id = OriginParser.MakeId("spell", Name),
                Name = Name,
                Level = level,
                School = parsedSchool,
                Classes = type.Groups["classes"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray(),
                CastingTime = ParseCastingTime(castingTime),
                CastingTimeText = castingTime,
                IsRitual = castingTime.Contains("Ritual", StringComparison.OrdinalIgnoreCase),
                RangeText = range,
                RangeFeet = ParseRangeFeet(range),
                Components = components,
                MaterialComponent = material,
                DurationText = duration,
                RequiresConcentration = duration.Contains("Concentration", StringComparison.OrdinalIgnoreCase),
                Text = body,
                Mechanics = mechanics,
                Save = save,
                Heal = heal,
                Revival = revival,
                Damage = SpellEffectParser.ParseDamage(body),
                AppliedConditions = attackRider is not null ? [attackRider] : classified.AppliedConditions,
                IsSpellAttack = isSpellAttack,
                // Guiding Bolt's rider, structured only on an attack spell: the
                // sentence hangs off "On a hit", and only a hit can land it.
                GrantsAdvantageAgainstTargetOnHit =
                    isSpellAttack && SpellEffectParser.ParseNextAttackAdvantage(body),
                // A structured spell carries no clause list, and that is a decision
                // rather than a gap: the stat-block leftover accounting is measurably
                // worse than useless on spell prose, so nothing here claims a spell is
                // fully modelled and PreparableSpells remains the authority. The whole
                // argument, with the numbers, is on SpellDefinition.UnclassifiedClauses
                // — read it before wiring an accounting pass in here (#292).
                UnclassifiedClauses = mechanics == EntryMechanics.Unmodelled
                    ? classified.UnmodelledClauses
                    : [],
                ScalingText = _scaling.Length > 0 ? _scaling.ToString().Trim() : null,
                SourcePage = page,
            };

            spell = SpellEffectParser.StructureHeldConditions(spell);

            spell = spell with
            {
                UpcastDicePerSlotLevel = ParseSlotScaling(spell),
                CantripUpgradeDice = ParseCantripScaling(spell),
            };

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Structures "The healing increases by 2d8 for each spell slot level above 1",
        /// refusing anything that does not line up exactly.
        /// </summary>
        /// <remarks>
        /// Three checks, each because getting it wrong would misprice a slot: the shape
        /// must match, the printed "above N" must be the spell's own level (it is for
        /// all 42 spells that match, verified against the whole book), and the die must
        /// be the same size as the base effect's so the two can roll as one expression.
        /// A sentence failing any of them stays text on ScalingText, visible and inert.
        /// </remarks>
        private static DiceExpression? ParseSlotScaling(SpellDefinition definition)
        {
            if (definition.ScalingText is not { } text
                || SlotScaling().Match(text) is not { Success: true } match
                || int.Parse(match.Groups["above"].Value, CultureInfo.InvariantCulture) != definition.Level
                || !DiceExpression.TryParse(match.Groups["dice"].Value, out var dice))
            {
                return null;
            }

            return BaseEffectSides(definition) == dice.Sides ? dice : null;
        }

        private static DiceExpression? ParseCantripScaling(SpellDefinition definition)
        {
            if (!definition.IsCantrip
                || definition.ScalingText is not { } text
                || CantripScaling().Match(text) is not { Success: true } match
                || !DiceExpression.TryParse(match.Groups["dice"].Value, out var dice))
            {
                return null;
            }

            return BaseEffectSides(definition) == dice.Sides ? dice : null;
        }

        /// <summary>The die the spell's base effect rolls, whichever shape it has.</summary>
        private static int? BaseEffectSides(SpellDefinition definition) =>
            definition.Heal?.Dice.Sides
            ?? (definition.Damage.Count > 0 ? definition.Damage[0].Amount.Sides : (int?)null)
            ?? (definition.Save?.FailureDamage.Count > 0 ? definition.Save.FailureDamage[0].Amount.Sides : (int?)null);

        private string Value(string label) =>
            _labelled.TryGetValue(label, out var value) ? value.ToString().Trim() : string.Empty;

        private static SpellCastingTime ParseCastingTime(string text)
        {
            if (text.StartsWith("Bonus Action", StringComparison.OrdinalIgnoreCase))
            {
                return SpellCastingTime.BonusAction;
            }

            if (text.StartsWith("Reaction", StringComparison.OrdinalIgnoreCase))
            {
                return SpellCastingTime.Reaction;
            }

            // "Action", and "Action or Ritual", both cost an Action in a fight.
            return text.StartsWith("Action", StringComparison.OrdinalIgnoreCase)
                ? SpellCastingTime.Action
                : SpellCastingTime.Extended;
        }

        private static (SpellComponents Components, string? Material) ParseComponents(string text)
        {
            var components = SpellComponents.None;

            if (text.Contains('V', StringComparison.Ordinal))
            {
                components |= SpellComponents.Verbal;
            }

            if (text.Contains('S', StringComparison.Ordinal))
            {
                components |= SpellComponents.Somatic;
            }

            var material = MaterialPattern().Match(text);

            if (material.Success)
            {
                components |= SpellComponents.Material;
            }

            return (components, material.Success ? material.Groups["material"].Value.Trim() : null);
        }

        /// <summary>
        /// The numeric range in feet, where there is one. "Self (15-foot Cone)" gives
        /// none: the 15 feet is the area's size, not how far away it can be cast.
        /// </summary>
        private static int? ParseRangeFeet(string text)
        {
            if (text.StartsWith("Self", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Touch", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var match = RangeFeetPattern().Match(text);

            return match.Success
                ? int.Parse(match.Groups["feet"].Value, CultureInfo.InvariantCulture)
                : match.Success ? null : ParseMiles(text);
        }

        private static int? ParseMiles(string text)
        {
            var miles = MilesPattern().Match(text);

            return miles.Success
                ? int.Parse(miles.Groups["miles"].Value, CultureInfo.InvariantCulture) * 5_280
                : null;
        }
    }

    private static void AppendWrapped(StringBuilder builder, string line)
    {
        if (builder.Length == 0)
        {
            builder.Append(line);
            return;
        }

        if (builder[^1] == '-' && char.IsLower(line[0]))
        {
            builder.Length -= 1;
            builder.Append(line);
            return;
        }

        builder.Append(' ').Append(line);
    }

    [GeneratedRegex(@"^(?:Level (?<level>\d)\s+(?<school>Abjuration|Conjuration|Divination|Enchantment|Evocation|Illusion|Necromancy|Transmutation)|(?<cantripSchool>Abjuration|Conjuration|Divination|Enchantment|Evocation|Illusion|Necromancy|Transmutation)\s+Cantrip)\s*\((?<classes>[^)]+)\)$")]
    private static partial Regex SpellTypePattern();

    // A type line whose class list has not closed by the end of the line: the same
    // grammar as SpellTypePattern up to the bracket, deliberately sharing its school
    // list so the two cannot drift apart.
    [GeneratedRegex(@"^(?:Level \d\s+(?:Abjuration|Conjuration|Divination|Enchantment|Evocation|Illusion|Necromancy|Transmutation)|(?:Abjuration|Conjuration|Divination|Enchantment|Evocation|Illusion|Necromancy|Transmutation)\s+Cantrip)\s*\([^)]*$")]
    private static partial Regex UnterminatedSpellType();

    [GeneratedRegex(@"M\s*\((?<material>[^)]+)\)")]
    private static partial Regex MaterialPattern();

    [GeneratedRegex(@"^(?<feet>\d+)\s*(?:feet|foot|ft\.?)")]
    private static partial Regex RangeFeetPattern();

    [GeneratedRegex(@"^(?<miles>\d+)\s*miles?")]
    private static partial Regex MilesPattern();

    [GeneratedRegex(@"^Using a Higher-Level Spell Slot\. The (?:healing|damage) increases by (?<dice>\d+d\d+) for each spell slot level above (?<above>\d+)\.")]
    private static partial Regex SlotScaling();

    [GeneratedRegex(@"^Cantrip Upgrade\. The damage increases by (?<dice>\d+d\d+) when you reach levels 5\b")]
    private static partial Regex CantripScaling();

    [GeneratedRegex(@"^(?:Using a Higher-Level Spell Slot|Cantrip Upgrade)\b")]
    private static partial Regex ScalingHeading();

    [GeneratedRegex(@"\b(?:melee|ranged)\s+spell\s+attack\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpellAttackPattern();
}
