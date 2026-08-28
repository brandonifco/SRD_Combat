using SRDCombat.Core.Characters;

namespace SRDCombat.Core.Tests.Characters;

/// <summary>
/// The shared wording both clients read a character's equipped items through (#534).
/// Every case here is built from bare numbers and flags — the same resolved facts
/// <c>CharacterSheet</c> and <c>CombatantFeatures</c> carry — never from
/// <c>MagicItemPowers</c>, which is the registry claim this formatter is deliberately
/// kept apart from.
/// </summary>
public class MagicItemReadoutTests
{
    [Fact]
    public void NoItems_DescribesAsEmpty() =>
        Assert.Equal(
            string.Empty,
            MagicItemReadout.Describe([], spellAttackItemBonus: 0, ignoresHalfCoverOnSpellAttacks: false));

    [Fact]
    public void AnItemWithNoRollEffect_NamesItAndSaysNothingElse() =>
        Assert.Equal(
            "Ring of Protection",
            MagicItemReadout.Describe(
                ["Ring of Protection"],
                spellAttackItemBonus: 0,
                ignoresHalfCoverOnSpellAttacks: false));

    [Fact]
    public void AnItemBonus_IsStatedInPlainWordsWithoutATotal() =>
        Assert.Equal(
            "Wand of the War Mage (+1) · +1 to spell attack rolls",
            MagicItemReadout.Describe(
                ["Wand of the War Mage (+1)"],
                spellAttackItemBonus: 1,
                ignoresHalfCoverOnSpellAttacks: false));

    [Fact]
    public void AnItemBonus_NamesTheTotalAndTheItemsSliceWhenTheTotalIsKnown() =>
        Assert.Equal(
            "Wand of the War Mage (+1) · spell attack +6 (+1 item)",
            MagicItemReadout.Describe(
                ["Wand of the War Mage (+1)"],
                spellAttackItemBonus: 1,
                ignoresHalfCoverOnSpellAttacks: false,
                spellAttackTotalBonus: 6));

    [Fact]
    public void TheCoverExemption_IsStatedInPlainWords() =>
        Assert.Equal(
            "Wand of the War Mage (+1) · ignores Half Cover on spell attacks",
            MagicItemReadout.Describe(
                ["Wand of the War Mage (+1)"],
                spellAttackItemBonus: 0,
                ignoresHalfCoverOnSpellAttacks: true));

    [Fact]
    public void BothWandPowers_AreStatedTogether() =>
        Assert.Equal(
            "Wand of the War Mage (+1) · spell attack +6 (+1 item) · ignores Half Cover on spell attacks",
            MagicItemReadout.Describe(
                ["Wand of the War Mage (+1)"],
                spellAttackItemBonus: 1,
                ignoresHalfCoverOnSpellAttacks: true,
                spellAttackTotalBonus: 6));

    [Fact]
    public void SeveralItems_AreListedTogetherBeforeTheirEffects() =>
        Assert.Equal(
            "Ring of Protection, Wand of the War Mage (+1) · +1 to spell attack rolls",
            MagicItemReadout.Describe(
                ["Ring of Protection", "Wand of the War Mage (+1)"],
                spellAttackItemBonus: 1,
                ignoresHalfCoverOnSpellAttacks: false));

    [Fact]
    public void Announce_NamesTheCharacterAheadOfTheirEquipment() =>
        Assert.Equal(
            "Aldous's equipment: Wand of the War Mage (+1) · +1 to spell attack rolls",
            MagicItemReadout.Announce(
                "Aldous",
                ["Wand of the War Mage (+1)"],
                spellAttackItemBonus: 1,
                ignoresHalfCoverOnSpellAttacks: false));

    [Fact]
    public void Announce_IsEmptyWhenNothingIsEquipped() =>
        Assert.Equal(
            string.Empty,
            MagicItemReadout.Announce(
                "Aldous",
                [],
                spellAttackItemBonus: 0,
                ignoresHalfCoverOnSpellAttacks: false));
}
