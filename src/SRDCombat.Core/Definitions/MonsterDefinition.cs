using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Definitions;

/// <summary>
/// One ability's line in a stat block: the score, and the saving throw bonus the
/// block prints. The modifier is derived rather than stored — the SRD prints it, but
/// it is always <c>(score - 10) / 2</c> rounded down, so storing it would let a bad
/// extraction disagree with itself silently. <see cref="Modifier"/> is what the game
/// uses; the printed value is only ever a check on extraction.
/// </summary>
/// <param name="Score">The ability score, 1–30.</param>
/// <param name="SaveBonus">
/// The stat block's SAVE column. Not derivable — a proficient save adds the
/// proficiency bonus, and some creatures have bonuses from neither source.
/// </param>
public sealed record MonsterAbility(int Score, int SaveBonus)
{
    /// <summary>The ability modifier, derived from <see cref="Score"/>.</summary>
    public int Modifier => (int)Math.Floor((Score - 10) / 2.0);
}

/// <summary>Which part of a stat block an entry appeared under.</summary>
public enum MonsterEntrySection
{
    /// <summary>Always-on features, printed above the Actions header.</summary>
    Trait,
    Action,
    BonusAction,
    Reaction,
    LegendaryAction,
}

/// <summary>
/// A named entry in a stat block — a trait, action, bonus action, reaction or
/// legendary action.
/// </summary>
/// <param name="Name">The entry's name, without its trailing period.</param>
/// <param name="Section">Which stat block section it appeared under.</param>
/// <param name="Text">The entry's full prose, with line breaks joined.</param>
/// <param name="Attack">
/// Structured attack data when the entry uses the SRD's attack grammar
/// (<c>Melee Attack Roll: +6, reach 5 ft. Hit: 10 (2d6 + 3) Piercing damage.</c>),
/// otherwise null. Entries that resolve through a saving throw, and entries with no
/// mechanical payload at all, both leave this null and keep their prose in
/// <paramref name="Text"/>.
/// </param>
public sealed record MonsterEntry(
    string Name,
    MonsterEntrySection Section,
    string Text,
    MonsterAttack? Attack = null);

/// <summary>Whether an attack is made in melee or at range.</summary>
public enum AttackKind
{
    Melee,
    Ranged,
}

/// <summary>
/// The mechanical part of an attack entry. Rider effects ("the target has the Prone
/// condition") stay in the owning <see cref="MonsterEntry.Text"/> — this carries only
/// what resolution needs to roll the attack and its damage.
/// </summary>
/// <param name="Kind">Melee or ranged.</param>
/// <param name="AttackBonus">The bonus added to the d20 attack roll.</param>
/// <param name="ReachFeet">Melee reach in feet. Null for a purely ranged attack.</param>
/// <param name="NormalRangeFeet">Normal range in feet. Null for a purely melee attack.</param>
/// <param name="LongRangeFeet">
/// Long range in feet — attacks beyond normal range but within this have
/// Disadvantage. Null when the attack has no long range band.
/// </param>
/// <param name="Damage">Damage components, in the order the SRD prints them.</param>
public sealed record MonsterAttack(
    AttackKind Kind,
    int AttackBonus,
    int? ReachFeet,
    int? NormalRangeFeet,
    int? LongRangeFeet,
    IReadOnlyList<AttackDamage> Damage);

/// <summary>
/// One damage component of an attack. An attack may have several — a Flame Whip deals
/// Force damage "plus" Fire damage, and each is rolled separately.
/// </summary>
/// <param name="Amount">The damage dice.</param>
/// <param name="Type">The damage type.</param>
/// <param name="PrintedAverage">
/// The average the SRD prints in front of the dice. Kept because it is the single
/// best check that the dice were extracted correctly — it must equal
/// <see cref="DiceExpression.Average"/>.
/// </param>
public sealed record AttackDamage(DiceExpression Amount, DamageType Type, int PrintedAverage);

/// <summary>A special sense and how far it reaches.</summary>
public sealed record MonsterSense(SenseType Type, int RangeFeet);

/// <summary>
/// A complete monster stat block, as printed in SRD 5.2.1's Monsters and Animals
/// sections.
/// </summary>
public sealed record MonsterDefinition
{
    /// <summary>Stable slug, derived from the name — <c>monster.bandit-captain</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The name as printed.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The creature's size. A handful of stat blocks print two ("Medium or Small
    /// Humanoid"); both are kept, in printed order, and the first is the default.
    /// </summary>
    public required IReadOnlyList<CreatureSize> Sizes { get; init; }

    public required CreatureType Type { get; init; }

    /// <summary>
    /// The parenthesised tag after the type — "Demon" in "Fiend (Demon)", "Chromatic"
    /// in "Dragon (Chromatic)". Null when the block prints no tag.
    /// </summary>
    public string? Subtype { get; init; }

    /// <summary>
    /// Alignment exactly as printed. Deliberately a string rather than an enum: it has
    /// no mechanical effect at this game's scope, and the printed values vary more than
    /// an enum would capture ("Unaligned", "Any Alignment", "Neutral Evil").
    /// </summary>
    public required string Alignment { get; init; }

    public required int ArmorClass { get; init; }

    /// <summary>The stat block's Initiative bonus, from its <c>Initiative +2 (12)</c> line.</summary>
    public required int InitiativeBonus { get; init; }

    /// <summary>Average hit points — the number the game actually starts the creature at.</summary>
    public required int HitPoints { get; init; }

    /// <summary>The hit dice behind <see cref="HitPoints"/>, e.g. <c>2d8 + 2</c>.</summary>
    public required DiceExpression HitDice { get; init; }

    /// <summary>Movement speeds in feet by mode. Always contains <see cref="MovementMode.Walk"/>.</summary>
    public required IReadOnlyDictionary<MovementMode, int> Speeds { get; init; }

    /// <summary>True when the creature's fly speed is annotated "(hover)".</summary>
    public bool CanHover { get; init; }

    public required IReadOnlyDictionary<Ability, MonsterAbility> Abilities { get; init; }

    /// <summary>Skill bonuses by skill name as printed, e.g. <c>Perception</c> to <c>+4</c>.</summary>
    public required IReadOnlyDictionary<string, int> Skills { get; init; }

    /// <summary>Damage types the creature resists, is immune to, or is vulnerable to.</summary>
    public required IReadOnlyDictionary<DamageType, DamageResponse> DamageResponses { get; init; }

    /// <summary>Conditions the creature cannot be given.</summary>
    public required IReadOnlyList<ConditionType> ConditionImmunities { get; init; }

    public required IReadOnlyList<MonsterSense> Senses { get; init; }

    public required int PassivePerception { get; init; }

    public required IReadOnlyList<string> Languages { get; init; }

    /// <summary>The block's Gear line — equipment the creature carries, as printed names.</summary>
    public required IReadOnlyList<string> Gear { get; init; }

    /// <summary>
    /// Challenge rating. Fractional ratings are real (1/8, 1/4, 1/2), so this is a
    /// decimal rather than an integer.
    /// </summary>
    public required decimal ChallengeRating { get; init; }

    /// <summary>
    /// Experience awarded for defeating the creature. This is what the encounter
    /// builder spends against the SRD's XP budget, so it is load-bearing rather than
    /// decorative.
    /// </summary>
    public required int ExperiencePoints { get; init; }

    /// <summary>
    /// The larger XP value some legendary creatures are worth in their lair, from
    /// <c>CR 14 (XP 11,500, or 13,000 in lair; PB +5)</c>. Null for everything else.
    /// </summary>
    public int? LairExperiencePoints { get; init; }

    public required int ProficiencyBonus { get; init; }

    /// <summary>Traits, actions, bonus actions, reactions and legendary actions, in printed order.</summary>
    public required IReadOnlyList<MonsterEntry> Entries { get; init; }

    /// <summary>The printed page in SRD 5.2.1 this block was extracted from.</summary>
    public required int SourcePage { get; init; }
}
