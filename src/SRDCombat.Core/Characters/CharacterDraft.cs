using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Characters;

/// <summary>How a background's ability score increases were spent.</summary>
/// <remarks>
/// In the 2024 rules the background grants the increases, not the species. The player
/// picks one of two shapes: +2 and +1 across two of the three listed abilities, or +1
/// to all three.
/// </remarks>
public enum AbilityIncreaseChoice
{
    /// <summary>+2 to one ability and +1 to another.</summary>
    TwoAndOne,

    /// <summary>+1 to each of the background's three abilities.</summary>
    OneEach,
}

/// <summary>Everything the player chose, before any rules are applied.</summary>
/// <remarks>
/// Deliberately just choices — no derived numbers. Every AC, hit point total and attack
/// bonus is computed by <see cref="CharacterResolver"/>, so a draft cannot hold a value
/// that disagrees with the rules.
/// </remarks>
public sealed record CharacterDraft
{
    public required string Name { get; init; }

    public required string SpeciesId { get; init; }

    public required string ClassId { get; init; }

    public required string BackgroundId { get; init; }

    /// <summary>Class level, 1–5 at this game's scope.</summary>
    public required int Level { get; init; }

    /// <summary>Ability scores before the background's increases.</summary>
    public required IReadOnlyDictionary<Ability, int> BaseAbilityScores { get; init; }

    /// <summary>Which shape of background increase was taken.</summary>
    public AbilityIncreaseChoice IncreaseChoice { get; init; } = AbilityIncreaseChoice.TwoAndOne;

    /// <summary>The ability taking +2, when <see cref="AbilityIncreaseChoice.TwoAndOne"/>.</summary>
    public Ability? PrimaryIncrease { get; init; }

    /// <summary>The ability taking +1, when <see cref="AbilityIncreaseChoice.TwoAndOne"/>.</summary>
    public Ability? SecondaryIncrease { get; init; }

    /// <summary>Skills chosen from the class's list.</summary>
    public IReadOnlyList<string> ChosenSkills { get; init; } = [];

    /// <summary>Ids of weapons the character is carrying.</summary>
    public IReadOnlyList<string> WeaponIds { get; init; } = [];

    /// <summary>Id of the armour worn, if any.</summary>
    public string? ArmorId { get; init; }

    /// <summary>Whether a Shield is held. Tracked separately because it stacks with armour.</summary>
    public bool HasShield { get; init; }
}

/// <summary>How hit points are determined on levelling.</summary>
public enum HitPointMethod
{
    /// <summary>
    /// The SRD's fixed value — the hit die's average, rounded up. The default here
    /// because the whole engine is built to be reproducible from a seed.
    /// </summary>
    Average,

    /// <summary>Roll the hit die.</summary>
    Rolled,
}
