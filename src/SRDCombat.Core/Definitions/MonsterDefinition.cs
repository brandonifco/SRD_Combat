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
/// <param name="Aura">
/// The aura signal, when this entry is a passive per-turn area effect the creature emits —
/// the Ghast's Stench (#670, extractor-recognised by #676). Null for every ordinary entry —
/// every entry but the Ghast's own Stench trait, as of #676 — and set otherwise only by the
/// hand-authored fixtures the engine's own aura tests build for shapes the extractor does
/// not yet classify. See <see cref="AuraEffect"/> for the reading. The entry's
/// <see cref="Save"/> supplies the DC, ability and rider; this signal says the save fires
/// on the aura's clock rather than as a spent action.
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
    IReadOnlyList<string>? UnmodelledClauses = null,
    AuraEffect? Aura = null)
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
    /// A circumstance of this attack's own roll that grants Advantage — the header
    /// parenthetical "(with Advantage if the target …)" printed on nine corpus entries
    /// (#666). Null for the overwhelming majority of attacks, which print no such
    /// parenthetical. See <see cref="AttackRollAdvantageCondition"/> for the two
    /// members and their printed wording.
    /// </summary>
    public AttackRollAdvantageCondition? AdvantageCondition { get; init; }

    /// <summary>
    /// The saving throw a hit forces — the Ghast's Claw: "If the target is a
    /// non-Undead creature, it is subjected to the following effect. Constitution
    /// Saving Throw: DC 10. Failure: The target has the Paralyzed condition until the
    /// end of its next turn." Null for the overwhelming majority of attacks, which
    /// print no embedded save.
    /// </summary>
    public EmbeddedAttackSave? EmbeddedSave { get; init; }

    /// <summary>
    /// A conditional damage tier that replaces some of <see cref="Damage"/> when its own
    /// condition holds — the whole list (#371, the Chimera's Bite, the Blood Hawk's Beak,
    /// every Bloodied-conditioned swarm's bite or sting) or, for the em-dash "or…if…plus"
    /// entries, only the one component it names (#409, its
    /// <see cref="AlternativeAttackDamage.ReplacesComponentIndex"/>). Null for the
    /// overwhelming majority of attacks, which print no alternative tier. See
    /// <see cref="AlternativeAttackDamage"/>'s own remarks for why this is not simply
    /// another <see cref="AttackDamage"/> in <see cref="Damage"/>.
    /// </summary>
    public AlternativeAttackDamage? Alternative { get; init; }

    /// <summary>
    /// True when a hit leaves the target with Disadvantage on its own next attack
    /// roll — the Ettin's Morningstar and the Fire Giant's rock: "the target has
    /// Disadvantage on the next attack roll it makes before the end of its next turn"
    /// (SRD 5.2.1 p. 284). This is a printed rider distinct from the Sap Weapon
    /// Mastery property (p. 90, "before the start of <em>your</em> next turn"): the
    /// wording here names no imposer, every pronoun in the clause is third person, and
    /// the boundary is the <em>end</em> of a turn rather than the start — the same
    /// clause appears verbatim on Vicious Mockery (p. 172), confirming it is the
    /// SRD's standard bearer-clock debuff phrasing rather than a paraphrase of Sap.
    /// "Its next turn" is therefore the <b>bearer's</b> own clock (see
    /// <see cref="ConditionDurationOwner.Bearer"/>'s doc comment for the same reading
    /// applied to conditions) — the target's own Disadvantage expires at the end of
    /// its own next turn, not the ettin's. Reading by designer, #665. False for every
    /// attack that prints no such rider.
    /// </summary>
    public bool ImposesDisadvantageOnTargetsNextAttack { get; init; }
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
/// the creature hit measurably harder than the SRD says it does. The same enum also
/// gates <see cref="AlternativeAttackDamage"/> (#371) — a different shape of
/// conditional entirely (see that type's own remarks for the distinction), but the
/// same closed set of conditions this engine can check at the moment an attack hits.
/// </remarks>
public enum AttackDamageCondition
{
    /// <summary>
    /// The goblins' "plus 2 (1d4) damage if the attack roll had Advantage"; also the
    /// Chimera's "or 18 (4d6 + 4) Piercing damage if the chimera had Advantage on the
    /// attack roll" (#371) — worded differently but the same printed rule, checked the
    /// same way at resolution: <c>AttackRoll.Roll.Mode == RollMode.Advantage</c>.
    /// </summary>
    AttackRollHadAdvantage,

    /// <summary>
    /// The attacking creature's own Bloodied state — the swarms' "or N (dice) damage
    /// if the swarm is Bloodied" (#371). Checked against the attacker, not the target.
    /// </summary>
    AttackerIsBloodied,

    /// <summary>
    /// The target's Bloodied state — the Blood Hawk's Beak: "or 6 (1d8 + 2) Piercing
    /// damage if the target is Bloodied" (#371).
    /// </summary>
    TargetIsBloodied,

    /// <summary>
    /// The target being Grappled <em>by the attacker</em> — the Mimic's Bite: "or 12
    /// (2d8 + 3) Piercing damage if the target is Grappled by the mimic" (#409). Checked
    /// against the source of the target's Grappled condition, not merely whether it is
    /// Grappled at all: a target held by someone else does not trigger the mimic's
    /// heavier bite.
    /// </summary>
    TargetIsGrappledByAttacker,
}

/// <summary>
/// A circumstance of an attack's own roll — as opposed to its damage — that grants
/// Advantage. Printed as the header parenthetical "(with Advantage if the target …)"
/// on nine corpus entries (#666, SRD 5.2.1): four print <see
/// cref="TargetIsGrappledByAttacker"/> and four print
/// <see cref="TargetIsMissingHitPoints"/>. A ninth entry, the Doppelganger's Slam —
/// "(with Advantage during the first round of each combat)", p. 280 right column —
/// prints a third, different parenthetical in the same header slot: a predicate over
/// the encounter clock rather than over attacker/target state, which this enum
/// deliberately does not have a member for. It stays honest residue; do not widen this
/// type to reach it. (The design conversation that named this slice's ninth entry
/// cited the Djinni, on the facing column of the same page — the Djinni's own Slam
/// does not exist and its printed attacks, Storm Blade and Storm Bolt, carry no such
/// parenthetical; verified against the PDF, corrected here.)
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not a member of <see cref="AttackDamageCondition"/></b>, even
/// though that enum already has the grapple predicate this one needs (
/// <see cref="AttackDamageCondition.TargetIsGrappledByAttacker"/>). That enum also
/// carries <see cref="AttackDamageCondition.AttackRollHadAdvantage"/>, which would be
/// circular if attached to the very roll it describes — "Advantage if the roll had
/// Advantage" is not an expressible printed rule, and a shared enum would let content
/// say it anyway. A separate closed set keeps the nonsensical value unrepresentable,
/// the same reasoning that keeps <see cref="AlternativeAttackDamage"/> a separate
/// shape from <see cref="AttackDamage.Condition"/>.
/// </para>
/// <para>
/// <b>Evaluated per roll, from live state, nothing stored.</b> This is a fact about
/// the instant of one attack roll — <see cref="SRDCombat.Core.Rules.AttackRules.DescribeCircumstances"/>
/// reads it fresh every time — not a condition the engine imposes or a duration that
/// expires. The Ankheg's first Bite on an ungrappled target rolls normally; the hit
/// then grapples the target; the Ankheg's <em>next</em> Bite rolls with Advantage. A
/// shark's first Bite against a full-Hit-Point target rolls normally; if it hits, the
/// shark's next Bite (the second swing of its own Multiattack, or a later turn) rolls
/// with Advantage. Escape, the grappler's death or a heal back to full stops it on the
/// very next roll — there is no state to clear because none was ever recorded.
/// </para>
/// </remarks>
public enum AttackRollAdvantageCondition
{
    /// <summary>
    /// The target is Grappled by <em>this attacker</em> — Ankheg's Bite ("with
    /// Advantage if the target is Grappled by the ankheg", p. 259), Bugbear Stalker's
    /// Morningstar ("… by the bugbear", p. 271), Bugbear Warrior's Light Hammer
    /// ("… by the bugbear", p. 272) and Mimic's Bite ("… by the mimic", p. 309).
    /// Checked the same way — and by the same shared predicate — as
    /// <see cref="AttackDamageCondition.TargetIsGrappledByAttacker"/>: the source of
    /// the target's Grappled condition must be this attacker, not merely that it is
    /// Grappled by anyone. A target held by an ally grants nothing.
    /// </summary>
    TargetIsGrappledByAttacker,

    /// <summary>
    /// The target "doesn't have all its Hit Points" — Giant Shark's Bite (p. 353),
    /// Hunter Shark's Bite (p. 356), Piranha's Bite (p. 358) and Swarm of Piranhas'
    /// Bites (p. 362), all worded identically.
    /// </summary>
    /// <remarks>
    /// <b>Not Bloodied.</b> Glossary p. 177: "A creature is Bloodied while it has half
    /// its Hit Points or fewer remaining." "Doesn't have all its Hit Points" is any
    /// shortfall at all — <c>CurrentHitPoints &lt; MaximumHitPoints</c>, one point of
    /// damage is enough — a strictly wider gate than Bloodied. Temporary Hit Points do
    /// not enter it either way: glossary p. 190 calls them "a buffer against losing
    /// real Hit Points", not Hit Points themselves, so a full-HP creature carrying
    /// temporary Hit Points still has all its (real) Hit Points, and a
    /// below-maximum creature carrying them still doesn't. See
    /// <see cref="SRDCombat.Core.Combat.Combatant.IsMissingHitPoints"/>, which this reads.
    /// </remarks>
    TargetIsMissingHitPoints,
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

/// <summary>
/// A damage tier that <em>replaces</em> one of an attack's damage components when
/// <see cref="Condition"/> holds — "Hit: 11 (2d6 + 4) Piercing damage, or 18
/// (4d6 + 4) Piercing damage if the chimera had Advantage on the attack roll" (#371).
/// </summary>
/// <remarks>
/// Deliberately a separate shape from <see cref="AttackDamage.Condition"/>, which
/// <em>adds</em> a rider component on top of unconditional damage rather than
/// replacing it — the goblins' Advantage-conditional bonus damage is dealt alongside
/// the base hit, never instead of it. An "or…if" alternative and a "plus…if" rider
/// read as opposite grammar and this type keeps them opposite in the model: printed
/// "or" replaces, printed "plus" adds.
/// <para>
/// <b>Which of the attack's components the alternative replaces</b> is
/// <see cref="ReplacesComponentIndex"/>. Null — the shape #371 structures for its
/// eight entries, each of which prints exactly one base component and nothing else —
/// means the alternative replaces <see cref="MonsterAttack.Damage"/> whole. A non-null
/// index means it replaces only that one component, leaving the attack's other
/// components (an unconditional "plus" tail) untouched. For a single-component attack
/// the two are identical, so #371's eight entries keep <see cref="ReplacesComponentIndex"/>
/// null and their behaviour and serialization are byte-for-byte unchanged.
/// </para>
/// <para>
/// <b>The corpus prints two entries that need the non-null form</b> (#409) — an
/// em-dash-joined "or…if…plus" chain the design doc counts among its twelve or-tiers
/// (not ten; docs/2026-08-24-span-accounting-design.md §11.1): the Mimic's Bite
/// ("7 (1d8 + 3) Piercing damage—or 12 (2d8 + 3) Piercing damage if the target is
/// Grappled by the mimic—plus 4 (1d8) Acid damage", SRD 5.2.1 p. 309) and Swarm of
/// Venomous Snakes' Bites ("8 (1d8 + 4) Piercing damage—or 6 (1d4 + 4) Piercing damage
/// if the swarm is Bloodied—plus 10 (3d6) Poison damage", p. 363). Both read as: the
/// Piercing component alone alternates on its own condition, and a second damage type
/// is added unconditionally regardless of which Piercing tier applies — a Bloodied
/// Swarm of Venomous Snakes deals 6 Piercing <em>plus</em> 10 Poison, never 6 alone
/// and never the Poison dropped. The Piercing base sits at index 0 of
/// <see cref="MonsterAttack.Damage"/>, the unconditional Acid/Poison "plus" at index 1,
/// and <see cref="ReplacesComponentIndex"/> is 0: <see cref="AttackRules.RollDamage"/>
/// swaps the Piercing tier when the condition holds while always rolling the "plus".
/// </para>
/// </remarks>
/// <param name="Amount">The alternative's damage dice.</param>
/// <param name="Type">The alternative's damage type — always the same type as the base component in the corpus, but read independently rather than assumed.</param>
/// <param name="PrintedAverage">The average the SRD prints in front of the alternative's dice.</param>
/// <param name="Condition">What must be true for the alternative to replace the base damage.</param>
public sealed record AlternativeAttackDamage(
    DiceExpression Amount,
    DamageType Type,
    int PrintedAverage,
    AttackDamageCondition Condition)
{
    /// <summary>
    /// The index into <see cref="MonsterAttack.Damage"/> of the single component this
    /// alternative replaces when its condition holds. Null — the common #371 case —
    /// means it replaces the whole <see cref="MonsterAttack.Damage"/> list, which is
    /// identical to replacing index 0 for the single-component attacks #371 structures.
    /// Set to a concrete index only for the em-dash "or…if…plus" entries (#409), whose
    /// unconditional "plus" component must survive the swap. See this type's own remarks.
    /// </summary>
    public int? ReplacesComponentIndex { get; init; }
}

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

    /// <summary>
    /// The Legendary Actions section's own printed preamble — <c>Legendary Action
    /// Uses: 3 (4 in Lair).</c> — kept on the monster rather than folded into whichever
    /// entry happened to be open when the "Legendary Actions" header appeared (#423).
    /// Null for the 300 monsters with no Legendary Actions section. The rest of the
    /// preamble sentence ("Immediately after another creature's turn, ... can expend a
    /// use...") is the legendary-action-economy rule itself, identical on every
    /// instance in the corpus, so only the counts are kept.
    /// </summary>
    public int? LegendaryActionUses { get; init; }

    /// <summary>
    /// The larger per-round use count some legendary creatures get in their lair, from
    /// the same preamble's "(4 in Lair)" clause. Null when the block prints no lair
    /// figure — including every monster with no Legendary Actions section.
    /// </summary>
    public int? LegendaryActionUsesInLair { get; init; }

    /// <summary>Traits, actions, bonus actions, reactions and legendary actions, in printed order.</summary>
    public required IReadOnlyList<MonsterEntry> Entries { get; init; }

    /// <summary>The printed page in SRD 5.2.1 this block was extracted from.</summary>
    public required int SourcePage { get; init; }
}
