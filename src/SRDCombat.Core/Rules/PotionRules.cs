using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Rules;

/// <summary>
/// The four potencies the Potions of Healing table prints (page 236).
/// </summary>
/// <remarks>
/// The book gives the plain one no qualifier and the other three "(greater)",
/// "(superior)" and "(supreme)", so <see cref="Standard"/> is this code's name for the
/// unqualified row rather than a printed one.
/// </remarks>
public enum HealingPotion
{
    Standard,
    Greater,
    Superior,
    Supreme,
}

/// <summary>
/// Potions of Healing: what each potency restores, and what drinking one costs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a curated rules map rather than extracted content.</b> Every other
/// magic item's mechanics live on its type line, which <c>MagicItemParser</c> reads.
/// The potencies do not: the chapter prints <em>one</em> entry, "Potions of Healing",
/// whose type line says only "Potion, Rarity Varies", and the four rows live in a table
/// inside the description. Parsing a body-text table for one item would be a grammar
/// with a single customer, so the rows are transcribed here — checked against print,
/// with the entry itself still extracted and counted like every other item.
/// </para>
/// <para>
/// <b>Only the first two are reachable in this game.</b> Superior and supreme are Rare
/// and Very Rare, and a Very Rare item's printed value is 40,000 GP — far past a tier
/// that stops at level 5. They are modelled anyway because the table prints them and
/// leaving rows out would be inventing a shorter table; <c>LootTable</c> decides what
/// actually drops.
/// </para>
/// <para>
/// <b>Two readings are written down here.</b> The printed cost is "Drinking a potion or
/// administering it to another creature requires a Bonus Action" (page 204) — the same
/// cost either way, which is what makes pouring one down an Unconscious ally's throat
/// worth doing. And the SRD sets no range on administering, so this engine reads it as
/// requiring <see cref="ReachFeet"/>: you have to be able to touch someone to make them
/// drink, and every other touch-shaped rule in the game is 5 feet.
/// </para>
/// </remarks>
public static class PotionRules
{
    /// <summary>How far a creature can reach to administer a potion to someone else.</summary>
    public static readonly int ReachFeet = Battlefield.FeetPerSquare;

    /// <summary>The hit points a potency restores, exactly as the table prints them.</summary>
    public static DiceExpression Healing(HealingPotion potency) => potency switch
    {
        HealingPotion.Standard => new DiceExpression(2, 4, 2),
        HealingPotion.Greater => new DiceExpression(4, 4, 4),
        HealingPotion.Superior => new DiceExpression(8, 4, 8),
        HealingPotion.Supreme => new DiceExpression(10, 4, 20),
        _ => throw new ArgumentOutOfRangeException(nameof(potency), potency, "Unknown potency."),
    };

    /// <summary>The rarity the table prints for a potency.</summary>
    public static MagicItemRarity RarityOf(HealingPotion potency) => potency switch
    {
        HealingPotion.Standard => MagicItemRarity.Common,
        HealingPotion.Greater => MagicItemRarity.Uncommon,
        HealingPotion.Superior => MagicItemRarity.Rare,
        HealingPotion.Supreme => MagicItemRarity.VeryRare,
        _ => throw new ArgumentOutOfRangeException(nameof(potency), potency, "Unknown potency."),
    };

    /// <summary>The item's name as the table prints it.</summary>
    public static string PrintedName(HealingPotion potency) => potency switch
    {
        HealingPotion.Standard => "Potion of Healing",
        HealingPotion.Greater => "Potion of Healing (greater)",
        HealingPotion.Superior => "Potion of Healing (superior)",
        HealingPotion.Supreme => "Potion of Healing (supreme)",
        _ => throw new ArgumentOutOfRangeException(nameof(potency), potency, "Unknown potency."),
    };

    /// <summary>The extracted chapter entry these potencies come from.</summary>
    public const string ItemId = "magic-item.potions-of-healing";
}
