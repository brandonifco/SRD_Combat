namespace SRDCombat.Core.Definitions;

/// <summary>
/// A named piece of rules text belonging to a species or background — a species trait
/// such as Darkvision or Dwarven Resilience.
/// </summary>
/// <remarks>
/// <para>
/// Carries the same discipline as <see cref="MonsterEntry"/>: the text is classified,
/// and anything the model cannot express is recorded on
/// <see cref="UnmodelledClauses"/> and counted rather than passing as prose. Species
/// traits are mechanics — Dwarven Resilience is Poison resistance plus Advantage on a
/// save, and a Breath Weapon is a saving-throw effect — so the same rule applies.
/// </para>
/// <para>
/// This and <see cref="MonsterEntry"/> are deliberately separate for now: an entry has
/// a stat block section and attacks, a trait has neither. They should converge on a
/// shared shape once class features arrive and there is a third case to generalise
/// from — two is not enough to see the right abstraction.
/// </para>
/// </remarks>
/// <param name="Name">The trait's name, without its trailing period.</param>
/// <param name="Text">The trait's full prose.</param>
/// <param name="Mechanics">What kind of mechanics it carries. Never absent.</param>
/// <param name="Save">A saving-throw effect, when the trait resolves through one.</param>
/// <param name="Usage">A usage limit, when the trait is limited.</param>
/// <param name="AppliedConditions">Conditions the trait imposes.</param>
/// <param name="UnmodelledClauses">Clauses the model cannot express. Empty when fully modelled.</param>
public sealed record TraitEntry(
    string Name,
    string Text,
    EntryMechanics Mechanics = EntryMechanics.Unmodelled,
    SaveEffect? Save = null,
    UsageLimit? Usage = null,
    IReadOnlyList<AppliedCondition>? AppliedConditions = null,
    IReadOnlyList<string>? UnmodelledClauses = null)
{
    public IReadOnlyList<AppliedCondition> AppliedConditions { get; init; } = AppliedConditions ?? [];

    public IReadOnlyList<string> UnmodelledClauses { get; init; } = UnmodelledClauses ?? [];

    /// <summary>
    /// The level a class grants this feature, read from its printed "Level 3:" heading.
    /// </summary>
    /// <remarks>
    /// Null for a species trait or a background feature, which are not granted by level.
    /// A class feature always has one, and <c>ClassValidator</c> checks that.
    /// </remarks>
    public int? GrantedAtLevel { get; init; }

    /// <summary>True when every mechanical clause in this trait is captured by the model.</summary>
    public bool IsFullyModelled =>
        Mechanics != EntryMechanics.Unmodelled && UnmodelledClauses.Count == 0;
}

/// <summary>A playable species, from the SRD's Character Species section.</summary>
public sealed record SpeciesDefinition
{
    /// <summary>Stable slug — <c>species.dragonborn</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Every species in the SRD is Humanoid, but the block states it, so it is read rather than assumed.</summary>
    public required CreatureType CreatureType { get; init; }

    /// <summary>
    /// The species' size. A couple let the player choose, so both are kept in printed
    /// order and the first is the default.
    /// </summary>
    public required IReadOnlyList<CreatureSize> Sizes { get; init; }

    public required int SpeedFeet { get; init; }

    /// <summary>The special traits the species grants.</summary>
    public required IReadOnlyList<TraitEntry> Traits { get; init; }

    /// <summary>The printed page in SRD 5.2.1 this was extracted from.</summary>
    public required int SourcePage { get; init; }
}

/// <summary>
/// A background, from the SRD's Character Backgrounds section.
/// </summary>
/// <remarks>
/// In the 2024 rules a background is where ability score increases come from — the
/// species grants none — so this is mechanically load-bearing rather than flavour.
/// </remarks>
public sealed record BackgroundDefinition
{
    /// <summary>Stable slug — <c>background.soldier</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The three abilities the background can raise. The player either adds +2 to one
    /// and +1 to another, or +1 to all three; which of those was chosen belongs to the
    /// character, not to the background.
    /// </summary>
    public required IReadOnlyList<Ability> AbilityScores { get; init; }

    /// <summary>The Origin feat the background grants, as printed.</summary>
    public required string FeatName { get; init; }

    /// <summary>The two skills the background grants proficiency in.</summary>
    public required IReadOnlyList<string> SkillProficiencies { get; init; }

    /// <summary>The tool proficiency, as printed. Some are a choice rather than a specific tool.</summary>
    public required string ToolProficiency { get; init; }

    /// <summary>
    /// The equipment line as printed. Deliberately not broken into items: the choice is
    /// "package A, or 50 GP", and the packages name tools and trinkets that have no
    /// definitions in this game yet. Structuring it would imply a precision the content
    /// does not have.
    /// </summary>
    public required string Equipment { get; init; }

    public required int SourcePage { get; init; }
}
