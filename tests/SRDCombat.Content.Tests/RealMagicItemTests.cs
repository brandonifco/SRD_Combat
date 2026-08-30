using SRDCombat.Content.Validation;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Equips real extracted magic items on real characters and checks that the numbers the
/// printed descriptions promise are the numbers the resolver derives — and that
/// everything the engine does not execute is refused by name rather than worn as
/// decoration.
/// </summary>
public class RealMagicItemTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void TheChapterExtractsWhole()
    {
        // The exact count is enforced at load; what this adds is the registry's view.
        Assert.Equal(MagicItemValidator.ExpectedItemCount, Content.MagicItems.Count);

        var executed = Content.MagicItems.Count(MagicItemRegistry.Executes);

        Assert.Equal(MagicItemRegistry.ExecutedNames.Count, executed);
    }

    [Fact]
    public void TheCuratedItemsCarryTheirPrintedShape()
    {
        var ring = Content.MagicItemsById["magic-item.ring-of-protection"];

        Assert.Equal(MagicItemCategory.Ring, ring.Category);
        Assert.Equal(MagicItemRarity.Rare, ring.Rarity);
        Assert.True(ring.RequiresAttunement);

        var weapon = Content.MagicItemsById["magic-item.weapon-plus-1-plus-2-or-plus-3"];

        Assert.Equal(MagicItemRarity.Varies, weapon.Rarity);
        Assert.Equal(
            [("+1", MagicItemRarity.Uncommon), ("+2", MagicItemRarity.Rare), ("+3", MagicItemRarity.VeryRare)],
            weapon.Variants.Select(variant => (variant.Suffix, variant.Rarity)));

        var wand = Content.MagicItemsById["magic-item.wand-of-the-war-mage-plus-1-plus-2-or-plus-3"];

        Assert.True(wand.RequiresAttunement);
        Assert.Equal("by a Spellcaster", wand.AttunementRequirement);
    }

    [Fact]
    public void APlusOneLongswordRaisesAttackAndDamageByExactlyOne()
    {
        var plain = Fighter();
        var armed = Fighter(new EquippedMagicItem
        {
            ItemId = "magic-item.weapon-plus-1-plus-2-or-plus-3",
            Variant = "+1",
            BoundWeaponId = "weapon.longsword",
        });

        Assert.Equal(plain.Attacks[0].AttackBonus + 1, armed.Attacks[0].AttackBonus);
        Assert.Equal(
            plain.Attacks[0].Damage[0].Amount.Modifier + 1,
            armed.Attacks[0].Damage[0].Amount.Modifier);
    }

    [Fact]
    public void AViciousWeaponAddsItsDiceAsASecondComponentOfTheSameType()
    {
        var armed = Fighter(new EquippedMagicItem
        {
            ItemId = "magic-item.vicious-weapon",
            BoundWeaponId = "weapon.longsword",
        });

        // "deals an extra 2d6 damage ... of the same type as the weapon's normal damage"
        Assert.Equal(2, armed.Attacks[0].Damage.Count);
        Assert.Equal("2d6", armed.Attacks[0].Damage[1].Amount.ToString());
        Assert.Equal(armed.Attacks[0].Damage[0].Type, armed.Attacks[0].Damage[1].Type);
    }

    [Fact]
    public void TheRingOfProtectionRaisesArmorClassAndEverySavingThrowByOne()
    {
        var plain = Fighter();
        var ringed = Fighter(new EquippedMagicItem { ItemId = "magic-item.ring-of-protection" });

        Assert.Equal(plain.ArmorClass + 1, ringed.ArmorClass);

        foreach (var ability in Enum.GetValues<Ability>())
        {
            Assert.Equal(plain.SavingThrows[ability] + 1, ringed.SavingThrows[ability]);
        }
    }

    [Fact]
    public void BracersOfDefenseWorkOnlyWithoutArmorAndShield()
    {
        // The Barbarian wears nothing, which is exactly who the bracers are for.
        var bare = Barbarian();
        var braced = Barbarian(new EquippedMagicItem { ItemId = "magic-item.bracers-of-defense" });

        Assert.Equal(bare.ArmorClass + 2, braced.ArmorClass);

        // "if you are wearing no armor and using no Shield" — a shield turns them off.
        var shielded = Barbarian(
            new EquippedMagicItem { ItemId = "magic-item.bracers-of-defense" },
            hasShield: true);
        var shieldedBare = Barbarian(hasShield: true);

        Assert.Equal(shieldedBare.ArmorClass, shielded.ArmorClass);
    }

    [Fact]
    public void GauntletsOfOgrePowerAreAFloorNotABonus()
    {
        var gauntleted = Fighter(new EquippedMagicItem { ItemId = "magic-item.gauntlets-of-ogre-power" });

        // The fighter's Strength resolves to 17; the gauntlets say it is 19.
        Assert.Equal(19, gauntleted.AbilityScores[Ability.Strength]);

        // And the attack derives from the new score: +4 modifier instead of +3.
        Assert.Equal(Fighter().Attacks[0].AttackBonus + 1, gauntleted.Attacks[0].AttackBonus);
    }

    [Fact]
    public void AdamantineArmorSetsTheFlagAndRefusesTheWrongArmor()
    {
        var armored = Fighter(new EquippedMagicItem { ItemId = "magic-item.adamantine-armor" });

        Assert.True(armored.CriticalHitsAgainstBecomeNormal);

        // "(Any Medium or Heavy, Except Hide Armor)" — leather is Light and refused.
        var refusal = Assert.Throws<ArgumentException>(() => Rogue(
            new EquippedMagicItem { ItemId = "magic-item.adamantine-armor" }));

        Assert.Contains("Adamantine Armor", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWandOfTheWarMageNeedsACasterAndRaisesOnlySpellAttacks()
    {
        var wand = new EquippedMagicItem
        {
            ItemId = "magic-item.wand-of-the-war-mage-plus-1-plus-2-or-plus-3",
            Variant = "+2",
        };

        Assert.Equal(2, Cleric(wand).SpellAttackItemBonus);

        // "Requires Attunement by a Spellcaster" — a Fighter is refused.
        var refusal = Assert.Throws<ArgumentException>(() => Fighter(wand));

        Assert.Contains("Spellcaster", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #534: the wand's numbers were never wrong — <c>CharacterSheet.SpellAttackItemBonus</c>
    /// resolves correctly, above — but nothing carried it, or the name it came with,
    /// onto the combatant a fight actually reads from. This pins that
    /// <see cref="CombatantStats.FromCharacter"/> carries both across, distinct from the
    /// total the caster's spell attack already folds them into, and that the exemption
    /// crosses too. <c>CoverTests.TheWand_IgnoresHalfCoverOnSpellAttacks</c> (Core.Tests)
    /// already pins the exemption acting on a live attack roll; this pins the readout
    /// data next to it.
    /// </summary>
    [Fact]
    public void TheWandCarriesIntoCombatAsADistinctContribution()
    {
        var sheet = Cleric(new EquippedMagicItem
        {
            ItemId = "magic-item.wand-of-the-war-mage-plus-1-plus-2-or-plus-3",
            Variant = "+1",
        });

        var stats = CombatantStats.FromCharacter(sheet, spellcastingAbility: Ability.Wisdom);

        Assert.NotNull(stats.Character);
        Assert.Equal(1, stats.Character!.SpellAttackItemBonus);
        Assert.Contains(
            stats.Character.MagicItemNames,
            name => name.Contains("Wand of the War Mage", StringComparison.Ordinal));
        Assert.True(stats.IgnoresHalfCoverOnSpellAttacks);

        // The total must still include the item — carrying its slice separately must
        // not un-fold it back out of the number Encounter.Casting actually rolls
        // against.
        var expectedTotal =
            SpellcastingRules.AttackBonus(sheet.ProficiencyBonus, sheet.Modifier(Ability.Wisdom)) + 1;

        Assert.Equal(expectedTotal, stats.Character.SpellAttackBonus);
    }

    /// <summary>
    /// #534's readout, over the same real sheet the resolver produces: both of the
    /// wand's powers, in plain words, without the player needing to know what "+1"
    /// means on an item card.
    /// </summary>
    [Fact]
    public void TheReadoutStatesBothResolvedWandFacts()
    {
        var sheet = Cleric(new EquippedMagicItem
        {
            ItemId = "magic-item.wand-of-the-war-mage-plus-1-plus-2-or-plus-3",
            Variant = "+1",
        });

        var readout = MagicItemReadout.Describe(
            sheet.MagicItemNames,
            sheet.SpellAttackItemBonus,
            sheet.IgnoresHalfCoverOnSpellAttacks);

        Assert.Contains("Wand of the War Mage", readout, StringComparison.Ordinal);
        Assert.Contains("+1 to spell attack rolls", readout, StringComparison.Ordinal);
        Assert.Contains("ignores Half Cover on spell attacks", readout, StringComparison.Ordinal);
    }

    [Fact]
    public void ElvenChainIsOnlyEverChain()
    {
        var chain = Fighter(new EquippedMagicItem { ItemId = "magic-item.elven-chain" });

        Assert.Equal(Fighter().ArmorClass + 1, chain.ArmorClass);

        var refusal = Assert.Throws<ArgumentException>(() => Rogue(
            new EquippedMagicItem { ItemId = "magic-item.elven-chain" }));

        Assert.Contains("Elven Chain", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemTheEngineDoesNotExecuteIsRefusedByName()
    {
        var refusal = Assert.Throws<ArgumentException>(() => Fighter(
            new EquippedMagicItem { ItemId = "magic-item.bag-of-holding" }));

        Assert.Contains("does not execute", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttunementStopsAtThreeAndRefusesDuplicates()
    {
        // Ring, Cloak, Gauntlets, Amulet — four attunements is one over the printed cap.
        var refusal = Assert.Throws<ArgumentException>(() => Fighter(
            new EquippedMagicItem { ItemId = "magic-item.ring-of-protection" },
            new EquippedMagicItem { ItemId = "magic-item.cloak-of-protection" },
            new EquippedMagicItem { ItemId = "magic-item.gauntlets-of-ogre-power" },
            new EquippedMagicItem { ItemId = "magic-item.amulet-of-health" }));

        Assert.Contains("no more than three", refusal.Message, StringComparison.Ordinal);

        var duplicate = Assert.Throws<ArgumentException>(() => Fighter(
            new EquippedMagicItem { ItemId = "magic-item.ring-of-protection" },
            new EquippedMagicItem { ItemId = "magic-item.ring-of-protection" }));

        Assert.Contains("more than one copy", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWeaponEnchantmentMustBeBoundToACarriedWeapon()
    {
        var unbound = Assert.Throws<ArgumentException>(() => Fighter(
            new EquippedMagicItem { ItemId = "magic-item.vicious-weapon" }));

        Assert.Contains("must be bound", unbound.Message, StringComparison.Ordinal);

        var wrongWeapon = Assert.Throws<ArgumentException>(() => Fighter(
            new EquippedMagicItem { ItemId = "magic-item.vicious-weapon", BoundWeaponId = "weapon.greataxe" }));

        Assert.Contains("does not carry", wrongWeapon.Message, StringComparison.Ordinal);
    }

    private static CharacterSheet Fighter(params EquippedMagicItem[] items) =>
        Build("class.fighter", "armor.chain-mail", hasShield: true, items);

    private static CharacterSheet Rogue(params EquippedMagicItem[] items) =>
        Build("class.rogue", "armor.leather-armor", hasShield: false, items);

    private static CharacterSheet Cleric(params EquippedMagicItem[] items) =>
        Build("class.cleric", "armor.chain-shirt", hasShield: true, items);

    private static CharacterSheet Barbarian(EquippedMagicItem? item = null, bool hasShield = false) =>
        Build("class.barbarian", armorId: null, hasShield, item is null ? [] : [item]);

    private static CharacterSheet Build(
        string classId,
        string? armorId,
        bool hasShield,
        IReadOnlyList<EquippedMagicItem> items)
    {
        var background = Content.BackgroundsById["background.soldier"];

        var draft = new CharacterDraft
        {
            Name = "Test",
            SpeciesId = "species.human",
            ClassId = classId,
            BackgroundId = "background.soldier",
            Level = 1,
            BaseAbilityScores = new Dictionary<Ability, int>
            {
                [Ability.Strength] = 15,
                [Ability.Dexterity] = 13,
                [Ability.Constitution] = 14,
                [Ability.Intelligence] = 12,
                [Ability.Wisdom] = 10,
                [Ability.Charisma] = 8,
            },
            PrimaryIncrease = background.AbilityScores[0],
            SecondaryIncrease = background.AbilityScores[1],
            WeaponIds = ["weapon.longsword"],
            ArmorId = armorId,
            HasShield = hasShield,
            MagicItems = items,
        };

        return CharacterResolver.Resolve(
            draft,
            new CharacterBuildContent(
                Content.SpeciesById["species.human"],
                Content.ClassesById[classId],
                background,
                Content.WeaponsById,
                Content.ArmorById,
                Content.MagicItemsById));
    }
}
