namespace SRDCombat.Core.Definitions;

/// <summary>The nine categories the Magic Item Categories table prints (page 204).</summary>
public enum MagicItemCategory
{
    Armor,
    Potion,
    Ring,
    Rod,
    Scroll,
    Staff,
    Wand,
    Weapon,
    WondrousItem,
}

/// <summary>The rarities the Magic Item Rarities and Values table prints (page 206).</summary>
public enum MagicItemRarity
{
    Common,
    Uncommon,
    Rare,
    VeryRare,
    Legendary,
    Artifact,

    /// <summary>The type line prints "Rarity Varies" — the variants carry the real ones.</summary>
    Varies,
}

/// <summary>
/// One tier of a variant item: the "+2" of "Weapon, +1, +2, or +3", with the rarity the
/// type line prints for that tier.
/// </summary>
/// <param name="Suffix">The tier's printed marker — "+1", "+2", "+3".</param>
/// <param name="Rarity">The rarity the type line assigns that tier.</param>
public sealed record MagicItemVariant(string Suffix, MagicItemRarity Rarity);

/// <summary>
/// A magic item from the SRD's Magic Items A–Z (printed pages 209–253).
/// </summary>
/// <remarks>
/// <para>
/// The definition mirrors the book's heading-level structure — name, category, what it
/// applies to, rarity, attunement — plus the description as text. Whether the
/// <em>effect</em> executes is a separate question answered by
/// <c>MagicItemRegistry</c> in <c>Core.Rules</c>, the same split the monster traits use:
/// content records what is printed, and a curated allowlist maps a printed name to an
/// executed effect only alongside the code that does the thing. An item absent from the
/// registry is counted, never silently held.
/// </para>
/// <para>
/// A heading like "Weapon, +1, +2, or +3" is <b>one definition</b> with three
/// <see cref="Variants"/>, exactly as it is one entry in the book; anything that wants
/// one tier of it names the definition plus a variant suffix.
/// </para>
/// </remarks>
public sealed record MagicItemDefinition
{
    /// <summary>Stable slug — <c>magic-item.ring-of-protection</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The heading exactly as printed — "Weapon, +1, +2, or +3".</summary>
    public required string Name { get; init; }

    public required MagicItemCategory Category { get; init; }

    /// <summary>
    /// The type line's parenthetical, as printed — "Any Simple or Martial",
    /// "Chain Mail or Chain Shirt", "Shield". Null when the type line has none.
    /// </summary>
    public string? AppliesTo { get; init; }

    /// <summary>
    /// The item's rarity. <see cref="MagicItemRarity.Varies"/> when the type line prints
    /// per-variant rarities or "Rarity Varies" — see <see cref="Variants"/>.
    /// </summary>
    public required MagicItemRarity Rarity { get; init; }

    /// <summary>The tiers of a variant item, empty for an ordinary one.</summary>
    public IReadOnlyList<MagicItemVariant> Variants { get; init; } = [];

    public bool RequiresAttunement { get; init; }

    /// <summary>
    /// The attunement qualifier when one is printed — "by a Spellcaster". Null for plain
    /// "Requires Attunement" and for items needing none.
    /// </summary>
    public string? AttunementRequirement { get; init; }

    /// <summary>The description, as printed. Tables within it arrive as text lines.</summary>
    public required string Text { get; init; }
}
