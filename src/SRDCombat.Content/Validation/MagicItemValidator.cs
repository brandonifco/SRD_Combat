using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Validation;

/// <summary>
/// Checks extracted magic items.
/// </summary>
/// <remarks>
/// Shape checks, like the spell validator's — and the count is exact for the same
/// reason the spell count is: the chapter's contents are fixed by the source, an item
/// the parser never detects raises no diagnostic, and a floor was already proven to
/// sit quietly on a broken number for months.
/// </remarks>
public static class MagicItemValidator
{
    /// <summary>
    /// How many item entries Magic Items A–Z contains (printed pages 209–253): every
    /// entry prints exactly one type line under its heading. Cross-checked against an
    /// independent <c>pdftotext</c> count of type-line-shaped lines — 258 both ways —
    /// rather than trusted from the parser's own total, which is the spell-count lesson.
    /// </summary>
    public const int ExpectedItemCount = 258;

    public static ValidationResult Validate(IReadOnlyList<MagicItemDefinition> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var issues = new List<ValidationIssue>();

        if (items.Count != ExpectedItemCount)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "magic-item.count.unexpected",
                "magic items",
                $"Expected exactly {ExpectedItemCount} magic items, found {items.Count}. " +
                "An undetected item raises no diagnostic, so a wrong count is the only symptom."));
        }

        var duplicateIds = items.GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateIds)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "magic-item.id.duplicate",
                group.Key,
                "Duplicate magic item id."));
        }

        foreach (var item in items)
        {
            if (item.Text.Length == 0)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "magic-item.text.empty", item.Name, "The item has no description."));
            }

            if (item.Rarity == MagicItemRarity.Varies && item.Variants.Count == 0
                && !item.Name.StartsWith("Potions of Healing", StringComparison.Ordinal)
                && !VariesInBody(item))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "magic-item.rarity.varies-unlisted",
                    item.Name,
                    "Rarity varies but the type line printed no variant tiers; the description carries them."));
            }

            if (item.Variants.Count > 0 && item.Rarity != MagicItemRarity.Varies)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "magic-item.rarity.variant-mismatch",
                    item.Name,
                    "An item with variant tiers must have rarity Varies."));
            }
        }

        // Every registry-executed name must exist in the content, so the allowlist
        // cannot quietly outlive a renamed or lost item. The other direction is free:
        // an unregistered item is simply counted as unmodelled.
        var names = items.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var executed in MagicItemRegistry.ExecutedNames.Where(name => !names.Contains(name)))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "magic-item.registry.orphan",
                executed,
                "The registry executes this item but the extracted chapter has no such name."));
        }

        return new ValidationResult(issues);
    }

    /// <summary>
    /// Items whose type line says "Rarity Varies" and whose tiers live in a table in
    /// the description — the Belt of Giant Strength's per-giant rows, the Ioun Stone's
    /// per-stone list, the Feather Token and Figurine of Wondrous Power.
    /// </summary>
    private static bool VariesInBody(MagicItemDefinition item) =>
        item.Text.Contains("Rarity", StringComparison.Ordinal);
}
