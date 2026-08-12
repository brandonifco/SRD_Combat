using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>One drop: who found it, what it reads as, and the draft change it makes.</summary>
/// <param name="MemberIndex">Index into the party, in seating order.</param>
/// <param name="Description">"a +1 Longsword", for the client to narrate.</param>
/// <param name="NewDraft">The finder's draft with the item equipped.</param>
public sealed record LootAward(int MemberIndex, string Description, CharacterDraft NewDraft);

/// <summary>
/// What the gauntlet drops, and for whom.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rates are this project's design, stated here; the items are the book's.</b>
/// The SRD prints no award-rate table — "Adventures hold the promise—but not a
/// guarantee—of finding magic items" is all it says — so the cadence is a design
/// decision like the ladder's shape: <b>one permanent item after each High milestone</b>,
/// the set piece paying out, and nothing on routine rungs. Every drop is an item the
/// registry executes, chosen from the candidates that would actually improve somebody:
/// loot that upgrades nobody is not offered, which is why there is no Headband of
/// Intellect here — nobody in this party casts on Intelligence.
/// </para>
/// <para>
/// <b>Rarity is gated by the finder's level</b> — Uncommon at any level, Rare at level 3
/// and up, nothing dearer, because the printed prices put Very Rare far beyond a tier
/// that ends at level 5. A +N item already owned upgrades in place rather than
/// duplicating, everything else refuses to drop twice for the same character, attunement
/// capacity is checked before an attunement item is offered, and <b>at most one
/// enchantment rides the worn armour</b> — "+1 Armor" and "Adamantine Armor" are
/// different suits in the book, and this model has one body to put a suit on.
/// </para>
/// <para>
/// <b>Potions are the routine drop</b>, on the same argument at a smaller scale: a
/// Moderate rung yields one Potion of Healing, so a run accumulates a consumable supply
/// between milestones without the permanent items arriving any faster. Potency follows
/// the finder's level for the same reason rarity does — a greater potion is Uncommon,
/// which a level 3 party has earned.
/// </para>
/// <para>
/// The pick among candidates goes through <see cref="IRandomSource"/>, so a seeded run
/// finds the same loot every time.
/// </para>
/// </remarks>
public static class LootTable
{
    /// <summary>The potency a character of this level finds.</summary>
    /// <remarks>
    /// Common below level 3 and Uncommon from there, matching the rarity gate the
    /// permanent items use. Superior and supreme are Rare and Very Rare and never drop
    /// in a game that stops at level 5.
    /// </remarks>
    public static HealingPotion PotionFor(int level) =>
        level >= 3 ? HealingPotion.Greater : HealingPotion.Standard;

    /// <summary>Rolls a drop, or returns null when no candidate would improve anybody.</summary>
    public static LootAward? Roll(
        SrdContent content,
        IReadOnlyList<PartyMember> party,
        IReadOnlyList<CharacterState> states,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(random);

        var candidates = new List<LootAward>();

        for (var index = 0; index < party.Count; index++)
        {
            if (!states[index].CanFight)
            {
                continue;
            }

            candidates.AddRange(CandidatesFor(content, party[index], states[index].Level, index));
        }

        // The resolver is the authority on whether a draft is legal; a candidate it
        // refuses is dropped here rather than crashing a run mid-award.
        var legal = candidates.Where(candidate => Resolves(content, candidate.NewDraft)).ToArray();

        return legal.Length == 0 ? null : legal[random.Roll(legal.Length) - 1];
    }

    private static bool Resolves(SrdContent content, CharacterDraft draft)
    {
        try
        {
            _ = CharacterResolver.Resolve(
                draft,
                new CharacterBuildContent(
                    content.SpeciesById[draft.SpeciesId],
                    content.ClassesById[draft.ClassId],
                    content.BackgroundsById[draft.BackgroundId],
                    content.WeaponsById,
                    content.ArmorById,
                    content.MagicItemsById));

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IEnumerable<LootAward> CandidatesFor(
        SrdContent content,
        PartyMember member,
        int level,
        int index)
    {
        var draft = member.Draft;
        var rareAllowed = level >= 3;

        // --- Weapon enchantments, on the first carried weapon. -----------------------
        if (draft.WeaponIds.Count > 0)
        {
            var weaponId = draft.WeaponIds[0];
            var weaponName = content.WeaponsById[weaponId].Name;

            var best = rareAllowed ? 2 : 1;
            var current = BonusVariant(draft, "magic-item.weapon-plus-1-plus-2-or-plus-3", weaponId);

            if (best > current)
            {
                yield return new LootAward(index, $"a +{best} {weaponName}", Equip(
                    draft,
                    new EquippedMagicItem
                    {
                        ItemId = "magic-item.weapon-plus-1-plus-2-or-plus-3",
                        Variant = $"+{best}",
                        BoundWeaponId = weaponId,
                    },
                    replacing: "magic-item.weapon-plus-1-plus-2-or-plus-3"));
            }

            if (rareAllowed && !Owns(draft, "magic-item.vicious-weapon"))
            {
                yield return new LootAward(index, $"a Vicious {weaponName}", Equip(
                    draft,
                    new EquippedMagicItem { ItemId = "magic-item.vicious-weapon", BoundWeaponId = weaponId }));
            }
        }

        // --- Armour: one enchantment per suit, deliberately. -------------------------
        if (draft.ArmorId is { } armorId && !OwnsAnyArmorItem(content, draft))
        {
            var armor = content.ArmorById[armorId];

            if (rareAllowed)
            {
                yield return new LootAward(index, $"+1 {armor.Name}", Equip(
                    draft,
                    new EquippedMagicItem
                    {
                        ItemId = "magic-item.armor-plus-1-plus-2-or-plus-3",
                        Variant = "+1",
                    }));
            }

            if (armor.Category is ArmorCategory.Medium or ArmorCategory.Heavy
                && armorId != "armor.hide-armor")
            {
                yield return new LootAward(index, $"Adamantine {armor.Name}", Equip(
                    draft,
                    new EquippedMagicItem { ItemId = "magic-item.adamantine-armor" }));
            }

            if (rareAllowed && armorId is "armor.chain-mail" or "armor.chain-shirt")
            {
                yield return new LootAward(index, "Elven Chain", Equip(
                    draft,
                    new EquippedMagicItem { ItemId = "magic-item.elven-chain" }));
            }
        }

        // --- Shields. ----------------------------------------------------------------
        if (draft.HasShield)
        {
            var best = rareAllowed ? 2 : 1;
            var current = BonusVariant(draft, "magic-item.shield-plus-1-plus-2-or-plus-3", boundTo: null);

            if (best > current)
            {
                yield return new LootAward(index, $"a +{best} Shield", Equip(
                    draft,
                    new EquippedMagicItem
                    {
                        ItemId = "magic-item.shield-plus-1-plus-2-or-plus-3",
                        Variant = $"+{best}",
                    },
                    replacing: "magic-item.shield-plus-1-plus-2-or-plus-3"));
            }
        }

        // --- Attunement items, only while a slot is free. ----------------------------
        if (AttunedCount(content, draft) < MagicItemRegistry.AttunementLimit)
        {
            if (!Owns(draft, "magic-item.cloak-of-protection"))
            {
                yield return new LootAward(index, "a Cloak of Protection", Equip(
                    draft,
                    new EquippedMagicItem { ItemId = "magic-item.cloak-of-protection" }));
            }

            if (rareAllowed && !Owns(draft, "magic-item.ring-of-protection"))
            {
                yield return new LootAward(index, "a Ring of Protection", Equip(
                    draft,
                    new EquippedMagicItem { ItemId = "magic-item.ring-of-protection" }));
            }

            if (rareAllowed
                && draft.ArmorId is null
                && !draft.HasShield
                && !Owns(draft, "magic-item.bracers-of-defense"))
            {
                yield return new LootAward(index, "Bracers of Defense", Equip(
                    draft,
                    new EquippedMagicItem { ItemId = "magic-item.bracers-of-defense" }));
            }

            if (member.Sheet.AbilityScores[Ability.Strength] < 19
                && draft.WeaponIds.Count > 0
                && content.WeaponsById[draft.WeaponIds[0]].Kind == WeaponKind.Melee
                && !Owns(draft, "magic-item.gauntlets-of-ogre-power"))
            {
                yield return new LootAward(index, "Gauntlets of Ogre Power", Equip(
                    draft,
                    new EquippedMagicItem { ItemId = "magic-item.gauntlets-of-ogre-power" }));
            }

            if (rareAllowed
                && member.Sheet.AbilityScores[Ability.Constitution] < 19
                && !Owns(draft, "magic-item.amulet-of-health"))
            {
                yield return new LootAward(index, "an Amulet of Health", Equip(
                    draft,
                    new EquippedMagicItem { ItemId = "magic-item.amulet-of-health" }));
            }

            if (SpellcastingRules.AbilityFor(draft.ClassId) is not null)
            {
                var best = rareAllowed ? 2 : 1;
                var current = BonusVariant(draft, "magic-item.wand-of-the-war-mage-plus-1-plus-2-or-plus-3", boundTo: null);

                if (best > current)
                {
                    yield return new LootAward(index, $"a Wand of the War Mage, +{best}", Equip(
                        draft,
                        new EquippedMagicItem
                        {
                            ItemId = "magic-item.wand-of-the-war-mage-plus-1-plus-2-or-plus-3",
                            Variant = $"+{best}",
                        },
                        replacing: "magic-item.wand-of-the-war-mage-plus-1-plus-2-or-plus-3"));
                }
            }
        }
    }

    private static bool Owns(CharacterDraft draft, string itemId) =>
        draft.MagicItems.Any(item => item.ItemId == itemId);

    /// <summary>The +N of an owned variant item, 0 when none — bound weapon included in the match.</summary>
    private static int BonusVariant(CharacterDraft draft, string itemId, string? boundTo)
    {
        var owned = draft.MagicItems.FirstOrDefault(item =>
            item.ItemId == itemId && (boundTo is null || item.BoundWeaponId == boundTo));

        return owned?.Variant is { } variant && variant.StartsWith('+')
            && int.TryParse(variant[1..], out var bonus)
                ? bonus
                : 0;
    }

    private static bool OwnsAnyArmorItem(SrdContent content, CharacterDraft draft) =>
        draft.MagicItems.Any(item =>
            content.MagicItemsById.TryGetValue(item.ItemId, out var definition)
            && MagicItemRegistry.PowersFor(definition.Name, item.Variant)?.AppliesToArmor == true);

    private static int AttunedCount(SrdContent content, CharacterDraft draft) =>
        draft.MagicItems.Count(item =>
            content.MagicItemsById.TryGetValue(item.ItemId, out var definition)
            && definition.RequiresAttunement);

    /// <summary>The draft with the item equipped, replacing an outgrown tier of the same item.</summary>
    private static CharacterDraft Equip(
        CharacterDraft draft,
        EquippedMagicItem item,
        string? replacing = null) =>
        draft with
        {
            MagicItems =
            [
                .. draft.MagicItems.Where(owned => replacing is null || owned.ItemId != replacing),
                item,
            ],
        };
}
