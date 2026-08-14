using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// One thing the party could buy: who it is for, what it costs, and the draft change
/// or potion it delivers. A gear offer carries the buyer's whole new draft, exactly as
/// a <see cref="LootAward"/> does — a purchase is a draft change and a re-resolve,
/// never a sheet edit.
/// </summary>
/// <param name="MemberIndex">Index into the party, in seating order.</param>
/// <param name="Description">"Chain Mail for Brenna — 75 GP", for a client to list.</param>
/// <param name="CostCopper">The printed price, in copper so it is exact.</param>
/// <param name="NewDraft">The buyer's draft with the gear equipped; null for a potion.</param>
/// <param name="Potion">The potion bought; null for gear.</param>
/// <param name="Score">
/// How much the purchase improves the buyer, for the auto-buyer's ordering: armor
/// class counts double a point of average damage, a stated weighting rather than a
/// derivation.
/// </param>
public sealed record ShopOffer(
    int MemberIndex,
    string Description,
    int CostCopper,
    CharacterDraft? NewDraft,
    HealingPotion? Potion,
    double Score);

/// <summary>
/// The Long Rest merchant: mundane weapons, armor, shields and Potions of Healing at
/// their exact printed prices, offered only where they would improve somebody.
/// </summary>
/// <remarks>
/// <para>
/// <b>The prices are the book's; the merchant is this project's design.</b> The
/// Equipment chapter prices every weapon and suit of armor to the copper piece and
/// sells a Potion of Healing for 50 GP; what the SRD does not print is a shop, so
/// when one opens — at each Long Rest, the two per cycle that bracket the milestone —
/// is a stated choice like the ladder's shape.
/// </para>
/// <para>
/// <b>An offer must improve its buyer, and the resolver is the judge.</b> Each
/// candidate purchase is a draft change re-resolved: gear the class has no
/// proficiency line for is never offered (the same printed-line readings creation
/// uses), a draft the resolver refuses is dropped, and the resolved sheet must come
/// out strictly better — higher armor class or a harder-hitting attack of the same
/// kind, with neither the other nor the Speed getting worse. The Barbarian is the
/// case that taught the gate its shape, twice: this remark first claimed Chain Mail
/// could never be offered because Unarmored Defense would switch off — and the test
/// proved the resolver disagrees, 16 beats an unarmored 14, so the offer is real and
/// legitimate; and the Speed clause exists because Heavy armor gates Fast Movement,
/// a cost the AC number alone cannot see.
/// </para>
/// <para>
/// Members whose draft carries an armor magic item get no armor offers — "+1 Armor"
/// and a newly bought suit are different suits, and the model has one body to put a
/// suit on, the same reading the loot table states.
/// </para>
/// </remarks>
public static class Shop
{
    /// <summary>A Potion of Healing's printed price: 50 GP.</summary>
    public const int PotionCostCopper = 5000;

    /// <summary>
    /// How many potions the auto-buyer stocks each member up to before it stops:
    /// gold beyond that accumulates toward gear, which compounds where a potion is
    /// drunk once.
    /// </summary>
    public const int AutoBuyPotionCap = 2;

    /// <summary>
    /// Everything the merchant would sell this party today, affordable or not —
    /// a client may want to show what the purse is short of.
    /// </summary>
    public static IReadOnlyList<ShopOffer> Offers(
        SrdContent content,
        IReadOnlyList<PartyMember> party,
        IReadOnlyList<CharacterState> states)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(states);

        var offers = new List<ShopOffer>();

        for (var index = 0; index < party.Count; index++)
        {
            if (!states[index].CanFight)
            {
                continue;
            }

            offers.AddRange(GearFor(content, party[index], states[index].Level, index));
        }

        // One potion offer, for whoever carries the fewest — the same spread rule the
        // Moderate-rung drop follows, and a client can buy it repeatedly.
        if (PotionBuyer(party, states) is { } thirstiest)
        {
            offers.Add(new ShopOffer(
                thirstiest,
                $"Potion of Healing for {party[thirstiest].Draft.Name} — {Price(PotionCostCopper)}",
                PotionCostCopper,
                NewDraft: null,
                HealingPotion.Standard,
                Score: 0));
        }

        return offers;
    }

    /// <summary>
    /// Spends the purse the way a sensible party would: the biggest improvement first
    /// among what it can afford, then potions up to the cap. Returns what was bought,
    /// in order, for a client or a log to narrate.
    /// </summary>
    public static IReadOnlyList<string> AutoBuy(SrdContent content, GauntletRun run)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(run);

        var bought = new List<string>();

        while (true)
        {
            var offer = Offers(content, run.Party, run.States)
                .Where(candidate => candidate.NewDraft is not null)
                .Where(candidate => candidate.CostCopper <= run.GoldCopper)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.CostCopper)
                .ThenBy(candidate => candidate.MemberIndex)
                .ThenBy(candidate => candidate.Description, StringComparer.Ordinal)
                .FirstOrDefault();

            if (offer is null || run.Purchase(offer) is not null)
            {
                break;
            }

            bought.Add(offer.Description);
        }

        while (run.GoldCopper >= PotionCostCopper
               && PotionBuyer(run.Party, run.States) is { } index
               && run.States[index].Potions.Values.Sum() < AutoBuyPotionCap)
        {
            var offer = Offers(content, run.Party, run.States)
                .FirstOrDefault(candidate => candidate.Potion is not null);

            if (offer is null || run.Purchase(offer) is not null)
            {
                break;
            }

            bought.Add(offer.Description);
        }

        return bought;
    }

    /// <summary>"75 GP", or "7 GP 5 SP" when the copper does not divide evenly.</summary>
    public static string Price(int copper)
    {
        var gold = copper / 100;
        var silver = copper % 100 / 10;
        var loose = copper % 10;

        var parts = new List<string>();

        if (gold > 0)
        {
            parts.Add($"{gold} GP");
        }

        if (silver > 0)
        {
            parts.Add($"{silver} SP");
        }

        if (loose > 0 || parts.Count == 0)
        {
            parts.Add($"{loose} CP");
        }

        return string.Join(" ", parts);
    }

    private static int? PotionBuyer(IReadOnlyList<PartyMember> party, IReadOnlyList<CharacterState> states) =>
        states
            .Select((state, index) => (state, index))
            .Where(pair => pair.state.CanFight)
            .OrderBy(pair => pair.state.Potions.Values.Sum())
            .ThenBy(pair => pair.index)
            .Select(pair => (int?)pair.index)
            .FirstOrDefault();

    private static IEnumerable<ShopOffer> GearFor(
        SrdContent content,
        PartyMember member,
        int level,
        int index)
    {
        var @class = content.ClassesById[member.Draft.ClassId];
        var currentAc = member.Sheet.ArmorClass;

        // Armor, unless a magic suit is already on the body.
        if (!OwnsAnyArmorItem(content, member.Draft))
        {
            foreach (var armor in CharacterCreation.ArmorOptions(content, @class))
            {
                if (string.Equals(armor.Id, member.Draft.ArmorId, StringComparison.Ordinal))
                {
                    continue;
                }

                var draft = member.Draft with { ArmorId = armor.Id };

                // Better and nothing worse: the AC must rise and the Speed must not
                // fall — Heavy armor gates Fast Movement, and a suit that armors the
                // Barbarian while slowing it is a trade the buyer decides, not a
                // strict improvement this stall may claim.
                if (Resolve(content, draft, level) is not { } resolved
                    || resolved.Sheet.ArmorClass <= currentAc
                    || resolved.Sheet.SpeedFeet < member.Sheet.SpeedFeet)
                {
                    continue;
                }

                yield return new ShopOffer(
                    index,
                    $"{armor.Name} for {member.Draft.Name} — {Price(armor.CostCopper)}",
                    armor.CostCopper,
                    draft,
                    Potion: null,
                    Score: (resolved.Sheet.ArmorClass - currentAc) * 2.0);
            }
        }

        if (CharacterCreation.MayCarryShield(@class) && !member.Draft.HasShield)
        {
            var draft = member.Draft with { HasShield = true };

            if (Resolve(content, draft, level) is { } resolved
                && resolved.Sheet.ArmorClass > currentAc)
            {
                yield return new ShopOffer(
                    index,
                    $"Shield for {member.Draft.Name} — {Price(ShieldCost(content))}",
                    ShieldCost(content),
                    draft,
                    Potion: null,
                    Score: (resolved.Sheet.ArmorClass - currentAc) * 2.0);
            }
        }

        // Weapons: a candidate replaces the owned weapon of its own kind, so a better
        // blade never costs the Rogue its bow.
        foreach (var weapon in CharacterCreation.WeaponOptions(content, @class))
        {
            if (member.Draft.WeaponIds.Contains(weapon.Id, StringComparer.Ordinal))
            {
                continue;
            }

            var replaced = member.Draft.WeaponIds
                .Where(content.WeaponsById.ContainsKey)
                .FirstOrDefault(owned => content.WeaponsById[owned].Kind == weapon.Kind);

            if (replaced is null)
            {
                continue;
            }

            var draft = member.Draft with
            {
                WeaponIds = [.. member.Draft.WeaponIds.Select(owned =>
                    owned == replaced ? weapon.Id : owned)],
                // Mastery follows the swap: the printed unlock is of a kind of weapon,
                // and the new one takes the old one's place in the plan.
                WeaponMasteryIds = [.. member.Draft.WeaponMasteryIds.Select(owned =>
                    owned == replaced ? weapon.Id : owned)],
            };

            if (Resolve(content, draft, level) is not { } resolved)
            {
                continue;
            }

            var before = BestAverage(member, weapon.Kind, content);
            var after = BestAverage(resolved, weapon.Kind, content);

            if (after <= before
                || resolved.Sheet.ArmorClass < currentAc
                || resolved.Sheet.SpeedFeet < member.Sheet.SpeedFeet)
            {
                continue;
            }

            yield return new ShopOffer(
                index,
                $"{weapon.Name} for {member.Draft.Name} — {Price(weapon.CostCopper)}",
                weapon.CostCopper,
                draft,
                Potion: null,
                Score: after - before);
        }
    }

    /// <summary>
    /// The member's hardest-hitting attack of this kind, by average damage — melee
    /// reach attacks for a melee weapon, ranged for ranged — so the comparison is
    /// like-for-like.
    /// </summary>
    private static double BestAverage(PartyMember member, WeaponKind kind, SrdContent content) =>
        member.Combatant.Stats.Attacks
            .Where(attack => kind == WeaponKind.Ranged
                ? attack.NormalRangeFeet is not null
                : attack.ReachFeet is not null)
            .Select(attack => attack.Damage.Sum(damage => damage.Amount.Average))
            .DefaultIfEmpty(0)
            .Max();

    private static int ShieldCost(SrdContent content) =>
        content.Armor.FirstOrDefault(armor =>
            armor.Name.Contains("Shield", StringComparison.OrdinalIgnoreCase))?.CostCopper ?? 1000;

    private static bool OwnsAnyArmorItem(SrdContent content, CharacterDraft draft) =>
        draft.MagicItems.Any(item =>
            content.MagicItemsById.TryGetValue(item.ItemId, out var definition)
            && definition.Category == MagicItemCategory.Armor);

    private static PartyMember? Resolve(SrdContent content, CharacterDraft draft, int level)
    {
        try
        {
            return PregeneratedParty.Resolve(content, draft, level);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
