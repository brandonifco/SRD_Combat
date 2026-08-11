using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Definitions;

/// <summary>Simple or Martial. Proficiency is usually granted by category.</summary>
public enum WeaponCategory
{
    Simple,
    Martial,
}

/// <summary>Whether the weapon's own attacks are melee or ranged.</summary>
public enum WeaponKind
{
    Melee,
    Ranged,
}

/// <summary>
/// A weapon's properties. <c>Ammunition</c>, <c>Thrown</c> and <c>Versatile</c> carry
/// extra data, held on <see cref="WeaponDefinition"/> rather than here.
/// </summary>
[Flags]
public enum WeaponProperty
{
    None = 0,
    Ammunition = 1 << 0,
    Finesse = 1 << 1,
    Heavy = 1 << 2,
    Light = 1 << 3,
    Loading = 1 << 4,
    Reach = 1 << 5,
    Thrown = 1 << 6,
    TwoHanded = 1 << 7,
    Versatile = 1 << 8,
}

/// <summary>
/// The SRD 5.2.1 mastery properties. These are most of what gives a martial character
/// a decision to make on their turn, so they are modelled from the start rather than
/// deferred.
/// </summary>
public enum WeaponMastery
{
    Cleave,
    Graze,
    Nick,
    Push,
    Sap,
    Slow,
    Topple,
    Vex,
}

/// <summary>A normal/long range band in feet. Beyond normal range the attack has Disadvantage.</summary>
public sealed record WeaponRange(int NormalFeet, int LongFeet);

/// <summary>A weapon from the SRD 5.2.1 Weapons table.</summary>
public sealed record WeaponDefinition
{
    /// <summary>Stable slug — <c>weapon.heavy-crossbow</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required WeaponCategory Category { get; init; }

    public required WeaponKind Kind { get; init; }

    /// <summary>
    /// Damage dice. Carries no ability modifier — that comes from the wielder. The
    /// Blowgun's flat <c>1</c> is a zero-dice expression.
    /// </summary>
    public required DiceExpression Damage { get; init; }

    public required DamageType DamageType { get; init; }

    public required WeaponProperty Properties { get; init; }

    /// <summary>
    /// Damage when wielded in two hands. Non-null exactly when
    /// <see cref="WeaponProperty.Versatile"/> is set.
    /// </summary>
    public DiceExpression? VersatileDamage { get; init; }

    /// <summary>
    /// The range band from the Ammunition or Thrown property. Non-null exactly when one
    /// of those properties is set.
    /// </summary>
    public WeaponRange? Range { get; init; }

    /// <summary>
    /// What the weapon fires — "Bolt", "Arrow", "Bullet", "Needle". Non-null exactly
    /// when <see cref="WeaponProperty.Ammunition"/> is set.
    /// </summary>
    public string? AmmunitionKind { get; init; }

    public required WeaponMastery Mastery { get; init; }

    /// <summary>Weight in pounds. The Sling's printed "—" is zero.</summary>
    public required decimal WeightPounds { get; init; }

    /// <summary>Cost in copper pieces, so every printed CP/SP/GP price is exact.</summary>
    public required int CostCopper { get; init; }

    /// <summary>
    /// The Lance's "Two-Handed (unless mounted)" qualifier, and anything else the
    /// property list annotates in prose. Null when the properties are unqualified.
    /// </summary>
    public string? PropertyNote { get; init; }
}
