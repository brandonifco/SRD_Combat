using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>A passive monster trait the engine executes.</summary>
public enum MonsterTrait
{
    /// <summary>
    /// Advantage on an attack roll when at least one ally is within 5 feet of the
    /// target and able to fight.
    /// </summary>
    PackTactics,

    /// <summary>Advantage on saving throws against spells.</summary>
    MagicResistance,

    /// <summary>Provokes no Opportunity Attacks by moving.</summary>
    Flyby,
}

/// <summary>
/// Which printed trait names the engine really executes, and what each one does.
/// </summary>
/// <remarks>
/// <para>
/// A curated allowlist, exactly like <see cref="ConditionRules"/> and
/// <c>ClassFeatureRegistry</c>: a printed name maps to a <see cref="MonsterTrait"/> only
/// when the engine actually does the thing. **Add a name here only alongside the code
/// that implements it.** Every trait absent from this map stays
/// <c>EntryMechanics.Unmodelled</c> in the content and is counted, never quietly inert.
/// </para>
/// <para>
/// The three implemented, and the reading each one rests on:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Pack Tactics</b> — "Advantage on an attack roll against a creature if at least one
/// of the wolf's allies is within 5 feet of the creature and the ally doesn't have the
/// Incapacitated condition." The engine asks <see cref="Combat.Combatant.IsActive"/> of
/// the ally rather than only "not Incapacitated": a dead or dying ally is no help, and
/// Unconscious brings Incapacitated with it anyway. Applies to Opportunity Attacks too —
/// the printed rule says attack rolls, not the Attack action.
/// </item>
/// <item>
/// <b>Magic Resistance</b> — "Advantage on saving throws against spells and other
/// magical effects." A spell is magical by definition; a stat block's saving-throw
/// entry — a breath weapon, a spray, a bellow — is read as <em>not</em> one, because
/// the SRD prints no marker this model captures for which entries are magical.
/// Narrower than the printed sentence, deliberately: Advantage a rule did not grant is
/// worse than Advantage withheld where print is ambiguous. Revisit if the extractor
/// ever captures a magical marker.
/// </item>
/// <item>
/// <b>Flyby</b> — "doesn't provoke Opportunity Attacks when it flies out of an enemy's
/// reach." The engine does not model movement modes, so a creature printed with Flyby
/// is read as flying whenever it moves — every one of them has a fly Speed it would
/// have no reason not to use.
/// </item>
/// </list>
/// <para>
/// Deliberately absent, and why: <b>Spider Climb</b> and <b>Incorporeal Movement</b>
/// need verticality and wall-passing the grid does not model; <b>Swarm</b> needs
/// space-sharing and hit-point-gated damage halving; <b>Sunlight Sensitivity</b> needs
/// a light model; <b>Undead Fortitude</b> needs a hook at the moment damage would drop
/// the creature, and is likely the best next addition. None of them may be added as a
/// name alone.
/// </para>
/// <para>
/// The extractor still classifies these entries <c>Unmodelled</c>; reclassifying them
/// needs a regeneration and belongs with the accounting work already filed (#28).
/// </para>
/// </remarks>
public static class MonsterTraitRegistry
{
    private static readonly Dictionary<string, MonsterTrait> Implemented = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pack Tactics"] = MonsterTrait.PackTactics,
        ["Magic Resistance"] = MonsterTrait.MagicResistance,
        ["Flyby"] = MonsterTrait.Flyby,
    };

    /// <summary>The implemented traits among a stat block's entries.</summary>
    public static IReadOnlyCollection<MonsterTrait> TraitsOf(IEnumerable<MonsterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .Where(entry => entry.Section == MonsterEntrySection.Trait)
            .Select(entry => Implemented.TryGetValue(entry.Name, out var trait) ? trait : (MonsterTrait?)null)
            .OfType<MonsterTrait>()
            .ToHashSet();
    }
}
