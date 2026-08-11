using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SrdExtract.Pdf;

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
    private const double MinimumHeadingHeight = 7.6;
    private const double MaximumHeadingHeight = 9.2;

    private static readonly string[] Labels = ["Casting Time:", "Range:", "Components:", "Duration:"];

    public static SpellParseResult Parse(IReadOnlyList<SourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var spells = new List<SpellDefinition>();
        var diagnostics = new List<ParseDiagnostic>();

        SpellBuilder? current = null;

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
                current = new SpellBuilder(line.Text.Trim(), line.Page);
                continue;
            }

            current?.Accept(line);
        }

        Flush();

        return new SpellParseResult(spells, diagnostics);
    }

    private static bool IsHeading(SourceLine line) =>
        line.Font == HeadingFont
        && line.Height >= MinimumHeadingHeight
        && line.Height <= MaximumHeadingHeight
        && line.Text.Length > 0;

    private static bool NextLineIsSpellType(IReadOnlyList<SourceLine> lines, int index) =>
        index + 1 < lines.Count && SpellTypePattern().IsMatch(lines[index + 1].Text.Trim());

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
            var classified = EntryMechanicsParser.ClassifyTrait(Name, body);
            var isSpellAttack = SpellAttackPattern().IsMatch(body);
            var save = SpellEffectParser.ParseSave(body, classified.AppliedConditions);

            var mechanics = save is not null
                ? EntryMechanics.SavingThrow
                : isSpellAttack
                    ? EntryMechanics.Attack
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
                Damage = SpellEffectParser.ParseDamage(body),
                AppliedConditions = classified.AppliedConditions,
                IsSpellAttack = isSpellAttack,
                UnmodelledClauses = mechanics == EntryMechanics.Unmodelled
                    ? classified.UnmodelledClauses
                    : [],
                ScalingText = _scaling.Length > 0 ? _scaling.ToString().Trim() : null,
                SourcePage = page,
            };

            reason = string.Empty;
            return true;
        }

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

    [GeneratedRegex(@"M\s*\((?<material>[^)]+)\)")]
    private static partial Regex MaterialPattern();

    [GeneratedRegex(@"^(?<feet>\d+)\s*(?:feet|foot|ft\.?)")]
    private static partial Regex RangeFeetPattern();

    [GeneratedRegex(@"^(?<miles>\d+)\s*miles?")]
    private static partial Regex MilesPattern();

    [GeneratedRegex(@"^(?:Using a Higher-Level Spell Slot|Cantrip Upgrade)\b")]
    private static partial Regex ScalingHeading();

    [GeneratedRegex(@"\b(?:melee|ranged)\s+spell\s+attack\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpellAttackPattern();
}
