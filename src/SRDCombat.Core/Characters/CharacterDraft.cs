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

/// <summary>
/// One taking of the Ability Score Improvement feat.
/// </summary>
/// <remarks>
/// The printed benefit is "Increase one ability score of your choice by 2, or increase
/// two ability scores of your choice by 1. This feat can't increase an ability score
/// above 20." The shape is expressed so the illegal reading cannot be written down:
/// <see cref="Second"/> absent is the +2, present is the +1/+1, and there is no way to
/// spell "+2 to two abilities".
/// </remarks>
public sealed record AbilityScoreImprovement
{
    /// <summary>The ability raised — by 2 when <see cref="Second"/> is absent, otherwise by 1.</summary>
    public required Ability First { get; init; }

    /// <summary>The second ability raised by 1, when the +1/+1 shape was taken.</summary>
    public Ability? Second { get; init; }
}

/// <summary>
/// One magic item the character has equipped: which printed item, which tier of a
/// variant item, and — for a weapon enchantment — which carried weapon it is bound to.
/// </summary>
/// <remarks>
/// A choice, not a number: what the item does comes from <c>MagicItemRegistry</c> at
/// resolve time, so a save carrying one of these cannot hold an effect that disagrees
/// with the rules. The resolver refuses an item the registry does not execute, a variant
/// the item does not print, and a binding to a weapon the draft does not carry.
/// </remarks>
public sealed record EquippedMagicItem
{
    /// <summary>The item's definition id — <c>magic-item.ring-of-protection</c>.</summary>
    public required string ItemId { get; init; }

    /// <summary>The tier of a variant item — "+2" of "Weapon, +1, +2, or +3".</summary>
    public string? Variant { get; init; }

    /// <summary>For a weapon item: the carried weapon it enchants.</summary>
    public string? BoundWeaponId { get; init; }
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

    /// <summary>
    /// Ids of the kinds of weapon whose mastery property the character has unlocked.
    /// </summary>
    /// <remarks>
    /// "Your training with weapons allows you to use the mastery properties of N kinds of
    /// weapons of your choice." A kind rather than an instance, so this names weapon ids
    /// and not carried items — and the resolver refuses a kind the character has no
    /// feature to unlock, more kinds than the class table grants, and any weapon whose
    /// property <c>WeaponMasteryRules</c> does not execute.
    /// </remarks>
    public IReadOnlyList<string> WeaponMasteryIds { get; init; } = [];

    /// <summary>Id of the armour worn, if any.</summary>
    public string? ArmorId { get; init; }

    /// <summary>Whether a Shield is held. Tracked separately because it stacks with armour.</summary>
    public bool HasShield { get; init; }

    /// <summary>Magic items equipped, in the order they were found.</summary>
    public IReadOnlyList<EquippedMagicItem> MagicItems { get; init; } = [];

    /// <summary>
    /// Ability Score Improvements taken, in the order they were taken.
    /// </summary>
    /// <remarks>
    /// <b>A draft is the character's choices, and resolving it at a level applies the
    /// ones that level has earned.</b> A list longer than the level allows is not an
    /// error — it is the plan for a character who has not got there yet — because the
    /// same draft has to describe the character at every level: levelling in this game
    /// is re-resolving the draft, not editing a sheet. A list shorter than the level
    /// allows is not an error either, since the printed feature is "the Ability Score
    /// Improvement feat <em>or another feat of your choice</em>", and no other feat is
    /// modelled; the shortfall is reported on <c>CharacterSheet.UnspentFeatChoices</c>
    /// rather than being silently forgotten.
    /// </remarks>
    public IReadOnlyList<AbilityScoreImprovement> AbilityScoreImprovements { get; init; } = [];

    /// <summary>
    /// Ids of the spells this caster has chosen, in the order they were chosen.
    /// </summary>
    /// <remarks>
    /// The same reading as <see cref="AbilityScoreImprovements"/>: a draft describes the
    /// character at every level, so the list is the <em>plan</em>, and resolving at a
    /// level prepares the first entries that level's printed columns allow — cantrips
    /// against the Cantrips column, level 1+ spells against Prepared Spells, and a spell
    /// whose level has no slots yet is skipped for now rather than refused, because "must
    /// be of a level for which you have spell slots" is a fact about today and the plan
    /// is about later. What <em>is</em> refused, loudly: an unknown id, a spell not on
    /// this class's printed list, and a spell whose effect the engine does not execute —
    /// a prepared spell that can never be cast would be an unimplemented rule holding
    /// silently. Empty means no choice was made, and a pregenerated loadout stands in.
    /// </remarks>
    public IReadOnlyList<string> ChosenSpellIds { get; init; } = [];
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
