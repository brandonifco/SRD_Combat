using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Validation;

/// <summary>
/// Checks extracted classes.
/// </summary>
/// <remarks>
/// The strongest check here is the proficiency bonus: the SRD's Character Advancement
/// table fixes it by level for every class, so a class Features table that disagrees was
/// misread. It plays the same role hit-points-versus-hit-dice does for monsters — an
/// independently known value the extraction has to reproduce.
/// </remarks>
public static partial class ClassValidator
{
    /// <summary>Hit dice the SRD actually uses for classes.</summary>
    private static readonly int[] LegalHitDice = [6, 8, 10, 12];

    public static ValidationResult Validate(IReadOnlyList<ClassDefinition> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);

        var issues = new List<ValidationIssue>();

        // Every level-table feature name must match a feature heading in the class's
        // own prose (subclass included). This is the validator whose absence let the
        // Sorcerer's wrapped rows truncate silently for the chapter's whole life (#78):
        // "Ability Score" is not a heading anywhere, and nothing said so. Written per
        // the standing lesson — assert the shape of what should have been found.
        foreach (var definition in classes)
        {
            var headings = definition.Features.Select(feature => feature.Name)
                .Concat(definition.SubclassFeatures.Select(feature => feature.Name))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var name in definition.Levels.SelectMany(level => level.FeatureNames))
            {
                var bare = name.Split('(')[0].Trim();

                if (headings.Contains(bare)
                    || bare.Contains("Subclass", StringComparison.OrdinalIgnoreCase)
                    || bare is "-" or "—")
                {
                    continue;
                }

                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "class.feature.no_heading",
                    $"{definition.Name}: {name}",
                    "A level-table feature name matches no feature heading — a wrapped or truncated cell."));
            }
        }

        // No feature's prose may carry a run of bare table numbers — the shape the
        // class table's per-level values leak in (#116). Written per the standing
        // lesson, after the leak sat invisible for the chapter's whole life: assert
        // the shape of what should NOT have been found, too. Printed prose lists
        // numbers with punctuation ("15, 14, 13, 12, 10, 8"), so five consecutive
        // bare numbers separated by spaces occur in no legitimate feature text.
        foreach (var definition in classes)
        {
            foreach (var feature in definition.Features.Concat(definition.SubclassFeatures))
            {
                if (TableNumberRun().IsMatch(feature.Text))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "class.feature.table_noise",
                        $"{definition.Name}: {feature.Name}",
                        "Feature prose carries a run of bare numbers — a table leaked into it."));
                }
            }
        }

        foreach (var duplicate in classes
                     .GroupBy(definition => definition.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "class.id.duplicate",
                duplicate.Key,
                $"{duplicate.Count()} classes share this id."));
        }

        foreach (var definition in classes)
        {
            ValidateOne(definition, issues);
        }

        return new ValidationResult(issues);
    }

    // Five or more consecutive bare small numbers separated only by whitespace.
    [GeneratedRegex(@"(\b\d{1,2}\b\s+){4,}\b\d{1,2}\b")]
    private static partial Regex TableNumberRun();

    private static void ValidateOne(ClassDefinition definition, List<ValidationIssue> issues)
    {
        void Add(ValidationSeverity severity, string code, string message) =>
            issues.Add(new ValidationIssue(severity, code, definition.Id, message));

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            Add(ValidationSeverity.Error, "class.name.missing", "Name is blank.");
        }

        if (!LegalHitDice.Contains(definition.HitDieSides))
        {
            Add(
                ValidationSeverity.Error,
                "class.hit_die.implausible",
                $"Hit die is d{definition.HitDieSides}; the SRD uses d6, d8, d10 or d12.");
        }

        // "Saving Throw: Strength and Constitution" — every class lists exactly two.
        if (definition.SavingThrowProficiencies.Count != 2)
        {
            Add(
                ValidationSeverity.Error,
                "class.saving_throws.wrong_count",
                $"Lists {definition.SavingThrowProficiencies.Count} saving throw proficiencies; every class lists 2.");
        }

        if (definition.PrimaryAbilities.Count == 0)
        {
            Add(ValidationSeverity.Error, "class.primary_ability.missing", "No primary ability was extracted.");
        }

        var skillListTooShort = !definition.ChoosesAnySkill
            && definition.SkillChoices.Count < definition.SkillChoiceCount;

        if (definition.SkillChoiceCount <= 0 || skillListTooShort)
        {
            Add(
                ValidationSeverity.Error,
                "class.skills.implausible",
                $"Chooses {definition.SkillChoiceCount} from {definition.SkillChoices.Count} skills.");
        }

        if (string.IsNullOrWhiteSpace(definition.StartingEquipment))
        {
            Add(ValidationSeverity.Error, "class.equipment.missing", "No starting equipment line was extracted.");
        }

        ValidateLevels(definition, Add);
    }

    private static void ValidateLevels(ClassDefinition definition, Action<ValidationSeverity, string, string> add)
    {
        // Every SRD class table runs 1 to 20 with no gaps.
        if (definition.Levels.Count != 20)
        {
            add(
                ValidationSeverity.Error,
                "class.levels.wrong_count",
                $"Has {definition.Levels.Count} level rows; every SRD class table has 20.");
        }

        var expectedLevel = 1;
        foreach (var level in definition.Levels)
        {
            if (level.Level != expectedLevel)
            {
                add(
                    ValidationSeverity.Error,
                    "class.levels.out_of_order",
                    $"Expected level {expectedLevel}, found {level.Level}.");
            }

            expectedLevel = level.Level + 1;

            // The independently-known value: the Character Advancement table fixes the
            // proficiency bonus by level, so any disagreement means the row was misread.
            var expectedBonus = AdvancementRules.ProficiencyBonusForLevel(level.Level);
            if (level.ProficiencyBonus != expectedBonus)
            {
                add(
                    ValidationSeverity.Error,
                    "class.level.proficiency_bonus_wrong",
                    $"Level {level.Level} prints +{level.ProficiencyBonus}; the advancement table says +{expectedBonus}.");
            }

            foreach (var (spellLevel, slots) in level.SpellSlots)
            {
                if (spellLevel is < 1 or > 9 || slots is < 1 or > 4)
                {
                    add(
                        ValidationSeverity.Error,
                        "class.level.spell_slots_implausible",
                        $"Level {level.Level} has {slots} slots of spell level {spellLevel}.");
                }
            }
        }

        // A caster's slots never decrease as it levels; a decrease means columns were
        // misaligned on one row.
        foreach (var spellLevel in definition.Levels.SelectMany(level => level.SpellSlots.Keys).Distinct())
        {
            var series = definition.Levels
                .OrderBy(level => level.Level)
                .Select(level => level.SpellSlots.GetValueOrDefault(spellLevel))
                .ToList();

            for (var index = 1; index < series.Count; index++)
            {
                if (series[index] < series[index - 1])
                {
                    add(
                        ValidationSeverity.Warning,
                        "class.level.spell_slots_decrease",
                        $"Spell level {spellLevel} slots drop from {series[index - 1]} to {series[index]} " +
                        $"at class level {index + 1}.");
                    break;
                }
            }
        }
    }
}
