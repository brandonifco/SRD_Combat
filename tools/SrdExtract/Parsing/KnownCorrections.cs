using SRDCombat.Core.Definitions;

namespace SrdExtract.Parsing;

/// <summary>
/// Repairs for values that cannot be read correctly from the PDF, applied after
/// parsing and before validation.
/// </summary>
/// <remarks>
/// <para>
/// This list exists so that corrections are auditable rather than hidden inside a
/// parser special case. Two rules govern what belongs here:
/// </para>
/// <list type="number">
/// <item>
/// Only <em>extraction</em> artifacts — cases where the printed page is right and the
/// text layer is wrong. Where the SRD itself is inconsistent, the printed value is kept
/// and the validator reports it. The Archmage's <c>CR 12 (XP 8,000)</c> disagrees with
/// the SRD's own CR table (8,400) and is deliberately <em>not</em> corrected here:
/// silently overriding the source would be worse than carrying the discrepancy.
/// </item>
/// <item>
/// Only where the correct value is certain from the rules, not merely plausible.
/// </item>
/// </list>
/// <para>
/// Every entry is applied by matching the value it expects to find. A correction whose
/// expected value no longer matches is reported rather than applied, so an improvement
/// to the parser that makes a correction unnecessary surfaces loudly instead of
/// silently rewriting something it now reads correctly.
/// </para>
/// </remarks>
internal static class KnownCorrections
{
    /// <summary>
    /// Applies every correction, returning the corrected monsters and a diagnostic for
    /// any correction that no longer applies.
    /// </summary>
    public static (IReadOnlyList<MonsterDefinition> Monsters, IReadOnlyList<ParseDiagnostic> Diagnostics)
        Apply(IReadOnlyList<MonsterDefinition> monsters)
    {
        var diagnostics = new List<ParseDiagnostic>();
        var corrected = monsters.ToList();

        // The Young White Dragon's Intelligence save renders as "2" in the PDF's text
        // layer; the printed block shows -2. Certain from the rules: the dragon is not
        // proficient in Intelligence saves, so the save must equal the -2 modifier, and
        // +2 matches neither the modifier nor modifier-plus-proficiency.
        ApplyAbilitySaveFix(
            corrected,
            diagnostics,
            monsterId: "monster.young-white-dragon",
            ability: Ability.Intelligence,
            expected: 2,
            replacement: -2);

        return (corrected, diagnostics);
    }

    private static void ApplyAbilitySaveFix(
        List<MonsterDefinition> monsters,
        List<ParseDiagnostic> diagnostics,
        string monsterId,
        Ability ability,
        int expected,
        int replacement)
    {
        var index = monsters.FindIndex(monster => monster.Id == monsterId);

        if (index < 0)
        {
            diagnostics.Add(new ParseDiagnostic(monsterId, "correction targets a monster that was not parsed."));
            return;
        }

        var monster = monsters[index];

        if (!monster.Abilities.TryGetValue(ability, out var current) || current.SaveBonus != expected)
        {
            diagnostics.Add(new ParseDiagnostic(
                monsterId,
                $"correction expected a {ability} save of {expected:+0;-0;+0} but found " +
                $"{current?.SaveBonus.ToString("+0;-0;+0", System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"} — " +
                "the correction is stale and should be removed."));
            return;
        }

        var abilities = monster.Abilities.ToDictionary(pair => pair.Key, pair => pair.Value);
        abilities[ability] = current with { SaveBonus = replacement };

        monsters[index] = monster with { Abilities = abilities };
    }
}
