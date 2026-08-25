using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Validation;

/// <summary>
/// Checks extracted spells.
/// </summary>
/// <remarks>
/// <para>
/// Spells have no self-checking arithmetic the way stat blocks do, so these are shape
/// checks: the things the SRD's own rules text says must be true of every spell.
/// </para>
/// <para>
/// <b>The most important one is the count</b>, and it was missing for far too long. The
/// parser silently dropped 39 of the book's 339 spells — Cure Wounds among them — and
/// nothing caught it, because a spell that is never detected produces no diagnostic and
/// the only figure anyone looked at was the extractor reporting its own total. A number
/// the pipeline prints about itself agrees with the code by construction and checks
/// nothing. This asserts the shape of what should have been found, which is the lesson
/// every other extraction bug in this project already taught.
/// </para>
/// </remarks>
public static class SpellValidator
{
    /// <summary>
    /// How many spell descriptions the SRD's spell chapter contains.
    /// </summary>
    /// <remarks>
    /// Counted from the printed pages 104–175: every spell prints exactly one
    /// level/school/classes line under its heading. If a future extraction legitimately
    /// changes this — a different source edition — move the number and say why in the
    /// same commit, because lowering it to match a broken parser is exactly the failure
    /// this guards.
    /// </remarks>
    public const int ExpectedSpellCount = 339;

    public static ValidationResult Validate(IReadOnlyList<SpellDefinition> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);

        var issues = new List<ValidationIssue>();

        if (spells.Count != ExpectedSpellCount)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "spell.count.unexpected",
                "spells",
                $"Extracted {spells.Count} spells; the SRD's spell chapter has {ExpectedSpellCount}."));
        }

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

        // Spirit Guardians' printed either/or (#375) is structured on exactly one
        // spell, and the shape of that one spell is fixed by print — an exact-count
        // check, not a floor, per the extraction-traps lesson: a floor is the wrong
        // shape for a count the source itself fixes. If a future extraction pass ever
        // finds this grammar on a second spell, or Spirit Guardians' own shape drifts,
        // that is a deliberate discovery for a human to read, not a silent pass.
        var evilCasterSpells = spells.Where(spell => spell.EvilCasterDamageType is not null).ToList();

        if (evilCasterSpells.Count != 1)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "spell.evil_caster_damage_type.unexpected_count",
                "spells",
                $"{evilCasterSpells.Count} spell(s) carry EvilCasterDamageType; expected exactly 1 " +
                "(Spirit Guardians, SRD 5.2.1 p. 164)."));
        }
        else
        {
            var spiritGuardians = evilCasterSpells[0];

            if (spiritGuardians.Id != "spell.spirit-guardians")
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "spell.evil_caster_damage_type.wrong_spell",
                    spiritGuardians.Id,
                    "Only Spirit Guardians prints the alignment-alternative damage grammar."));
            }

            if (spiritGuardians.EvilCasterDamageType != DamageType.Necrotic)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "spell.evil_caster_damage_type.wrong_type",
                    spiritGuardians.Id,
                    $"EvilCasterDamageType is {spiritGuardians.EvilCasterDamageType}; the print is Necrotic."));
            }

            void CheckSingleRadiantComponent(string label, IReadOnlyList<AttackDamage> components)
            {
                var isSingleRadiant3d8 = components.Count == 1
                    && components[0].Type == DamageType.Radiant
                    && components[0].Amount.ToString() == "3d8";

                if (!isSingleRadiant3d8)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "spell.evil_caster_damage_type.malformed_components",
                        spiritGuardians.Id,
                        $"{label} has [{string.Join(", ", components.Select(c => $"{c.Amount} {c.Type}"))}]; " +
                        "expected exactly one 3d8 Radiant component."));
                }
            }

            CheckSingleRadiantComponent("damage", spiritGuardians.Damage);
            CheckSingleRadiantComponent(
                "save.failureDamage",
                spiritGuardians.Save?.FailureDamage ?? []);
        }

        return new ValidationResult(issues);
    }
}
