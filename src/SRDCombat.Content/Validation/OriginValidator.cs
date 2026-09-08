using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Validation;

/// <summary>
/// Checks extracted species and backgrounds.
/// </summary>
/// <remarks>
/// These have no printed self-checking numbers the way a stat block does — there is no
/// hit-points-versus-hit-dice equivalent — so the checks here are about shape: the
/// things the SRD's own rules text says must be true of every species and every
/// background.
/// </remarks>
public static partial class OriginValidator
{
    /// <summary>The row count and column headers the SRD fixes for one full-width origins table.</summary>
    private sealed record ExpectedTableShape(int Rows, IReadOnlyList<string> Columns);

    /// <summary>
    /// The three full-width tables the origins chapter prints, and the row count and
    /// column headers fixed by the source for each (#381) — the general lesson this
    /// project keeps relearning (<c>docs/guides/extraction.md</c>): write the
    /// validator that asserts the shape of what should have been found, not just what
    /// was. The column list matters as much as the count: two columns silently merged
    /// at a boundary (Level 3 and Level 5 read as one, say) would still pass a row
    /// count and per-row cell count check, so both are asserted here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ExpectedTableShape> ExpectedTableShapes =
        new Dictionary<string, ExpectedTableShape>(StringComparer.Ordinal)
        {
            ["Draconic Ancestors"] = new(10, ["Dragon", "Damage Type"]),
            ["Elven Lineages"] = new(3, ["Lineage", "Level 1", "Level 3", "Level 5"]),
            ["Fiendish Legacies"] = new(3, ["Legacy", "Level 1", "Level 3", "Level 5"]),
        };

    public static ValidationResult ValidateSpecies(IReadOnlyList<SpeciesDefinition> species)
    {
        ArgumentNullException.ThrowIfNull(species);

        var issues = new List<ValidationIssue>();
        AddDuplicateIdIssues(species.Select(entry => entry.Id), "species", issues);

        var foundTableNames = species.SelectMany(entry => entry.Tables.Select(table => table.Name)).ToHashSet(StringComparer.Ordinal);

        foreach (var expectedTable in ExpectedTableShapes.Keys.Where(name => !foundTableNames.Contains(name)))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "species.table.missing",
                expectedTable,
                $"the {expectedTable} table was not found anywhere in the chapter."));
        }

        foreach (var entry in species)
        {
            void Add(ValidationSeverity severity, string code, string message) =>
                issues.Add(new ValidationIssue(severity, code, entry.Id, message));

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Add(ValidationSeverity.Error, "species.name.missing", "Name is blank.");
            }

            if (entry.Sizes.Count == 0)
            {
                Add(ValidationSeverity.Error, "species.size.missing", "No size was extracted.");
            }

            // Every SRD species walks; a speed of 0 means the Speed line was misread.
            if (entry.SpeedFeet <= 0 || entry.SpeedFeet % 5 != 0)
            {
                Add(ValidationSeverity.Error, "species.speed.implausible", $"Speed is {entry.SpeedFeet} ft.");
            }

            if (entry.Traits.Count == 0)
            {
                Add(
                    ValidationSeverity.Error,
                    "species.traits.missing",
                    "No special traits were extracted; every SRD species has at least one.");
            }

            foreach (var trait in entry.Traits.Where(trait => string.IsNullOrWhiteSpace(trait.Name)))
            {
                Add(ValidationSeverity.Error, "species.trait.name_missing", $"A trait has no name: '{trait.Text}'.");
            }

            // Three species (Dragonborn, Elf, Tiefling) carry a full-width sub-table —
            // Draconic Ancestors, Elven Lineages, Fiendish Legacies — that the
            // two-column pass can slice at the column boundary and interleave into
            // whichever trait was open when the table's region arrived. A wide first
            // column can cross into the next column's stream entirely, which is how a
            // table reached a neighbouring species — Elven Lineages into Gnome's
            // Gnomish Lineage, Fiendish Legacies into Human's Versatile — for five
            // affected traits in total. The #116 shape, recurring in this chapter:
            // OriginParser only appends same-typeface prose now, but this is the
            // regression check for when that discipline slips or a future table is
            // added. A table row is column entries with no connecting lowercase word
            // — "Black Acid Gold Fire", "Legacy Level 1 Abyssal" — a run no legitimate
            // trait sentence produces, verified against every trait this chapter
            // currently extracts.
            foreach (var trait in entry.Traits)
            {
                if (TitleCaseRun().IsMatch(trait.Text))
                {
                    Add(
                        ValidationSeverity.Error,
                        "species.trait.table_noise",
                        $"{trait.Name}: trait text carries a run of table-style capitalized words — a table leaked into it.");
                }
            }

            foreach (var table in entry.Tables)
            {
                if (ExpectedTableShapes.TryGetValue(table.Name, out var expected))
                {
                    if (table.Rows.Count != expected.Rows)
                    {
                        Add(
                            ValidationSeverity.Error,
                            "species.table.row_count",
                            $"{table.Name}: found {table.Rows.Count} row(s); expected {expected.Rows} " +
                            "(the model's own row count, not necessarily the page's physical row count — " +
                            "see OriginTable's remarks for Draconic Ancestors).");
                    }

                    // Guards against two columns silently merging at a boundary — Level
                    // 3 and Level 5 read as one, say — which a row-count check alone
                    // cannot see: the row and per-row cell counts both still agree.
                    if (!table.Columns.SequenceEqual(expected.Columns, StringComparer.Ordinal))
                    {
                        Add(
                            ValidationSeverity.Error,
                            "species.table.columns_mismatch",
                            $"{table.Name}: columns [{string.Join(", ", table.Columns)}]; " +
                            $"expected [{string.Join(", ", expected.Columns)}].");
                    }
                }
                else
                {
                    Add(
                        ValidationSeverity.Error,
                        "species.table.unknown",
                        $"'{table.Name}' is not one of the three full-width tables this chapter prints.");
                }

                if (table.Columns.Count == 0)
                {
                    Add(ValidationSeverity.Error, "species.table.columns_missing", $"{table.Name}: no columns were read.");
                }

                foreach (var row in table.Rows)
                {
                    if (row.Count != table.Columns.Count)
                    {
                        Add(
                            ValidationSeverity.Error,
                            "species.table.row_shape",
                            $"{table.Name}: a row has {row.Count} cell(s); the table has {table.Columns.Count} column(s).");
                    }

                    if (row.Any(string.IsNullOrWhiteSpace))
                    {
                        Add(ValidationSeverity.Error, "species.table.cell_empty", $"{table.Name}: a row has a blank cell.");
                    }
                }
            }
        }

        return new ValidationResult(issues);
    }

    // Four or more consecutive Title-Case words with nothing but whitespace between
    // them — no legitimate trait sentence produces this; a table row does.
    [GeneratedRegex(@"(?:\b[A-Z][a-zA-Z'-]*\.?\s+){3}\b[A-Z][a-zA-Z'-]*\b")]
    private static partial Regex TitleCaseRun();

    public static ValidationResult ValidateBackgrounds(IReadOnlyList<BackgroundDefinition> backgrounds)
    {
        ArgumentNullException.ThrowIfNull(backgrounds);

        var issues = new List<ValidationIssue>();
        AddDuplicateIdIssues(backgrounds.Select(entry => entry.Id), "background", issues);

        foreach (var entry in backgrounds)
        {
            void Add(string code, string message) =>
                issues.Add(new ValidationIssue(ValidationSeverity.Error, code, entry.Id, message));

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Add("background.name.missing", "Name is blank.");
            }

            // "A background lists three of your character's ability scores."
            if (entry.AbilityScores.Count != 3)
            {
                Add(
                    "background.abilities.wrong_count",
                    $"Lists {entry.AbilityScores.Count} ability scores; the SRD says every background lists 3.");
            }

            if (entry.AbilityScores.Distinct().Count() != entry.AbilityScores.Count)
            {
                Add("background.abilities.duplicate", "The same ability is listed twice.");
            }

            // "A background gives your character proficiency in two specified skills."
            if (entry.SkillProficiencies.Count != 2)
            {
                Add(
                    "background.skills.wrong_count",
                    $"Grants {entry.SkillProficiencies.Count} skills; the SRD says every background grants 2.");
            }

            // "A background gives your character a specified Origin feat."
            if (string.IsNullOrWhiteSpace(entry.FeatName))
            {
                Add("background.feat.missing", "No Origin feat was extracted.");
            }

            // "Each background gives a character proficiency with one tool."
            if (string.IsNullOrWhiteSpace(entry.ToolProficiency))
            {
                Add("background.tool.missing", "No tool proficiency was extracted.");
            }

            if (string.IsNullOrWhiteSpace(entry.Equipment))
            {
                Add("background.equipment.missing", "No equipment line was extracted.");
            }
        }

        return new ValidationResult(issues);
    }

    private static void AddDuplicateIdIssues(IEnumerable<string> ids, string kind, List<ValidationIssue> issues)
    {
        foreach (var duplicate in ids
                     .GroupBy(id => id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                $"{kind}.id.duplicate",
                duplicate.Key,
                $"{duplicate.Count()} items share this id."));
        }
    }
}
