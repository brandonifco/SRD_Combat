using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Validation;

/// <summary>
/// Checks extracted weapons and armor. The invariants here are mostly about optional
/// data that must be present exactly when a property says it is — a Versatile weapon
/// with no two-handed damage, or an Ammunition weapon with no range, is a row whose
/// columns were misaligned during extraction.
/// </summary>
public static class EquipmentValidator
{
    public static ValidationResult ValidateWeapons(IReadOnlyList<WeaponDefinition> weapons)
    {
        ArgumentNullException.ThrowIfNull(weapons);

        var issues = new List<ValidationIssue>();
        AddDuplicateIdIssues(weapons.Select(weapon => weapon.Id), "weapon", issues);

        foreach (var weapon in weapons)
        {
            void Add(string code, string message) =>
                issues.Add(new ValidationIssue(ValidationSeverity.Error, code, weapon.Id, message));

            if (string.IsNullOrWhiteSpace(weapon.Name))
            {
                Add("weapon.name.missing", "Name is blank.");
            }

            if (weapon.Damage.Maximum <= 0)
            {
                Add("weapon.damage.not_positive", $"Damage {weapon.Damage} cannot deal damage.");
            }

            var isVersatile = weapon.Properties.HasFlag(WeaponProperty.Versatile);
            if (isVersatile != (weapon.VersatileDamage is not null))
            {
                Add(
                    "weapon.versatile.inconsistent",
                    isVersatile
                        ? "Has the Versatile property but no two-handed damage."
                        : $"Has two-handed damage {weapon.VersatileDamage} but not the Versatile property.");
            }

            if (weapon.VersatileDamage is { } versatile && versatile.Average <= weapon.Damage.Average)
            {
                Add(
                    "weapon.versatile.not_greater",
                    $"Two-handed damage {versatile} does not exceed one-handed {weapon.Damage}.");
            }

            var usesAmmunition = weapon.Properties.HasFlag(WeaponProperty.Ammunition);
            if (usesAmmunition != (weapon.AmmunitionKind is not null))
            {
                Add(
                    "weapon.ammunition.inconsistent",
                    usesAmmunition
                        ? "Has the Ammunition property but names no ammunition."
                        : $"Names ammunition '{weapon.AmmunitionKind}' but lacks the Ammunition property.");
            }

            // Range comes from either Ammunition or Thrown; a weapon with neither has
            // no range band, and one with either must have one.
            var expectsRange = usesAmmunition || weapon.Properties.HasFlag(WeaponProperty.Thrown);
            if (expectsRange != (weapon.Range is not null))
            {
                Add(
                    "weapon.range.inconsistent",
                    expectsRange
                        ? "Is Ammunition or Thrown but has no range band."
                        : $"Has range {weapon.Range?.NormalFeet}/{weapon.Range?.LongFeet} but is neither Ammunition nor Thrown.");
            }

            if (weapon.Range is { } range && range.LongFeet < range.NormalFeet)
            {
                Add(
                    "weapon.range.long_shorter_than_normal",
                    $"Long range {range.LongFeet} ft. is under normal range {range.NormalFeet} ft.");
            }

            if (weapon.Kind == WeaponKind.Ranged && !usesAmmunition && !weapon.Properties.HasFlag(WeaponProperty.Thrown))
            {
                Add("weapon.ranged.no_delivery", "Is a ranged weapon but is neither Ammunition nor Thrown.");
            }

            if (weapon.CostCopper < 0)
            {
                Add("weapon.cost.negative", $"Cost is {weapon.CostCopper} cp.");
            }

            if (weapon.WeightPounds < 0)
            {
                Add("weapon.weight.negative", $"Weight is {weapon.WeightPounds} lb.");
            }
        }

        return new ValidationResult(issues);
    }

    public static ValidationResult ValidateArmor(IReadOnlyList<ArmorDefinition> armors)
    {
        ArgumentNullException.ThrowIfNull(armors);

        var issues = new List<ValidationIssue>();
        AddDuplicateIdIssues(armors.Select(armor => armor.Id), "armor", issues);

        foreach (var armor in armors)
        {
            void Add(string code, string message) =>
                issues.Add(new ValidationIssue(ValidationSeverity.Error, code, armor.Id, message));

            if (string.IsNullOrWhiteSpace(armor.Name))
            {
                Add("armor.name.missing", "Name is blank.");
            }

            // Light and Medium armor add Dexterity; Heavy armor and Shields do not.
            var shouldAddDexterity = armor.Category is ArmorCategory.Light or ArmorCategory.Medium;
            if (armor.AddsDexterityModifier != shouldAddDexterity)
            {
                Add(
                    "armor.dexterity.inconsistent_with_category",
                    $"{armor.Category} armor should {(shouldAddDexterity ? "add" : "not add")} the Dexterity modifier.");
            }

            // Only Medium armor caps Dexterity, and it always caps it at 2.
            if (armor.MaximumDexterityModifier is { } cap)
            {
                if (armor.Category != ArmorCategory.Medium)
                {
                    Add("armor.dexterity.unexpected_cap", $"{armor.Category} armor should not cap Dexterity.");
                }
                else if (cap != 2)
                {
                    Add("armor.dexterity.unexpected_cap_value", $"Medium armor caps Dexterity at 2, not {cap}.");
                }
            }
            else if (armor.Category == ArmorCategory.Medium)
            {
                Add("armor.dexterity.missing_cap", "Medium armor should cap the Dexterity modifier at 2.");
            }

            if (armor.MinimumStrength is { } strength && armor.Category != ArmorCategory.Heavy)
            {
                Add(
                    "armor.strength.unexpected",
                    $"{armor.Category} armor should have no Strength requirement, but requires {strength}.");
            }

            if (armor.BaseArmorClass <= 0)
            {
                Add("armor.armor_class.not_positive", $"Armor Class value is {armor.BaseArmorClass}.");
            }

            if (armor.CostCopper < 0)
            {
                Add("armor.cost.negative", $"Cost is {armor.CostCopper} cp.");
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
