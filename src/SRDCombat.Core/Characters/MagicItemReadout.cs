namespace SRDCombat.Core.Characters;

/// <summary>
/// Turns a character's already-resolved magic-item facts into the one wording both
/// clients show — #534: neither client ever named what a character carried, so a
/// Cleric handed a Wand of the War Mage read its silently-working +1 as "he cannot use
/// it".
/// </summary>
/// <remarks>
/// <para>
/// <b>This formats resolved facts, never a registry claim.</b> An earlier version of
/// this fix formatted <c>MagicItemPowers</c> straight off the registry — what an item
/// is <em>printed</em> to do — and review rejected it: a populated
/// <c>MagicItemPowers</c> field says the registry claims the power, not that
/// <c>CharacterResolver</c> or <c>Encounter</c> actually apply it, and a formatter
/// reading the claim would keep faithfully describing it even after execution drifted
/// out from under it — the misattribution shape this project's rule warns against.
/// So every parameter here is a number or a flag something downstream already computed
/// and used: <see cref="CharacterSheet.SpellAttackItemBonus"/> is summed into a live
/// <c>CombatAttack</c>'s bonus by <c>Encounter.Casting</c>, and
/// <see cref="CharacterSheet.IgnoresHalfCoverOnSpellAttacks"/> is read by
/// <c>Encounter</c>'s own Half Cover exemption. There is nothing to say about an item
/// beyond what these already prove happened.
/// </para>
/// <para>
/// <b>The caster's total spell attack bonus is optional</b> because it is not always in
/// hand: a live <c>Combatant</c> mid-fight carries it on
/// <c>CombatantFeatures.SpellAttackBonus</c>, but a bare <c>CharacterSheet</c> — read
/// between fights, before any encounter exists — has nowhere to derive it from without
/// re-deriving the spellcasting-ability lookup <c>CharacterResolver</c> already did.
/// Omitting it still states the item's own contribution in plain words; it only loses
/// the "total, with the item's slice named" framing that a live fight can afford.
/// </para>
/// </remarks>
public static class MagicItemReadout
{
    /// <summary>
    /// The equipped items and what they resolve to — "Wand of the War Mage (+1) · spell
    /// attack +6 (+1 item) · ignores Half Cover on spell attacks", or a bare list of
    /// names when nothing about them changes a roll. Empty when nothing is equipped.
    /// </summary>
    /// <param name="magicItemNames">
    /// Every equipped item's display name — <see cref="CharacterSheet.MagicItemNames"/>
    /// or its combat-carried twin on <c>CombatantFeatures</c>.
    /// </param>
    /// <param name="spellAttackItemBonus">
    /// The resolved item contribution to spell attack rolls; 0 when no equipped item
    /// grants one.
    /// </param>
    /// <param name="ignoresHalfCoverOnSpellAttacks">
    /// Whether an equipped item resolved the Wand's "ignore Half Cover on a spell
    /// attack" power.
    /// </param>
    /// <param name="spellAttackTotalBonus">
    /// The caster's whole spell attack bonus, item included, when it is known — see the
    /// remarks on why a bare sheet cannot always supply it.
    /// </param>
    public static string Describe(
        IReadOnlyList<string> magicItemNames,
        int spellAttackItemBonus,
        bool ignoresHalfCoverOnSpellAttacks,
        int? spellAttackTotalBonus = null)
    {
        ArgumentNullException.ThrowIfNull(magicItemNames);

        if (magicItemNames.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string> { string.Join(", ", magicItemNames) };
        parts.AddRange(EffectParts(spellAttackItemBonus, ignoresHalfCoverOnSpellAttacks, spellAttackTotalBonus));

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// "<paramref name="characterName"/>'s equipment: " plus <see cref="Describe"/> —
    /// the line an interlude prints right where an award lands, naming who carries what
    /// was just found rather than leaving it to the next screen's status block.
    /// Empty when the character carries nothing worth naming.
    /// </summary>
    public static string Announce(
        string characterName,
        IReadOnlyList<string> magicItemNames,
        int spellAttackItemBonus,
        bool ignoresHalfCoverOnSpellAttacks,
        int? spellAttackTotalBonus = null)
    {
        ArgumentNullException.ThrowIfNull(characterName);

        var description = Describe(
            magicItemNames,
            spellAttackItemBonus,
            ignoresHalfCoverOnSpellAttacks,
            spellAttackTotalBonus);

        return description.Length == 0 ? string.Empty : $"{characterName}'s equipment: {description}";
    }

    /// <summary>
    /// The roll-modifier facts alone, in plain words — "spell attack +6 (+1 item)",
    /// "ignores Half Cover on spell attacks", both, or neither. What makes a passive
    /// item's effect legible where <see cref="Describe"/> would otherwise print only a
    /// name.
    /// </summary>
    private static IEnumerable<string> EffectParts(
        int spellAttackItemBonus,
        bool ignoresHalfCoverOnSpellAttacks,
        int? spellAttackTotalBonus)
    {
        if (spellAttackItemBonus != 0)
        {
            yield return spellAttackTotalBonus is { } total
                ? $"spell attack {Signed(total)} ({Signed(spellAttackItemBonus)} item)"
                : $"{Signed(spellAttackItemBonus)} to spell attack rolls";
        }

        if (ignoresHalfCoverOnSpellAttacks)
        {
            yield return "ignores Half Cover on spell attacks";
        }
    }

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();
}
