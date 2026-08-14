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
/// (<c>Melee Attack Roll: +6, reach 5 ft. Hit: 10 (2d6 + 3) Piercing damage.</c>).
/// </param>
/// <param name="Mechanics">
/// What kind of mechanics this entry carries. Never absent: an entry the model cannot
/// express is <see cref="EntryMechanics.Unmodelled"/> and is counted, rather than
/// passing as ordinary prose.
/// </param>
/// <param name="Save">The saving-throw effect, when the entry resolves through one.</param>
/// <param name="Multiattack">The Multiattack, when the entry is one.</param>
/// <param name="Reaction">The Trigger/Response pair, when the entry is a reaction.</param>
/// <param name="Usage">How often the entry can be used, from "(Recharge 5-6)" or "(3/Day)".</param>
/// <param name="AppliedConditions">
/// Conditions the entry imposes — the riders that hang off an attack or a failed save.
/// </param>
/// <param name="UnmodelledClauses">
/// The sentences whose mechanics were recognised as real but could not be expressed.
/// Empty for anything fully modelled. This is what makes the gap countable.
/// </param>
public sealed record MonsterEntry(
    string Name,
    MonsterEntrySection Section,
    string Text,
    MonsterAttack? Attack = null,
    EntryMechanics Mechanics = EntryMechanics.Unmodelled,
    SaveEffect? Save = null,
    MultiattackEffect? Multiattack = null,
    ReactionEffect? Reaction = null,
    UsageLimit? Usage = null,
    IReadOnlyList<AppliedCondition>? AppliedConditions = null,
    IReadOnlyList<string>? UnmodelledClauses = null)
{
    /// <summary>Conditions this entry imposes. Never null.</summary>
    public IReadOnlyList<AppliedCondition> AppliedConditions { get; init; } = AppliedConditions ?? [];

    /// <summary>Clauses recognised as mechanical but not expressible by the model. Never null.</summary>
    public IReadOnlyList<string> UnmodelledClauses { get; init; } = UnmodelledClauses ?? [];

    /// <summary>
    /// True when every mechanical clause in this entry is captured by the model. False
    /// means the engine will not do everything the stat block says.
    /// </summary>
    public bool IsFullyModelled =>
        Mechanics != EntryMechanics.Unmodelled && UnmodelledClauses.Count == 0;
}

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
    IReadOnlyList<AttackDamage> Damage)
{
    /// <summary>
    /// The saving throw a hit forces — the Ghast's Claw: "If the target is a
    /// non-Undead creature, it is subjected to the following effect. Constitution
    /// Saving Throw: DC 10. Failure: The target has the Paralyzed condition until the
    /// end of its next turn." Null for the overwhelming majority of attacks, which
    /// print no embedded save.
    /// </summary>
    public EmbeddedAttackSave? EmbeddedSave { get; init; }
}

/// <summary>
/// A saving throw rolled when an attack hits, with the printed gate on who rolls it.
/// </summary>
/// <remarks>
/// Structured only when every part is expressible: the gate must be a creature-type
/// exclusion the stats carry ("non-Undead creature"), and the failure a rider the
/// model imposes to the letter. The Ghoul's Claw is one word beyond that bar — "isn't
/// an Undead or elf" names a species, which no combatant carries — and stays refused
/// with its sentence counted, exactly as it was before the Ghast's could ride.
/// </remarks>
/// <param name="Save">The save, with its printed DC and the failure's riders.</param>
/// <param name="ExcludedTargetType">
/// The creature type the printed gate exempts — Undead for the Ghast. Null when
/// everyone rolls.
/// </param>
public sealed record EmbeddedAttackSave(SaveEffect Save, CreatureType? ExcludedTargetType);

/// <summary>
/// A condition that must hold for a damage component to apply at all.
/// </summary>
/// <remarks>
/// Most extra damage is unconditional — a Flame Whip simply deals Force damage plus
/// Fire damage. A few attacks qualify theirs, and treating those as unconditional makes
/// the creature hit measurably harder than the SRD says it does.
/// </remarks>
public enum AttackDamageCondition
{
    /// <summary>The goblins' "plus 2 (1d4) damage if the attack roll had Advantage".</summary>
    AttackRollHadAdvantage,
}

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
/// <param name="Condition">
/// What must be true for this component to be dealt at all. Null — the overwhelmingly
/// common case — means it always applies.
/// </param>
public sealed record AttackDamage(
    DiceExpression Amount,
    DamageType Type,
    int PrintedAverage,
    AttackDamageCondition? Condition = null);

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
