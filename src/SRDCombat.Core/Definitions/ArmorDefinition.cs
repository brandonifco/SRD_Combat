namespace SRDCombat.Core.Definitions;

/// <summary>
/// Armor category. Determines how the Dexterity modifier applies to AC, and how long
/// the armor takes to don and doff.
/// </summary>
public enum ArmorCategory
{
    Light,
    Medium,
    Heavy,
    Shield,
}

/// <summary>Armor from the SRD 5.2.1 Armor table.</summary>
public sealed record ArmorDefinition
{
    /// <summary>Stable slug — <c>armor.studded-leather-armor</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required ArmorCategory Category { get; init; }

    /// <summary>
    /// The armor's base AC. For a Shield this is the <c>+2</c> bonus rather than a base
    /// value — <see cref="Category"/> is what tells the two apart.
    /// </summary>
    public required int BaseArmorClass { get; init; }

    /// <summary>
    /// Whether the wearer's Dexterity modifier adds to AC. True for Light and Medium
    /// armor, false for Heavy armor and Shields.
    /// </summary>
    public required bool AddsDexterityModifier { get; init; }

    /// <summary>
    /// The cap on the Dexterity modifier — 2 for Medium armor. Null when uncapped
    /// (Light armor) or when Dexterity does not apply at all.
    /// </summary>
    public int? MaximumDexterityModifier { get; init; }

    /// <summary>
    /// The Strength score required to avoid a 10-foot speed penalty. Null when the
    /// table prints "—".
    /// </summary>
    public int? MinimumStrength { get; init; }

    /// <summary>Whether the wearer has Disadvantage on Dexterity (Stealth) checks.</summary>
    public required bool StealthDisadvantage { get; init; }

    public required decimal WeightPounds { get; init; }

    /// <summary>Cost in copper pieces, so every printed price is exact.</summary>
    public required int CostCopper { get; init; }
}
