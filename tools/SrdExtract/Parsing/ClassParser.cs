using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SrdExtract.Pdf;

namespace SrdExtract.Parsing;

/// <summary>The result of parsing the Classes chapter.</summary>
public sealed record ClassParseResult(
    IReadOnlyList<ClassDefinition> Classes,
    IReadOnlyList<ParseDiagnostic> Diagnostics);

/// <summary>
/// Parses the SRD's Classes chapter.
/// </summary>
/// <remarks>
/// <para>
/// A class page mixes two layouts, which is why this reads each page twice. The Core
/// Traits table and the feature prose sit in the ordinary two text columns; the Features
/// table spans the full page width at the bottom. Reading the whole page one way gets
/// one of them wrong — two-column reading slices the wide table into fragments, and
/// full-width reading interleaves the body prose.
/// </para>
/// <para>
/// All twelve SRD classes are extracted, not just the six the game launches with.
/// The extractor has no reason to know which the game uses, and filtering here would
/// mean editing the extractor every time that decision changes.
/// </para>
/// </remarks>
public static partial class ClassParser
{
    /// <summary>
    /// Words closer than this on a Features table header belong to the same column.
    /// Within-column spacing is 2-5pt and between-column spacing is 12pt or more, so the
    /// exact value is not delicate.
    /// </summary>
    private const double WithinColumnGapPoints = 8.0;
    private const string HeadingFont = "GillSans-SemiBold";
    private const string TraitTableFontFamily = "GillSans";

    /// <summary>Feature headings ("Level 1: Rage") are set at roughly 8.2pt.</summary>
    private const double MinimumFeatureHeadingHeight = 7.8;
    private const double MaximumFeatureHeadingHeight = 9.0;

    public static ClassParseResult Parse(
        IReadOnlyList<SourceLine> columnLines,
        IReadOnlyList<SourceLine> fullWidthLines)
    {
        ArgumentNullException.ThrowIfNull(columnLines);
        ArgumentNullException.ThrowIfNull(fullWidthLines);

        var diagnostics = new List<ParseDiagnostic>();
        var classes = new List<ClassDefinition>();

        var unreadableRows = new List<string>();
        var tablesByClass = ParseFeatureTables(fullWidthLines, unreadableRows);

        ClassBuilder? current = null;

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            tablesByClass.TryGetValue(current.Name, out var levels);

            if (current.TryBuild(levels ?? [], out var built, out var reason))
            {
                classes.Add(built);
            }
            else
            {
                diagnostics.Add(new ParseDiagnostic(current.Name, reason));
            }

            current = null;
        }

        foreach (var line in columnLines)
        {
            if (CoreTraitsHeading().Match(line.Text) is { Success: true } heading
                && line.Font == HeadingFont)
            {
                Flush();
                current = new ClassBuilder(heading.Groups["name"].Value.Trim(), line.Page);
                continue;
            }

            current?.Accept(line);
        }

        Flush();

        diagnostics.AddRange(unreadableRows
            .GroupBy(row => row.Split(':')[0], StringComparer.Ordinal)
            .Select(group => new ParseDiagnostic(
                group.Key,
                $"{group.Count()} level table row(s) could not be resolved into columns.")));

        foreach (var name in tablesByClass.Keys.Where(name =>
                     !classes.Any(definition => definition.Name == name)))
        {
            diagnostics.Add(new ParseDiagnostic(name, "a Features table was found with no matching class."));
        }

        return new ClassParseResult(classes, diagnostics);
    }

    /// <summary>
    /// Reads every "X Features" table from the full-width pass, keyed by class name.
    /// </summary>
    private static Dictionary<string, List<ClassLevel>> ParseFeatureTables(
        IReadOnlyList<SourceLine> lines,
        List<string> unreadableRows)
    {
        var tables = new Dictionary<string, List<ClassLevel>>(StringComparer.Ordinal);

        string? currentClass = null;
        IReadOnlyList<TableColumn> columns = [];

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (FeaturesHeading().Match(line.Text) is { Success: true } heading && line.Font == HeadingFont)
            {
                currentClass = heading.Groups["name"].Value.Trim();
                columns = ReadColumns(lines, index);
                tables[currentClass] = [];
                continue;
            }

            if (currentClass is null || columns.Count == 0)
            {
                continue;
            }

            if (LooksLikeLevelRow(line) && ReadLevelRow(line, columns) is not { } row)
            {
                unreadableRows.Add($"{currentClass}: could not resolve columns for '{line.Text}'.");
                continue;
            }
            else if (ReadLevelRow(line, columns) is { } resolved)
            {
                tables[currentClass].Add(resolved);
            }
        }

        return tables;
    }

    /// <summary>
    /// Works out the table's columns from its two header lines.
    /// </summary>
    /// <remarks>
    /// The header is stacked: "Proficiency" sits above "Bonus", "Rage" above "Damage".
    /// The lower line supplies the column positions, and a word on the upper line
    /// starting at roughly the same x supplies the first half of the name. Adjacent
    /// words on the lower line are merged into one column only when both are alphabetic
    /// — that keeps "Class Features" together while leaving a caster's single-digit
    /// spell-slot headers as nine separate columns.
    /// </remarks>
    private static IReadOnlyList<TableColumn> ReadColumns(IReadOnlyList<SourceLine> lines, int headingIndex)
    {
        for (var index = headingIndex + 1; index < lines.Count && index <= headingIndex + 4; index++)
        {
            var lower = lines[index];

            if (!lower.Text.StartsWith("Level", StringComparison.Ordinal))
            {
                continue;
            }

            var upper = index > headingIndex + 1 ? lines[index - 1] : null;
            var columns = new List<TableColumn>();

            var pending = new List<SourceWord>();

            void Commit()
            {
                if (pending.Count == 0)
                {
                    return;
                }

                var left = pending[0].Left;
                var name = string.Join(' ', pending.Select(word => word.Text));

                // Prepend the stacked word above, when there is one at the same x.
                var right = pending[^1].Right;

                // A single-digit column is a spell-slot level under the table's
                // "—Spell Slots per Spell Level—" spanner. Nothing above it should be
                // prepended: on the Bard's table the spanner itself sits directly above
                // the digits and turned them into columns called "- 1" and "-Spell 3".
                var isSpellSlotColumn = pending.Count == 1
                    && pending[0].Text.Length == 1
                    && char.IsAsciiDigit(pending[0].Text[0]);

                if (!isSpellSlotColumn)
                {
                    // Matched on centre distance rather than any overlap: the Cleric's
                    // "Channel" is wide enough to overlap the neighbouring "Cantrips"
                    // column, which produced a column called "Channel Cantrips".
                    var centre = (left + right) / 2;

                    var above = upper?.Words
                        .Where(word => Math.Abs(((word.Left + word.Right) / 2) - centre) < 22)
                        .OrderBy(word => Math.Abs(((word.Left + word.Right) / 2) - centre))
                        .FirstOrDefault();

                    if (above is not null)
                    {
                        name = $"{above.Text} {name}";
                    }
                }

                columns.Add(new TableColumn(name, left));
                pending.Clear();
            }

            foreach (var word in lower.Words)
            {
                var previous = pending.Count > 0 ? pending[^1] : null;

                // Most two-word column names are *stacked* — "Proficiency" over "Bonus" —
                // and get reassembled from the line above. Some are printed side by side
                // on this line instead: "Class Features" everywhere, and "Sneak Attack"
                // on the Rogue's table, which has no stacked row at all.
                //
                // The two cases are told apart by gap size, and the margin is wide: words
                // within one column sit 2-5pt apart, while separate columns are never
                // closer than 12pt. An earlier 20pt threshold was too generous and merged
                // the Cleric's "Level" and "Bonus", whose gap is 13pt.
                var isDigit = word.Text.Length == 1 && char.IsAsciiDigit(word.Text[0]);

                var continuesColumn = previous is not null
                    && !isDigit
                    && previous.Text.All(char.IsLetter)
                    && word.Text.All(char.IsLetter)
                    && word.Left - previous.Right < WithinColumnGapPoints;

                if (!continuesColumn)
                {
                    Commit();
                }

                pending.Add(word);
            }

            Commit();
            return columns;
        }

        return [];
    }

    /// <summary>Reads one data row, or null when the line is not one.</summary>
    private static ClassLevel? ReadLevelRow(SourceLine line, IReadOnlyList<TableColumn> columns)
    {
        if (line.Words.Count < 2)
        {
            return null;
        }

        // A data row opens with a level number and a proficiency bonus. Body prose does
        // not, which is what keeps the full-width pass from picking up ordinary text.
        if (!int.TryParse(line.Words[0].Text, CultureInfo.InvariantCulture, out var level)
            || level is < 1 or > 20
            || !SignedNumber().IsMatch(line.Words[1].Text))
        {
            return null;
        }

        var cells = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var word in line.Words)
        {
            var column = columns.LastOrDefault(candidate => word.Left >= candidate.Left - 8) ?? columns[0];

            if (!cells.TryGetValue(column.Name, out var contents))
            {
                contents = [];
                cells[column.Name] = contents;
            }

            contents.Add(word.Text);
        }

        var spellSlots = new Dictionary<int, int>();
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        var features = new List<string>();
        var proficiencyBonus = 0;

        foreach (var (name, words) in cells)
        {
            var value = string.Join(' ', words).Trim();

            if (name.EndsWith("Level", StringComparison.Ordinal) && value == level.ToString(CultureInfo.InvariantCulture))
            {
                continue;
            }

            if (name.Contains("Bonus", StringComparison.Ordinal))
            {
                // A row whose columns did not resolve dumps several cells' worth of text
                // here. Refusing the row is right: a half-read level table is worse than
                // a reported gap, and the caller turns this into a diagnostic.
                if (!int.TryParse(value.TrimStart('+'), CultureInfo.InvariantCulture, out proficiencyBonus))
                {
                    return null;
                }

                continue;
            }

            if (name.Contains("Features", StringComparison.Ordinal))
            {
                features.AddRange(SplitFeatures(value));
                continue;
            }

            // A single-digit header is a spell-slot level, under the table's
            // "Spell Slots per Spell Level" spanner.
            if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            {
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var slots) && slots > 0)
                {
                    spellSlots[name[0] - '0'] = slots;
                }

                continue;
            }

            if (value is not ("-" or "—" or ""))
            {
                resources[name] = value;
            }
        }

        return new ClassLevel(level, proficiencyBonus, features, spellSlots, resources);
    }

    /// <summary>Splits "Rage, Unarmored Defense, Weapon Mastery" into its features.</summary>
    private static IEnumerable<string> SplitFeatures(string value)
    {
        if (value is "-" or "—" or "")
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(feature => feature.Length > 0 && feature is not ("-" or "—"));
    }

    /// <summary>Whether a line opens like a level table row, regardless of whether it parsed.</summary>
    private static bool LooksLikeLevelRow(SourceLine line) =>
        line.Words.Count >= 2
        && int.TryParse(line.Words[0].Text, CultureInfo.InvariantCulture, out var level)
        && level is >= 1 and <= 20
        && SignedNumber().IsMatch(line.Words[1].Text);

    /// <summary>
    /// Splits a Core Traits line into its key and its value by matching the leading
    /// words against the known keys.
    /// </summary>
    /// <remarks>
    /// A gap heuristic was tried first and does not work. "Weapon Proficiencies" is wide
    /// enough to overflow its column, so its value begins after an ordinary word gap
    /// rather than a wide one — the split was missed, and the whole Weapon Proficiencies
    /// row was swallowed into the end of the skill list above it. Matching the key
    /// itself has no such failure mode: the set is small, closed and printed verbatim.
    /// </remarks>
    private static (string? Key, List<SourceWord> Value) SplitKeyFromValue(SourceLine line)
    {
        var maximum = Math.Min(3, line.Words.Count);

        for (var length = maximum; length >= 1; length--)
        {
            var candidate = string.Join(' ', line.Words.Take(length).Select(word => word.Text));

            if (CoreTraitKeys.Contains(candidate))
            {
                return (candidate, [.. line.Words.Skip(length)]);
            }
        }

        return (null, [.. line.Words]);
    }

    private sealed record TableColumn(string Name, double Left);

    /// <summary>Accumulates one class's Core Traits table and feature prose.</summary>
    private sealed class ClassBuilder(string name, int page)
    {
        private readonly Dictionary<string, StringBuilder> _traits = new(StringComparer.Ordinal);
        private readonly List<(string Name, int Level, StringBuilder Text)> _features = [];
        private string? _currentTrait;
        private double? _valueColumnLeft;

        public string Name { get; } = name;

        public void Accept(SourceLine line)
        {
            var text = line.Text.Trim();

            // A feature heading closes the Core Traits table and opens the prose.
            if (line.Font == HeadingFont
                && line.Height >= MinimumFeatureHeadingHeight
                && line.Height <= MaximumFeatureHeadingHeight
                && FeatureHeading().Match(text) is { Success: true } feature)
            {
                _currentTrait = null;
                _features.Add((
                    feature.Groups["name"].Value.Trim(),
                    int.Parse(feature.Groups["level"].Value, CultureInfo.InvariantCulture),
                    new StringBuilder()));
                return;
            }

            if (_features.Count > 0)
            {
                if (text.Length > 0)
                {
                    AppendWrapped(_features[^1].Text, text);
                }

                return;
            }

            // Keys are set semi-bold and wrapped values in the lighter weight, so the
            // family is matched rather than the exact face. Matching only the semi-bold
            // face silently dropped every continuation line, truncating the Barbarian's
            // six-skill list to one.
            if (!line.Font.StartsWith(TraitTableFontFamily, StringComparison.Ordinal))
            {
                return;
            }

            // Inside the Core Traits table.
            var (key, valueWords) = SplitKeyFromValue(line);

            if (key is not null)
            {
                _currentTrait = key;
                _valueColumnLeft = valueWords.Count > 0 ? valueWords[0].Left : _valueColumnLeft;
                _traits[key] = new StringBuilder(string.Join(' ', valueWords.Select(word => word.Text)).Trim());
                return;
            }

            // No key: either a wrapped value continuing the row above, or something that
            // is not part of the table at all. The value column's own x tells them apart
            // — a wrapped value is indented to it, "Proficiencies" is not, and that
            // sub-heading must not be appended to the row above it.
            var isWrappedValue = _currentTrait is not null
                && valueWords.Count > 0
                && _valueColumnLeft is { } valueLeft
                && valueWords[0].Left >= valueLeft - 5;

            if (isWrappedValue)
            {
                AppendWrapped(_traits[_currentTrait!], string.Join(' ', valueWords.Select(word => word.Text)).Trim());
                return;
            }

            _currentTrait = null;
        }

        public bool TryBuild(IReadOnlyList<ClassLevel> levels, out ClassDefinition definition, out string reason)
        {
            definition = null!;

            var hitDieText = Value("Hit Point Die");
            var hitDie = HitDiePattern().Match(hitDieText);

            if (!hitDie.Success)
            {
                reason = $"could not read a hit die from '{hitDieText}'.";
                return false;
            }

            if (levels.Count == 0)
            {
                reason = "no Features table was found.";
                return false;
            }

            var skillText = Value("Skill Proficiencies");
            var skillChoice = SkillChoicePattern().Match(skillText);

            definition = new ClassDefinition
            {
                Id = OriginParser.MakeId("class", Name),
                Name = Name,
                PrimaryAbilities = ReadAbilities(Value("Primary Ability")),
                HitDieSides = int.Parse(hitDie.Groups["sides"].Value, CultureInfo.InvariantCulture),
                SavingThrowProficiencies = ReadAbilities(Value("Saving Throw")),
                SkillChoiceCount = skillChoice.Success
                    ? int.Parse(skillChoice.Groups["count"].Value, CultureInfo.InvariantCulture)
                    : 0,
                SkillChoices = ChoosesAnySkill(skillText) ? [] : ReadSkillList(skillText),
                ChoosesAnySkill = ChoosesAnySkill(skillText),
                WeaponProficiencies = Value("Weapon Proficiencies"),
                ArmorTraining = Value("Armor Training"),
                StartingEquipment = Value("Starting Equipment"),
                Levels = levels.OrderBy(level => level.Level).ToArray(),
                Features = Classify(_features.Take(SubclassStart(_features))),
                SubclassFeatures = Classify(_features.Skip(SubclassStart(_features))),
                SourcePage = page,
            };

            reason = string.Empty;
            return true;
        }

        private static IReadOnlyList<TraitEntry> Classify(
            IEnumerable<(string Name, int Level, StringBuilder Text)> features) =>
            features
                .Select(feature => EntryMechanicsParser.ClassifyTrait(
                    feature.Name,
                    feature.Text.ToString().Trim()) with { GrantedAtLevel = feature.Level })
                .ToArray();

        /// <summary>
        /// Where the class's own features end and its subclass's begin.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The printed levels are the boundary, and they are unambiguous.</b> A class's
        /// features climb from level 1 to 20 and then the subclass starts over — the
        /// Fighter runs to "Level 20: Three Extra Attacks" and the Champion's first
        /// heading is "Level 3: Improved Critical". A single backwards step in an
        /// otherwise rising sequence is the split, and there is exactly one per class.
        /// </para>
        /// <para>
        /// Derived rather than curated on purpose: the alternative is a hand-written list
        /// of which features belong to which subclass, which would go stale the moment
        /// the source changed. <c>ClassValidator</c> asserts the shape this relies on —
        /// every class has one reset and every feature carries a level.
        /// </para>
        /// </remarks>
        private static int SubclassStart(List<(string Name, int Level, StringBuilder Text)> features)
        {
            for (var index = 1; index < features.Count; index++)
            {
                if (features[index].Level < features[index - 1].Level)
                {
                    return index;
                }
            }

            return features.Count;
        }

        private string Value(string key) => _traits.TryGetValue(key, out var value) ? value.ToString().Trim() : string.Empty;

        /// <summary>The Bard chooses "any 3 skills" rather than from a printed list.</summary>
        private static bool ChoosesAnySkill(string text) =>
            text.Contains("any", StringComparison.OrdinalIgnoreCase);

        private static IReadOnlyList<Ability> ReadAbilities(string text) =>
            AbilityPattern()
                .Matches(text)
                .Select(match => Enum.Parse<Ability>(match.Value, ignoreCase: true))
                .Distinct()
                .ToArray();

        /// <summary>Reads the skill list out of "Choose 2: Animal Handling, Athletics, ... or Survival".</summary>
        private static IReadOnlyList<string> ReadSkillList(string text)
        {
            var colon = text.IndexOf(':');
            var list = colon >= 0 ? text[(colon + 1)..] : text;

            return list
                .Replace(" or ", ", ", StringComparison.Ordinal)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(skill => skill.Length > 0)
                .ToArray();
        }
    }

    private static readonly HashSet<string> CoreTraitKeys = new(StringComparer.Ordinal)
    {
        "Primary Ability",
        "Hit Point Die",
        "Saving Throw",
        "Skill Proficiencies",
        "Weapon Proficiencies",
        "Tool Proficiencies",
        "Armor Training",
        "Starting Equipment",
    };

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

    [GeneratedRegex(@"^Core\s+(?<name>[A-Z][A-Za-z]+)\s+Traits$")]
    private static partial Regex CoreTraitsHeading();

    [GeneratedRegex(@"^(?<name>[A-Z][A-Za-z]+)\s+Features$")]
    private static partial Regex FeaturesHeading();

    [GeneratedRegex(@"^Level\s+(?<level>\d+):\s*(?<name>.+)$")]
    private static partial Regex FeatureHeading();

    [GeneratedRegex(@"^[dD](?<sides>\d+)")]
    private static partial Regex HitDiePattern();

    [GeneratedRegex(@"Choose\s+(?:any\s+)?(?<count>\d+)")]
    private static partial Regex SkillChoicePattern();

    [GeneratedRegex(@"\b(Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\b")]
    private static partial Regex AbilityPattern();

    [GeneratedRegex(@"^[+-]\d+$")]
    private static partial Regex SignedNumber();
}
