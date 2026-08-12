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

/// <summary>
/// A Fighting Style feat, taken by a Fighter at level 1 and a Ranger at level 2.
/// </summary>
/// <remarks>
/// <para>
/// Only the two the engine executes are named here, and for the usual reason: a style
/// this list offered but did not apply would be a choice that silently did nothing. The
/// rest of the printed styles reference machinery that does not exist — Great Weapon
/// Fighting needs per-die rerolls, Two-Weapon Fighting needs off-hand attacks, Blind
/// Fighting needs Blindsight — and a character wanting one takes
/// <see cref="Unspecified"/> and carries the printed feature as unimplemented, exactly
/// as it did before this choice existed.
/// </para>
/// <para>
/// The SRD lets a Fighter swap this feat on gaining a level. Nothing here models
/// levelling in play, so the choice is fixed at build time.
/// </para>
/// </remarks>
public enum FightingStyle
{
    /// <summary>No style chosen, or one the engine does not execute.</summary>
    Unspecified,

    /// <summary>"You gain a +2 bonus to attack rolls you make with Ranged weapons."</summary>
    Archery,

    /// <summary>
    /// "While you're wearing Light, Medium, or Heavy armor, you gain a +1 bonus to
    /// Armor Class."
    /// </summary>
    Defense,
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

    /// <summary>
    /// Skills taken with Expertise, which doubles the proficiency bonus on them.
    /// </summary>
    /// <remarks>
    /// One list for every source of Expertise, because the rule is the same wherever it
    /// comes from and the sources differ only in how many picks they grant — a Rogue's
    /// two at level 1 and two more at 6, a Ranger's one from Deft Explorer. How many the
    /// character is entitled to is checked against the class and level by
    /// <see cref="CharacterResolver"/>; Expertise on a skill the character is not
    /// proficient in is refused there too, since the SRD grants it "in your skill
    /// proficiencies".
    /// </remarks>
    public IReadOnlyList<string> ExpertiseSkills { get; init; } = [];

    /// <summary>
    /// The Fighting Style feat taken, for classes that grant one.
    /// </summary>
    /// <remarks>
    /// <see cref="FightingStyle.Unspecified"/> is the honest default: a character may
    /// legitimately have taken a printed style the engine does not execute, and the
    /// feature then stays reported on <c>CharacterSheet.UnimplementedFeatures</c>.
    /// </remarks>
    public FightingStyle FightingStyle { get; init; } = FightingStyle.Unspecified;

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
