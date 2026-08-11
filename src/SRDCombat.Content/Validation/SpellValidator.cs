using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Validation;

/// <summary>
/// Checks extracted spells.
/// </summary>
/// <remarks>
/// Spells have no self-checking arithmetic the way stat blocks do, so these are shape
/// checks: the things the SRD's own rules text says must be true of every spell.
/// </remarks>
public static class SpellValidator
{
    public static ValidationResult Validate(IReadOnlyList<SpellDefinition> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);

        var issues = new List<ValidationIssue>();

        foreach (var duplicate in spells
                     .GroupBy(spell => spell.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "spell.id.duplicate",
                duplicate.Key,
                $"{duplicate.Count()} spells share this id."));
        }

        foreach (var spell in spells)
        {
            void Add(string code, string message) =>
                issues.Add(new ValidationIssue(ValidationSeverity.Error, code, spell.Id, message));

            if (string.IsNullOrWhiteSpace(spell.Name))
            {
                Add("spell.name.missing", "Name is blank.");
            }

            // Cantrips are level 0; every other spell is 1-9.
            if (spell.Level is < 0 or > 9)
            {
                Add("spell.level.out_of_range", $"Level is {spell.Level}.");
            }

            // "A spell appears on at least one class's spell list."
            if (spell.Classes.Count == 0)
            {
                Add("spell.classes.missing", "No class list was extracted.");
            }

            if (string.IsNullOrWhiteSpace(spell.CastingTimeText))
            {
                Add("spell.casting_time.missing", "No casting time was extracted.");
            }

            if (string.IsNullOrWhiteSpace(spell.RangeText))
            {
                Add("spell.range.missing", "No range was extracted.");
            }

            if (string.IsNullOrWhiteSpace(spell.DurationText))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "spell.duration.missing",
                    spell.Id,
                    "No duration in the source text; the entry is truncated at a column break."));
            }

            if (string.IsNullOrWhiteSpace(spell.Text))
            {
                Add("spell.text.missing", "No description was extracted.");
            }

            // A warning rather than an error, because the source is at fault rather than
            // the parser. Six spells (Barkskin, Contagion, Divine Smite, Find Steed,
            // Guidance, Resistance) have their Components and Duration lines missing from
            // the PDF's text layer entirely — each sits at the foot of a column and the
            // lines are simply not present, confirmed with two independent extractors.
            // Inventing plausible values would be worse than shipping the gap visibly.
            if (spell.Components == SpellComponents.None)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "spell.components.none",
                    spell.Id,
                    "No components in the source text; the entry is truncated at a column break."));
            }

            // A material component is named only when the M component is present.
            if ((spell.MaterialComponent is not null) != spell.Components.HasFlag(SpellComponents.Material))
            {
                Add(
                    "spell.components.material_inconsistent",
                    $"Material component '{spell.MaterialComponent}' disagrees with components {spell.Components}.");
            }

            // Duration is missing for the same six truncated entries, so its absence is a
            // warning for the same reason.
            // Concentration is stated in the duration, so the flag and the text must agree.
            var saysConcentration = spell.DurationText.Contains(
                "Concentration",
                StringComparison.OrdinalIgnoreCase);

            if (saysConcentration != spell.RequiresConcentration)
            {
                Add(
                    "spell.concentration.inconsistent",
                    $"Duration '{spell.DurationText}' disagrees with the Concentration flag.");
            }
        }

        return new ValidationResult(issues);
    }
}
