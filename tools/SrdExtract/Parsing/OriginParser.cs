using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SrdExtract.Pdf;

namespace SrdExtract.Parsing;

/// <summary>The result of parsing the Character Origins section.</summary>
public sealed record OriginParseResult(
    IReadOnlyList<SpeciesDefinition> Species,
    IReadOnlyList<BackgroundDefinition> Backgrounds,
    IReadOnlyList<ParseDiagnostic> Diagnostics);

/// <summary>
/// Parses the SRD's Character Origins section — backgrounds and species.
/// </summary>
/// <remarks>
/// <para>
/// Both are found by structure rather than by a list of expected names, so a species
/// the SRD adds is picked up rather than silently missed. A species is a heading
/// immediately followed by a <c>Creature Type:</c> line; a background is a heading
/// immediately followed by an <c>Ability Scores:</c> line. Nothing else on these pages
/// has either shape, and neither detector can fire on a section header.
/// </para>
/// <para>
/// Values wrap onto continuation lines set in a lighter weight, so each labelled value
/// is buffered until the next label rather than read one line at a time — the same
/// approach the stat block parser uses for its Senses line.
/// </para>
/// </remarks>
public static partial class OriginParser
{
    private const string HeadingFont = "GillSans-SemiBold";
    private const string ContinuationFont = "GillSans";
    /// <summary>
    /// Trait names are set bold-italic, but the typeface differs by chapter: the
    /// bestiary uses Optima and the player-facing chapters use Cambria. Matching the
    /// style suffix rather than a full font name keeps this working across both.
    /// </summary>
    private const string TraitNameFontSuffix = "BoldItalic";

    /// <summary>Species and background headings sit at roughly 8.3pt.</summary>
    private const double MinimumHeadingHeight = 7.8;
    private const double MaximumHeadingHeight = 9.0;

    public static OriginParseResult Parse(IReadOnlyList<SourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var diagnostics = new List<ParseDiagnostic>();

        return new OriginParseResult(
            ParseSpecies(lines, diagnostics),
            ParseBackgrounds(lines, diagnostics),
            diagnostics);
    }

    private static List<SpeciesDefinition> ParseSpecies(
        IReadOnlyList<SourceLine> lines,
        List<ParseDiagnostic> diagnostics)
    {
        var species = new List<SpeciesDefinition>();
        SpeciesBuilder? current = null;

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            if (current.TryBuild(out var built, out var reason))
            {
                species.Add(built);
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

            if (IsHeading(line) && FollowedByLabel(lines, index, "Creature Type:"))
            {
                Flush();
                current = new SpeciesBuilder(line.Text.Trim(), line.Page);
                continue;
            }

            // A background heading ends the species that preceded it, and vice versa.
            if (IsHeading(line) && FollowedByLabel(lines, index, "Ability Scores:"))
            {
                Flush();
                continue;
            }

            current?.Accept(line);
        }

        Flush();
        return species;
    }

    private static List<BackgroundDefinition> ParseBackgrounds(
        IReadOnlyList<SourceLine> lines,
        List<ParseDiagnostic> diagnostics)
    {
        var backgrounds = new List<BackgroundDefinition>();
        BackgroundBuilder? current = null;

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            if (current.TryBuild(out var built, out var reason))
            {
                backgrounds.Add(built);
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

            if (IsHeading(line) && FollowedByLabel(lines, index, "Ability Scores:"))
            {
                Flush();
                current = new BackgroundBuilder(line.Text.Trim(), line.Page);
                continue;
            }

            if (IsHeading(line) && FollowedByLabel(lines, index, "Creature Type:"))
            {
                Flush();
                continue;
            }

            current?.Accept(line);
        }

        Flush();
        return backgrounds;
    }

    private static bool IsHeading(SourceLine line) =>
        line.Font == HeadingFont
        && line.Height >= MinimumHeadingHeight
        && line.Height <= MaximumHeadingHeight
        && line.Text.Length > 0;

    /// <summary>Whether the next non-empty line begins with the given label.</summary>
    private static bool FollowedByLabel(IReadOnlyList<SourceLine> lines, int index, string label)
    {
        for (var next = index + 1; next < lines.Count && next <= index + 2; next++)
        {
            if (lines[next].Text.StartsWith(label, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Turns a printed name into a stable slug.</summary>
    internal static string MakeId(string prefix, string name)
    {
        var slug = new StringBuilder(prefix).Append('.');
        var lastWasDash = true;

        foreach (var character in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character))
            {
                slug.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                slug.Append('-');
                lastWasDash = true;
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    /// <summary>Accumulates one species as its lines arrive.</summary>
    private sealed class SpeciesBuilder(string name, int page)
    {
        private readonly List<(string Name, StringBuilder Text)> _traits = [];
        private CreatureType? _creatureType;
        private IReadOnlyList<CreatureSize> _sizes = [];
        private int? _speedFeet;

        public string Name { get; } = name;

        public void Accept(SourceLine line)
        {
            var text = line.Text.Trim();

            if (TryTake(text, "Creature Type:", out var creatureType))
            {
                if (Enum.TryParse<CreatureType>(creatureType, ignoreCase: true, out var parsed))
                {
                    _creatureType = parsed;
                }

                return;
            }

            if (TryTake(text, "Size:", out var size))
            {
                _sizes = ParseSizes(size);
                return;
            }

            if (TryTake(text, "Speed:", out var speed) && SpeedPattern().Match(speed) is { Success: true } feet)
            {
                _speedFeet = int.Parse(feet.Groups["feet"].Value, CultureInfo.InvariantCulture);
                return;
            }

            // A trait opens with its name in bold italic, exactly as a stat block entry does.
            if (line.Font.EndsWith(TraitNameFontSuffix, StringComparison.Ordinal))
            {
                var heading = line.LeadingRunInFont("BoldItalic").TrimEnd('.', ' ');
                var body = text;

                if (heading.Length > 0 && body.StartsWith(heading, StringComparison.Ordinal))
                {
                    body = body[heading.Length..].TrimStart('.', ' ');
                }

                _traits.Add((heading.Length > 0 ? heading : text.TrimEnd('.'), new StringBuilder(body)));
                return;
            }

            if (_traits.Count > 0 && text.Length > 0)
            {
                AppendWrapped(_traits[^1].Text, text);
            }
        }

        public bool TryBuild(out SpeciesDefinition species, out string reason)
        {
            species = null!;

            if (_creatureType is null || _sizes.Count == 0 || _speedFeet is null)
            {
                reason = "missing Creature Type, Size or Speed — probably not a species heading.";
                return false;
            }

            species = new SpeciesDefinition
            {
                Id = MakeId("species", Name),
                Name = Name,
                CreatureType = _creatureType.Value,
                Sizes = _sizes,
                SpeedFeet = _speedFeet.Value,
                Traits = _traits
                    .Select(trait => EntryMechanicsParser.ClassifyTrait(trait.Name, trait.Text.ToString().Trim()))
                    .ToArray(),
                SourcePage = page,
            };

            reason = string.Empty;
            return true;
        }

        /// <summary>Reads "Medium (about 4-5 feet tall)" and "Small or Medium".</summary>
        private static IReadOnlyList<CreatureSize> ParseSizes(string text) =>
            SizePattern()
                .Matches(text)
                .Select(match => Enum.Parse<CreatureSize>(match.Value, ignoreCase: true))
                .Distinct()
                .ToArray();
    }

    /// <summary>Accumulates one background as its lines arrive.</summary>
    private sealed class BackgroundBuilder(string name, int page)
    {
        private static readonly string[] Labels =
        [
            "Ability Scores:",
            "Feat:",
            "Skill Proficiencies:",
            "Tool Proficiency:",
            "Equipment:",
        ];

        private readonly Dictionary<string, StringBuilder> _values = new(StringComparer.Ordinal);
        private string? _currentLabel;

        public string Name { get; } = name;

        public void Accept(SourceLine line)
        {
            var text = line.Text.Trim();

            foreach (var label in Labels)
            {
                if (!text.StartsWith(label, StringComparison.Ordinal))
                {
                    continue;
                }

                _currentLabel = label;
                _values[label] = new StringBuilder(text[label.Length..].Trim());
                return;
            }

            // Continuation of the value above, set in the lighter weight.
            if (_currentLabel is not null && line.Font == ContinuationFont && text.Length > 0)
            {
                AppendWrapped(_values[_currentLabel], text);
            }
        }

        public bool TryBuild(out BackgroundDefinition background, out string reason)
        {
            background = null!;

            if (!_values.TryGetValue("Ability Scores:", out var abilityText))
            {
                reason = "no Ability Scores line — probably not a background heading.";
                return false;
            }

            var abilities = AbilityPattern()
                .Matches(abilityText.ToString())
                .Select(match => Enum.Parse<Ability>(match.Value, ignoreCase: true))
                .Distinct()
                .ToArray();

            if (abilities.Length != 3)
            {
                reason = $"expected 3 ability scores, found {abilities.Length}.";
                return false;
            }

            background = new BackgroundDefinition
            {
                Id = MakeId("background", Name),
                Name = Name,
                AbilityScores = abilities,
                FeatName = CleanFeat(Value("Feat:")),
                SkillProficiencies = SplitSkills(Value("Skill Proficiencies:")),
                ToolProficiency = CleanReference(Value("Tool Proficiency:")),
                Equipment = CleanReference(Value("Equipment:")),
                SourcePage = page,
            };

            reason = string.Empty;
            return true;
        }

        private string Value(string label) =>
            _values.TryGetValue(label, out var value) ? value.ToString().Trim() : string.Empty;

        /// <summary>Strips the "(see "Feats")" cross-reference the SRD appends.</summary>
        private static string CleanFeat(string text) => CrossReference().Replace(text, string.Empty).Trim();

        private static string CleanReference(string text) =>
            CrossReference().Replace(text, string.Empty).Replace("  ", " ").Trim();

        /// <summary>"Arcana and History" and "Athletics and Intimidation".</summary>
        private static IReadOnlyList<string> SplitSkills(string text) => text
            .Split([" and ", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(skill => skill.Length > 0)
            .ToArray();
    }

    private static bool TryTake(string text, string label, out string remainder)
    {
        if (text.StartsWith(label, StringComparison.Ordinal))
        {
            remainder = text[label.Length..].Trim();
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    /// <summary>
    /// Joins a wrapped line, undoing the hyphenation the SRD's justified columns
    /// introduce — the same rule the stat block parser uses.
    /// </summary>
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

    [GeneratedRegex(@"(?<feet>\d+)\s*(?:feet|ft\.?)")]
    private static partial Regex SpeedPattern();

    [GeneratedRegex(@"\b(Tiny|Small|Medium|Large|Huge|Gargantuan)\b")]
    private static partial Regex SizePattern();

    [GeneratedRegex(@"\b(Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\b")]
    private static partial Regex AbilityPattern();

    [GeneratedRegex(@"\s*\(see [^)]*\)")]
    private static partial Regex CrossReference();
}
