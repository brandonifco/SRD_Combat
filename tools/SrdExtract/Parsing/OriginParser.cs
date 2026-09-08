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
/// <para>
/// Three species pages also carry a full-width sub-table — Draconic Ancestors, Elven
/// Lineages, Fiendish Legacies (#381) — so <see cref="Parse"/> reads the species pages
/// twice, the same shape <c>ClassParser</c> uses for its Features table: once as the
/// ordinary two columns, once as the full page width. Which pass captures which table
/// is not uniform, and is written down where each is read — see
/// <see cref="SpeciesBuilder"/>'s remarks for Draconic Ancestors and
/// <see cref="ParseFullWidthTables"/> for the other two.
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

    /// <summary>
    /// Owning species for the two full-width tables that genuinely span the page
    /// (<see cref="ParseFullWidthTables"/>) — read from the printed cross-reference
    /// each trait makes to its own table by name ("Choose a lineage from the Elven
    /// Lineages table."). Draconic Ancestors is not here: unlike these two, it sits
    /// entirely inside the left text column (see <see cref="SpeciesBuilder"/>'s
    /// remarks), so it is captured from the ordinary two-column pass instead.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> FullWidthTableOwner =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Elven Lineages"] = "Elf",
            ["Fiendish Legacies"] = "Tiefling",
        };

    public static OriginParseResult Parse(IReadOnlyList<SourceLine> lines, IReadOnlyList<SourceLine> fullWidthLines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(fullWidthLines);

        var diagnostics = new List<ParseDiagnostic>();
        var fullWidthTables = ParseFullWidthTables(fullWidthLines, diagnostics);

        return new OriginParseResult(
            ParseSpecies(lines, diagnostics, fullWidthTables),
            ParseBackgrounds(lines, diagnostics),
            diagnostics);
    }

    private static List<SpeciesDefinition> ParseSpecies(
        IReadOnlyList<SourceLine> lines,
        List<ParseDiagnostic> diagnostics,
        IReadOnlyDictionary<string, OriginTable> fullWidthTables)
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
                if (fullWidthTables.TryGetValue(built.Name, out var table))
                {
                    built = built with { Tables = [.. built.Tables, table] };
                }

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

    /// <summary>
    /// Reads the Elven Lineages and Fiendish Legacies tables from the full-width pass
    /// — the same "read the page twice" split <c>ClassParser</c> uses for its Features
    /// table, because both genuinely span the full page width. The two-column pass
    /// would slice each row at the column boundary: the Lineage/Legacy name and the
    /// Level 1 cell land in the left-column stream, but Level 3 and Level 5 land in
    /// the right-column stream — arriving, in the overall line order, near whatever
    /// species is being built on that side of the page rather than the one the table
    /// belongs to (#374's cross-column leak). Reading full width keeps every row
    /// whole, at the cost of the ordinary body prose around it interleaving — which is
    /// fine here, since nothing outside the table's own lines is read.
    /// </summary>
    private static Dictionary<string, OriginTable> ParseFullWidthTables(
        IReadOnlyList<SourceLine> lines,
        List<ParseDiagnostic> diagnostics)
    {
        var tables = new Dictionary<string, OriginTable>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (line.Font != HeadingFont || !FullWidthTableOwner.TryGetValue(line.Text.Trim(), out var owner))
            {
                continue;
            }

            var table = ReadFullWidthTable(lines, ref index);

            if (table is null)
            {
                diagnostics.Add(new ParseDiagnostic(owner, $"the {line.Text.Trim()} table could not be read."));
                continue;
            }

            tables[owner] = table;
        }

        return tables;
    }

    /// <summary>
    /// Reads one table starting at its heading line. Advances <paramref name="index"/>
    /// to the last line consumed, so the caller's loop resumes just after the table.
    /// </summary>
    private static OriginTable? ReadFullWidthTable(IReadOnlyList<SourceLine> lines, ref int index)
    {
        var name = lines[index].Text.Trim();
        var headerIndex = index + 1;

        if (headerIndex >= lines.Count)
        {
            return null;
        }

        var columns = ReadTableHeader(lines[headerIndex]);

        if (columns.Count < 2)
        {
            return null;
        }

        var rows = new List<StringBuilder[]>();
        var cursor = headerIndex + 1;

        // Every line belonging to the table — its own header row's typeface included —
        // is set in the GillSans family (SemiBold for headers, plain for a row's own
        // text, italic for the Level 3/5 spell names). The first line outside that
        // family is the chapter's ordinary Cambria prose resuming once the table ends.
        while (cursor < lines.Count && lines[cursor].Font.StartsWith("GillSans", StringComparison.Ordinal))
        {
            var byColumn = SplitByColumn(lines[cursor], columns);

            // A new row always names its lineage/legacy in the first column; a wrapped
            // continuation of the Level 1 cell never reaches that far left.
            if (byColumn.ContainsKey(0))
            {
                var cells = new StringBuilder[columns.Count];

                for (var column = 0; column < cells.Length; column++)
                {
                    cells[column] = new StringBuilder();
                }

                foreach (var (column, text) in byColumn)
                {
                    AppendWrapped(cells[column], text);
                }

                rows.Add(cells);
            }
            else if (rows.Count > 0)
            {
                foreach (var (column, text) in byColumn)
                {
                    AppendWrapped(rows[^1][column], text);
                }
            }

            cursor++;
        }

        index = cursor - 1;

        return new OriginTable(
            name,
            columns.Select(column => column.Name).ToArray(),
            rows.Select(row => (IReadOnlyList<string>)row.Select(cell => cell.ToString().Trim()).ToArray()).ToArray());
    }

    /// <summary>
    /// Splits the header row into columns. A two-word header like "Level 1" is printed
    /// side by side rather than stacked, so adjacent words merge into one column
    /// whenever their gap is inside a single column's own word spacing — the same
    /// 12pt-or-more-between-columns, 2-5pt-within-a-column rule <c>ClassParser</c>
    /// documents for the Classes chapter's tables.
    /// </summary>
    private static IReadOnlyList<TableColumn> ReadTableHeader(SourceLine header)
    {
        var columns = new List<TableColumn>();
        var pending = new List<SourceWord>();

        void Commit()
        {
            if (pending.Count == 0)
            {
                return;
            }

            columns.Add(new TableColumn(
                string.Join(' ', pending.Select(word => word.Text)),
                pending[0].Left));
            pending.Clear();
        }

        foreach (var word in header.Words)
        {
            if (pending.Count > 0 && word.Left - pending[^1].Right >= HeaderColumnGapPoints)
            {
                Commit();
            }

            pending.Add(word);
        }

        Commit();
        return columns;
    }

    /// <summary>
    /// Buckets a line's words by column, keyed by column index, joining each column's
    /// words with a single space. A word belongs to the last column whose left edge it
    /// has reached, with the same small tolerance <c>ClassParser</c> uses for the
    /// Classes chapter's tables.
    /// </summary>
    private static Dictionary<int, string> SplitByColumn(SourceLine line, IReadOnlyList<TableColumn> columns)
    {
        var byColumn = new Dictionary<int, List<string>>();

        foreach (var word in line.Words)
        {
            var column = 0;

            for (var candidate = 1; candidate < columns.Count; candidate++)
            {
                if (word.Left >= columns[candidate].Left - ColumnAssignmentTolerance)
                {
                    column = candidate;
                }
            }

            if (!byColumn.TryGetValue(column, out var words))
            {
                words = [];
                byColumn[column] = words;
            }

            words.Add(word.Text);
        }

        return byColumn.ToDictionary(pair => pair.Key, pair => string.Join(' ', pair.Value));
    }

    private const double HeaderColumnGapPoints = 10.0;
    private const double ColumnAssignmentTolerance = 8.0;

    private sealed record TableColumn(string Name, double Left);

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
    /// <remarks>
    /// Draconic Ancestors is captured here, from the ordinary two-column pass, rather
    /// than alongside Elven Lineages and Fiendish Legacies in
    /// <see cref="ParseFullWidthTables"/>. Its printed table is only two columns wide,
    /// duplicated side by side to fill the page — every word of it sits left of x≈285,
    /// entirely inside where the two-column split already puts the left column (the
    /// boundary is x=300 — see <c>PageTextReader.ColumnBoundary</c>) — so the
    /// two-column pass hands it over already clean, on its own lines, arriving right
    /// after the Draconic Ancestry trait while Dragonborn is still the open species.
    /// Reading it from the full-width pass instead would work too, but would then have
    /// to filter out the *other* column's prose landing on the same baseline (verified
    /// against the PDF: "Draconic Ancestors ... Stonecunning. As a Bonus Action ..." —
    /// the Dwarf's own trait, sharing the row purely by page position) — a problem the
    /// two-column split has already solved for free.
    /// </remarks>
    private sealed class SpeciesBuilder(string name, int page)
    {
        private const string DraconicAncestorsHeading = "Draconic Ancestors";

        private readonly List<(string Name, StringBuilder Text)> _traits = [];
        private readonly List<(string Dragon, string DamageType)> _draconicAncestors = [];
        private CreatureType? _creatureType;
        private IReadOnlyList<CreatureSize> _sizes = [];
        private int? _speedFeet;
        private bool _readingDraconicAncestors;

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

            if (_readingDraconicAncestors)
            {
                if (TryAcceptDraconicAncestorsLine(line))
                {
                    return;
                }

                // The table's last row was the line before this one; fall through and
                // let this line — the next trait heading — be read normally below.
                _readingDraconicAncestors = false;
            }

            if (text == DraconicAncestorsHeading && line.Font == HeadingFont)
            {
                _readingDraconicAncestors = true;
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

            // Three species (Dragonborn, Elf, Tiefling) carry a full-width sub-table —
            // Draconic Ancestors, Elven Lineages, Fiendish Legacies — that spans both
            // text columns. Draconic Ancestors was already read above, from its own
            // heading, and never reaches here. Elven Lineages and Fiendish Legacies
            // still arrive as two-column fragments at this point — their Lineage/Legacy
            // name and Level 1 text sit left of the column boundary, same as Draconic
            // Ancestors, but their Level 3 and Level 5 cells cross it, landing on the
            // *other* side (#381's ParseFullWidthTables reads the whole row from the
            // full-width pass instead, so nothing is lost). Left unfiltered, the
            // fragment left behind here would interleave with whichever trait was open
            // when it arrived — the same #116 shape ClassParser's Features table
            // produces, and just as capable of reaching the *next* species: the Elven
            // Lineages fragment's wide first column crosses into the right column and
            // lands mid-sentence in Gnome's Gnomish Lineage, and the Fiendish Legacies
            // fragment lands in Human's Versatile — five affected traits in total
            // (#374). Trait prose in this chapter is Cambria (the wrapping and italic
            // variants included, which is why the family is matched); everything
            // GillSans here is a table fragment's, so only Cambria lines may continue a
            // trait — the fragment is absent from the trait text and honest, exactly as
            // a class feature's printed sub-table is.
            if (_traits.Count > 0 && text.Length > 0 && line.Font.StartsWith("Cambria", StringComparison.Ordinal))
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

            var tables = new List<OriginTable>();

            if (_draconicAncestors.Count > 0)
            {
                tables.Add(new OriginTable(
                    DraconicAncestorsHeading,
                    ["Dragon", "Damage Type"],
                    _draconicAncestors
                        .Select(row => (IReadOnlyList<string>)new[] { row.Dragon, row.DamageType })
                        .ToArray()));
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
                Tables = tables,
                SourcePage = page,
            };

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Reads one line of the Draconic Ancestors table, or reports it does not
        /// belong to the table so the caller can close it and re-read the line
        /// normally. The header row ("Dragon Damage Type Dragon Damage Type") carries
        /// no data — its shape is fixed and known — so it is consumed and ignored;
        /// each data row prints two (Dragon, Damage Type) pairs side by side, always
        /// as four single words in that left-to-right order.
        /// </summary>
        private bool TryAcceptDraconicAncestorsLine(SourceLine line)
        {
            if (line.Font == HeadingFont)
            {
                return true;
            }

            if (line.Font == ContinuationFont && line.Words.Count == 4)
            {
                _draconicAncestors.Add((line.Words[0].Text, line.Words[1].Text));
                _draconicAncestors.Add((line.Words[2].Text, line.Words[3].Text));
                return true;
            }

            return false;
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
