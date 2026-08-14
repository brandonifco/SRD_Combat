using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// An attack a combatant can make. Mirrors <see cref="MonsterAttack"/> but carries its
/// own name and is not tied to a stat block, so a player character's weapon attack can
/// be expressed the same way in Phase 2.
/// </summary>
public sealed record CombatAttack(
    string Name,
    AttackKind Kind,
    int AttackBonus,
    int? ReachFeet,
    int? NormalRangeFeet,
    int? LongRangeFeet,
    IReadOnlyList<AttackDamage> Damage,
    IReadOnlyList<AppliedCondition>? AppliedConditions = null)
{
    /// <summary>
    /// Conditions a hit imposes. Never null, and usually empty — a weapon attack has no
    /// rider, and a monster's is only carried here when the model expresses all of it.
    /// </summary>
    public IReadOnlyList<AppliedCondition> AppliedConditions { get; init; } = AppliedConditions ?? [];

    /// <summary>
    /// The weapon's mastery property, when the wielder has unlocked it.
    /// </summary>
    /// <remarks>
    /// Null unless the character both carries a weapon with a mastery and has taken
    /// mastery of that kind of weapon — the property is "usable only by a character who
    /// has a feature ... that unlocks the property", so a locked one never reaches the
    /// attack at all.
    /// </remarks>
    public WeaponMastery? Mastery { get; init; }

    /// <summary>
    /// The ability modifier folded into this attack's bonus and damage, which Graze and
    /// Topple both need in their own right.
    /// </summary>
    public int AbilityModifier { get; init; }

    /// <summary>
    /// The saving throw a hit forces, carried whole from the stat block — see
    /// <see cref="EmbeddedAttackSave"/>. Null for nearly every attack.
    /// </summary>
    public EmbeddedAttackSave? EmbeddedSave { get; init; }

    /// <summary>
    /// True when a hit marks the target so that the next attack roll against it has
    /// Advantage — Guiding Bolt's rider, carried from the spell onto the attack the
    /// cast builds. False for every weapon and every stat-block attack.
    /// </summary>
    public bool GrantsAdvantageAgainstTargetOnHit { get; init; }

    /// <summary>The furthest this attack can reach at all, in feet.</summary>
    public int MaximumRangeFeet =>
        Math.Max(ReachFeet ?? 0, LongRangeFeet ?? NormalRangeFeet ?? 0);

    /// <summary>
    /// True when a target at this distance is beyond normal range but still reachable —
    /// the SRD's long-range band, which imposes Disadvantage.
    /// </summary>
    public bool IsAtLongRange(int distanceFeet) =>
        NormalRangeFeet is { } normal
        && LongRangeFeet is { } far
        && distanceFeet > normal
        && distanceFeet <= far
        // A dual-mode attack used inside its melee reach is not at long range.
        && distanceFeet > (ReachFeet ?? 0);

    /// <summary>True when the attack can be made against a target this far away.</summary>
    public bool CanReach(int distanceFeet) => distanceFeet <= MaximumRangeFeet;

    /// <summary>
    /// True when using this attack at this distance makes a <em>ranged</em> attack roll,
    /// which is what "Ranged Attacks in Close Combat" (printed page 15) hangs on.
    /// </summary>
    /// <remarks>
    /// A pure ranged attack rolls ranged at any distance — a Shortbow fired at an
    /// adjacent target is still a ranged attack roll, which is exactly when the printed
    /// Disadvantage bites. A dual-mode attack ("Melee or Ranged Attack Roll") used
    /// inside its melee reach is read as the melee use, the same reading
    /// <see cref="IsAtLongRange"/> already makes; beyond its reach only the ranged use
    /// remains. A melee-only attack never rolls ranged.
    /// </remarks>
    public bool IsRangedAttackRoll(int distanceFeet) =>
        NormalRangeFeet is not null && distanceFeet > (ReachFeet ?? 0);
}

/// <summary>
/// The character-only part of a combatant: implemented class features and the numbers
/// they run on.
/// </summary>
/// <remarks>
/// Null for a monster. Kept as its own record rather than as a dozen nullable fields on
/// <see cref="CombatantStats"/>, so "is this a character?" is one question with one
/// answer rather than a scattering of defaults that happen to mean nothing for monsters.
/// </remarks>
/// <param name="Features">Implemented features the character has.</param>
/// <param name="AttacksPerAction">Attacks made with one Attack action. Two with Extra Attack.</param>
/// <param name="SneakAttackDamage">The Rogue's Sneak Attack dice at this level.</param>
/// <param name="RageDamageBonus">Bonus damage on Strength melee attacks while raging.</param>
/// <param name="RageUses">Rages per long rest.</param>
/// <param name="SecondWindUses">Second Wind uses per rest.</param>
/// <param name="ActionSurgeUses">Action Surge uses per rest.</param>
/// <param name="Level">Character level, which several features scale on.</param>
/// <param name="Spells">Spells the character can cast, by id.</param>
/// <param name="SpellSlots">Spell slots by spell level, as the class table grants them.</param>
/// <param name="SpellcastingAbility">The ability the character casts with. Null for a non-caster.</param>
/// <param name="SpellSaveDifficultyClass">The DC a target must beat to resist this caster's spells.</param>
/// <param name="SpellAttackBonus">The bonus added to this caster's spell attack rolls.</param>
/// <param name="ChannelDivinityUses">Channel Divinity uses per rest arc, from the class table's column.</param>
public sealed record CombatantFeatures(
    IReadOnlyList<ClassFeature> Features,
    int AttacksPerAction,
    DiceExpression? SneakAttackDamage,
    int RageDamageBonus,
    int RageUses,
    int SecondWindUses,
    int ActionSurgeUses,
    int Level,
    IReadOnlyList<SpellDefinition>? Spells = null,
    IReadOnlyDictionary<int, int>? SpellSlots = null,
    Ability? SpellcastingAbility = null,
    int SpellSaveDifficultyClass = 0,
    int SpellAttackBonus = 0,
    int ChannelDivinityUses = 0)
{
    public bool Has(ClassFeature feature) => Features.Contains(feature);

    /// <summary>Spells the character can cast. Never null.</summary>
    public IReadOnlyList<SpellDefinition> Spells { get; init; } = Spells ?? [];

    /// <summary>Slots the class table grants at this level. Never null.</summary>
    public IReadOnlyDictionary<int, int> SpellSlots { get; init; } =
        SpellSlots ?? new Dictionary<int, int>();

    /// <summary>True when the character can cast at all.</summary>
    public bool CanCast => SpellcastingAbility is not null && Spells.Count > 0;
}

/// <summary>
/// A combatant's unchanging statistics, resolved before the fight starts.
/// </summary>
/// <param name="ArmorClass">The number an attack roll must meet or beat.</param>
/// <param name="MaximumHitPoints">Starting and maximum hit points.</param>
/// <param name="SpeedFeet">Walking speed in feet.</param>
/// <param name="InitiativeBonus">The bonus added to the initiative roll.</param>
/// <param name="Abilities">Ability scores and saving throw bonuses.</param>
/// <param name="ProficiencyBonus">The creature's proficiency bonus.</param>
/// <param name="Size">Creature size.</param>
/// <param name="DamageResponses">Resistances, immunities and vulnerabilities by damage type.</param>
/// <param name="ConditionImmunities">Conditions the creature cannot be given.</param>
/// <param name="Attacks">Every attack the creature can make.</param>
/// <param name="DiesAtZeroHitPoints">
/// True for monsters, false for player characters. The SRD is explicit: a monster dies
/// the instant it drops to 0 hit points, while a character falls Unconscious and begins
/// making Death Saving Throws.
/// </param>
public sealed record CombatantStats(
    int ArmorClass,
    int MaximumHitPoints,
    int SpeedFeet,
    int InitiativeBonus,
    IReadOnlyDictionary<Ability, MonsterAbility> Abilities,
    int ProficiencyBonus,
    CreatureSize Size,
    IReadOnlyDictionary<DamageType, DamageResponse> DamageResponses,
    IReadOnlyList<ConditionType> ConditionImmunities,
    IReadOnlyList<CombatAttack> Attacks,
    bool DiesAtZeroHitPoints)
{
    /// <summary>Class features and their numbers. Null for a monster.</summary>
    public CombatantFeatures? Character { get; init; }

    /// <summary>
    /// "While you're wearing it, any Critical Hit against you becomes a normal hit" —
    /// Adamantine Armor. The hit still lands; only the dice-doubling is denied.
    /// </summary>
    public bool CriticalHitsAgainstBecomeNormal { get; init; }

    /// <summary>
    /// "You ignore Half Cover when making a spell attack" — the Wand of the War Mage.
    /// Half exactly; Three-Quarters still counts.
    /// </summary>
    public bool IgnoresHalfCoverOnSpellAttacks { get; init; }

    /// <summary>
    /// The creature's printed type. Humanoid for every character — the 2024 rules print
    /// every species as Humanoid — and the stat block's own line for a monster. Added
    /// for the one printed rule that reads it in a fight: Shatter's "A Construct has
    /// Disadvantage on the save."
    /// </summary>
    public CreatureType Type { get; init; } = CreatureType.Humanoid;

    /// <summary>
    /// The lowest natural d20 roll that scores a Critical Hit. 20 for everyone except a
    /// Champion, whose Improved Critical reads "on a roll of 19 or 20".
    /// </summary>
    /// <remarks>
    /// Only a natural 20 auto-hits; a 19 still has to beat Armor Class, because the
    /// printed feature widens the crit and says nothing about hitting.
    /// </remarks>
    public int CriticalHitThreshold { get; init; } = 20;

    /// <summary>
    /// Skill bonuses by SRD skill name, for the ability checks a fight asks for.
    /// </summary>
    /// <remarks>
    /// Only skills the creature is actually better at appear. Anything absent falls back
    /// to the bare ability modifier, which is what an unproficient check is — see
    /// <see cref="Rules.SkillRules.BonusFor"/>. A monster's stat block prints only the
    /// skills it has a bonus in, so storing the absences would be inventing them.
    /// </remarks>
    public IReadOnlyDictionary<string, int> SkillBonuses { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The creature's Multiattack, when its stat block has one. Null for a character —
    /// a character's several attacks come from Extra Attack instead.
    /// </summary>
    public MultiattackEffect? Multiattack { get; init; }

    /// <summary>
    /// The creature's stat block entries, exactly as extracted. Empty for a character.
    /// </summary>
    /// <remarks>
    /// Carried whole rather than filtered, for the same reason the extractor reads all
    /// twelve classes: what is usable is decided where it is used —
    /// <c>Encounter.UseEntry</c> refuses an entry it cannot resolve with a named code,
    /// and <see cref="Combatant"/> builds its usage state from the limits printed here.
    /// A client listing what a monster can do reads this, not a curated subset.
    /// </remarks>
    public IReadOnlyList<MonsterEntry> Entries { get; init; } = [];

    /// <summary>
    /// How many attacks one Attack action buys.
    /// </summary>
    /// <remarks>
    /// Extra Attack and Multiattack are the same shape of rule from the engine's point
    /// of view — the action buys several attacks rather than several actions — so they
    /// resolve to one number here rather than being handled twice.
    /// </remarks>
    public int AttacksPerAction =>
        Character?.AttacksPerAction
        ?? Multiattack?.AttackCount
        ?? 1;

    /// <summary>
    /// Whether an attack may be used for the nth swing of a Multiattack.
    /// </summary>
    /// <remarks>
    /// A stat block either names one attack to repeat ("makes two Slam attacks") or
    /// offers a choice ("makes two attacks, using Scimitar and Pistol in any
    /// combination"). Anything not named is not part of the Multiattack, and a creature
    /// with no Multiattack is unconstrained.
    /// </remarks>
    public bool AllowsInMultiattack(string attackName) =>
        Multiattack is not { AttackNames.Count: > 0 } multiattack
        || multiattack.AttackNames.Contains(attackName, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when this combatant has the given implemented class feature.</summary>
    public bool Has(ClassFeature feature) => Character?.Has(feature) == true;

    /// <summary>The ability modifier for an ability, or 0 if the creature has no score for it.</summary>
    public int ModifierFor(Ability ability) =>
        Abilities.TryGetValue(ability, out var value) ? value.Modifier : 0;

    /// <summary>The saving throw bonus for an ability.</summary>
    public int SaveBonusFor(Ability ability) =>
        Abilities.TryGetValue(ability, out var value) ? value.SaveBonus : 0;

    /// <summary>
    /// Builds combat statistics from a resolved character sheet.
    /// </summary>
    /// <remarks>
    /// The one difference that matters most is <c>DiesAtZeroHitPoints: false</c> — a
    /// character falls Unconscious and rolls Death Saving Throws where a monster simply
    /// dies.
    /// </remarks>
    public static CombatantStats FromCharacter(
        CharacterSheet sheet,
        DiceExpression? sneakAttackDamage = null,
        int rageDamageBonus = 0,
        int rageUses = 0,
        int secondWindUses = 0,
        int actionSurgeUses = 0,
        IReadOnlyList<SpellDefinition>? spells = null,
        Ability? spellcastingAbility = null,
        int channelDivinityUses = 0)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var abilities = sheet.AbilityScores.ToDictionary(
            pair => pair.Key,
            pair => new MonsterAbility(pair.Value, sheet.SavingThrows[pair.Key]));

        return new CombatantStats(
            sheet.ArmorClass,
            sheet.MaximumHitPoints,
            sheet.SpeedFeet,
            sheet.Modifier(Ability.Dexterity),
            abilities,
            sheet.ProficiencyBonus,
            sheet.Size,
            new Dictionary<DamageType, DamageResponse>(),
            [],
            sheet.Attacks,
            DiesAtZeroHitPoints: false)
        {
            CriticalHitsAgainstBecomeNormal = sheet.CriticalHitsAgainstBecomeNormal,
            IgnoresHalfCoverOnSpellAttacks = sheet.IgnoresHalfCoverOnSpellAttacks,
            CriticalHitThreshold = sheet.Has(ClassFeature.ImprovedCritical) ? 19 : 20,

            SkillBonuses = sheet.Skills.ToDictionary(
                skill => skill.Skill,
                skill => skill.Bonus,
                StringComparer.OrdinalIgnoreCase),

            Character = new CombatantFeatures(
                sheet.Features.Select(granted => granted.Feature).ToArray(),
                sheet.AttacksPerAction,
                sneakAttackDamage,
                rageDamageBonus,
                rageUses,
                secondWindUses,
                actionSurgeUses,
                sheet.Level,
                spells,
                sheet.SpellSlots,
                spellcastingAbility,
                spellcastingAbility is { } ability
                    ? Rules.SpellcastingRules.SaveDifficultyClass(sheet.ProficiencyBonus, sheet.Modifier(ability))
                    : 0,
                spellcastingAbility is { } attackAbility
                    ? Rules.SpellcastingRules.AttackBonus(sheet.ProficiencyBonus, sheet.Modifier(attackAbility))
                        + sheet.SpellAttackItemBonus
                    : 0,
                channelDivinityUses),
        };
    }

    /// <summary>Builds combat statistics from an extracted stat block.</summary>
    public static CombatantStats FromMonster(MonsterDefinition monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        var attacks = monster.Entries
            .Where(entry => entry.Attack is not null)
            .Select(entry => new CombatAttack(
                entry.Name,
                entry.Attack!.Kind,
                entry.Attack.AttackBonus,
                entry.Attack.ReachFeet,
                entry.Attack.NormalRangeFeet,
                entry.Attack.LongRangeFeet,
                entry.Attack.Damage,
                // The rider hangs off the entry rather than off the attack grammar, so it
                // is joined to the attack here. Only the ones the engine can impose to the
                // letter come across; the rest stay counted in UnmodelledClauses.
                entry.AppliedConditions.Where(ConditionRules.CanBeImposed).ToArray())
            {
                EmbeddedSave = entry.Attack.EmbeddedSave,
            })
            .ToArray();

        return new CombatantStats(
            monster.ArmorClass,
            monster.HitPoints,
            monster.Speeds.TryGetValue(MovementMode.Walk, out var walk) ? walk : 0,
            monster.InitiativeBonus,
            monster.Abilities,
            monster.ProficiencyBonus,
            monster.Sizes.Count > 0 ? monster.Sizes[0] : CreatureSize.Medium,
            monster.DamageResponses,
            monster.ConditionImmunities,
            attacks,
            DiesAtZeroHitPoints: true)
        {
            Type = monster.Type,

            // Only a Multiattack whose named attacks the creature actually has is worth
            // carrying: several stat blocks name an attack granted by a trait or a
            // spell, and letting those through would hand out swings the creature has no
            // way to make.
            Multiattack = UsableMultiattack(monster, attacks),

            Entries = monster.Entries,

            SkillBonuses = monster.Skills.ToDictionary(
                skill => skill.Key,
                skill => skill.Value,
                StringComparer.OrdinalIgnoreCase),
        };
    }

    private static MultiattackEffect? UsableMultiattack(
        MonsterDefinition monster,
        IReadOnlyList<CombatAttack> attacks)
    {
        var multiattack = monster.Entries
            .Select(entry => entry.Multiattack)
            .FirstOrDefault(effect => effect is not null);

        if (multiattack is null || multiattack.AttackCount < 2)
        {
            return null;
        }

        var usable = multiattack.AttackNames
            .Where(name => attacks.Any(attack =>
                string.Equals(attack.Name, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        // A named-attack Multiattack whose attack does not exist is dropped entirely; a
        // free-combination one keeps whichever of its options are real.
        return usable.Length == 0 ? null : multiattack with { AttackNames = usable };
    }
}

/// <summary>How a combatant's turn is going: what it has left to spend.</summary>
public sealed class TurnResources
{
    /// <summary>True while the combatant still has its Action.</summary>
    public bool HasAction { get; private set; } = true;

    /// <summary>True while the combatant still has its Bonus Action.</summary>
    public bool HasBonusAction { get; private set; } = true;

    /// <summary>
    /// True while the combatant still has its Reaction. Unlike the others this is
    /// refreshed at the <em>start</em> of the creature's turn, so a Reaction spent
    /// during someone else's turn stays spent until then.
    /// </summary>
    public bool HasReaction { get; private set; } = true;

    /// <summary>Movement left this turn, in feet.</summary>
    public int MovementFeet { get; private set; }

    /// <summary>
    /// True once any movement has been spent this turn. Standing up spends movement,
    /// so it counts as having moved — the reading Steady Aim's "haven't moved" gate
    /// rests on.
    /// </summary>
    public bool HasMoved { get; private set; }

    /// <summary>True after Steady Aim: Speed is 0 for the rest of the turn.</summary>
    private bool _movementForfeited;

    /// <summary>True when the combatant took the Dodge action this turn.</summary>
    public bool IsDodging { get; private set; }

    /// <summary>True when the combatant took the Disengage action this turn.</summary>
    public bool HasDisengaged { get; private set; }

    /// <summary>Begins a new turn, restoring everything except an already-spent Reaction's history.</summary>
    public void BeginTurn(int speedFeet)
    {
        HasAction = true;
        HasBonusAction = true;
        HasReaction = true;
        MovementFeet = speedFeet;
        HasMoved = false;
        _movementForfeited = false;
        IsDodging = false;
        HasDisengaged = false;
    }

    public void SpendAction() => HasAction = false;

    /// <summary>Gives the action back — what Action Surge buys.</summary>
    public void RestoreAction() => HasAction = true;

    public void SpendBonusAction() => HasBonusAction = false;

    public void SpendReaction() => HasReaction = false;

    public void SpendMovement(int feet)
    {
        MovementFeet = Math.Max(0, MovementFeet - feet);
        HasMoved = HasMoved || feet > 0;
    }

    /// <summary>The Dash action: gain extra movement equal to your Speed.</summary>
    /// <remarks>
    /// Buys nothing after Steady Aim — "your Speed is 0 for the rest of the turn", and
    /// a Dash adds to a Speed of 0.
    /// </remarks>
    public void AddMovement(int feet)
    {
        if (!_movementForfeited)
        {
            MovementFeet += feet;
        }
    }

    /// <summary>Steady Aim's price: no movement for the rest of the turn.</summary>
    public void ForfeitMovement()
    {
        MovementFeet = 0;
        _movementForfeited = true;
    }

    public void StartDodging() => IsDodging = true;

    public void Disengage() => HasDisengaged = true;
}

/// <summary>
/// The per-fight state of a combatant's class features: what is active, and what is
/// left to spend.
/// </summary>
public sealed class FeatureState
{
    /// <summary>True while a Barbarian's Rage is running.</summary>
    public bool IsRaging { get; internal set; }

    /// <summary>Rages left this rest.</summary>
    public int RagesRemaining { get; internal set; }

    /// <summary>Second Wind uses left this rest.</summary>
    public int SecondWindRemaining { get; internal set; }

    /// <summary>Action Surge uses left this rest.</summary>
    public int ActionSurgeRemaining { get; internal set; }

    /// <summary>Channel Divinity uses left this rest.</summary>
    public int ChannelDivinityRemaining { get; internal set; }

    /// <summary>Sneak Attack is once per turn, not once per attack.</summary>
    public bool SneakAttackUsedThisTurn { get; internal set; }

    /// <summary>True once Reckless Attack has been declared this turn.</summary>
    public bool IsRecklessThisTurn { get; internal set; }

    /// <summary>Frenzy is "the first target you hit on your turn" — once, tracked here.</summary>
    public bool FrenzyUsedThisTurn { get; internal set; }

    /// <summary>Cleave is "only once per turn", tracked here.</summary>
    public bool CleaveUsedThisTurn { get; internal set; }

    /// <summary>
    /// Who has Slowed this creature. Non-empty means Speed is down 10 feet — exactly 10
    /// however many entries there are, because "if the creature is hit more than once by
    /// weapons that have this property, the Speed reduction doesn't exceed 10 feet".
    /// Each entry expires at the start of its own author's next turn.
    /// </summary>
    public HashSet<string> SlowedBy { get; } = new(StringComparer.Ordinal);

    /// <summary>True after Steady Aim, until the next attack roll consumes it.</summary>
    public bool SteadyAimedThisTurn { get; internal set; }

    /// <summary>
    /// The Cunning Strike effect declared for this turn's Sneak Attack, if any.
    /// </summary>
    /// <remarks>
    /// Declared ahead of the attack because its cost is paid in dice removed
    /// <em>before</em> rolling, so the choice has to exist by the time damage is rolled.
    /// Cleared when it is spent, and at the end of the turn.
    /// </remarks>
    public CunningStrikeEffect CunningStrike { get; internal set; }

    /// <summary>
    /// Whether the creature did one of the printed things that extend a Rage this
    /// turn: <b>made an attack roll</b> against an enemy, forced an enemy to make a
    /// saving throw, or spent the Bonus Action on the extension itself.
    /// </summary>
    /// <remarks>
    /// The printed option is "Make an attack roll against an enemy" — <b>the roll,
    /// not the hit</b>. This flag was set only where damage landed until a played
    /// run watched a Barbarian rage, swing, miss and lose the Rage in the same turn.
    /// </remarks>
    public bool SustainedRageThisTurn { get; internal set; }

    /// <summary>
    /// The rager's turn count when the Rage began. "The Rage lasts until the end of
    /// your next turn", so the turn it was entered on never has to extend it — the
    /// requirement starts one turn later, on the same stamped clock Vex and Guiding
    /// Bolt run on.
    /// </summary>
    public int RageBeganOnTurn { get; internal set; }

    /// <summary>
    /// Attacks still available from the current Attack action. Extra Attack is modelled
    /// as the action buying several attacks rather than as several actions.
    /// </summary>
    public int AttacksRemainingThisAction { get; internal set; }

    /// <summary>Spell slots left this rest, by spell level.</summary>
    public Dictionary<int, int> SpellSlotsRemaining { get; } = [];

    /// <summary>
    /// Who sapped this creature, when a Sap mastery has left it with Disadvantage on its
    /// next attack roll. Null when it is unsapped.
    /// </summary>
    /// <remarks>
    /// Two things end it, exactly as printed: the creature's next attack roll consumes
    /// it, and the start of the sapper's next turn clears it whether it was used or not
    /// ("before the start of your next turn"). The sapper's id is stored because that
    /// second clause is measured against <em>their</em> turn, not the victim's — the same
    /// possessive trap the condition expiries carry.
    /// </remarks>
    public string? SappedBy { get; internal set; }

    /// <summary>
    /// The creature this one has Advantage against on its next attack roll, from a Vex
    /// mastery. Null when there is none.
    /// </summary>
    /// <remarks>
    /// "before the end of your next turn" — so unlike Sap this is cleared at the end of
    /// the holder's own <em>next</em> turn, and it is target-specific: vexing a goblin
    /// buys nothing against the goblin beside it. <see cref="VexEarnedOnTurn"/> is what
    /// makes "next" mean next: for its first shipped year this was cleared at the end
    /// of whatever turn was ending, so a Vex earned by a single-attack Rogue died
    /// before the Sneak Attack it was printed to feed (#153).
    /// </remarks>
    public string? VexedTargetId { get; internal set; }

    /// <summary>
    /// The holder's turn count when the Vex was earned. The end of a <em>later</em>
    /// turn of the holder's clears it; the end of the earning turn does not.
    /// </summary>
    public int VexEarnedOnTurn { get; internal set; }

    /// <summary>
    /// Who lit this creature up with Guiding Bolt's rider, when somebody has: the next
    /// attack roll made against it — anyone's — has Advantage. Null when nobody has.
    /// </summary>
    /// <remarks>
    /// The state lives on the victim because the benefit belongs to whoever attacks it
    /// next, and the author is remembered because the expiry — "before the end of your
    /// next turn" — is measured on the <em>caster's</em> clock, the possessive trap
    /// every duration in this file carries.
    /// </remarks>
    public string? GuidedBy { get; internal set; }

    /// <summary>
    /// The author's turn count when the light landed. The end of a later turn of the
    /// author's clears it; the end of the casting turn does not — the same stamped
    /// clock Vex runs on (#153).
    /// </summary>
    public int GuidedOnAuthorTurn { get; internal set; }

    /// <summary>
    /// The spell this creature is concentrating on, if any. A creature can concentrate
    /// on only one spell at a time.
    /// </summary>
    public string? ConcentratingOn { get; internal set; }

    internal void BeginTurn()
    {
        SneakAttackUsedThisTurn = false;
        IsRecklessThisTurn = false;
        FrenzyUsedThisTurn = false;
        CleaveUsedThisTurn = false;
        SteadyAimedThisTurn = false;
        SustainedRageThisTurn = false;
        AttacksRemainingThisAction = 0;
        CunningStrike = CunningStrikeEffect.None;
    }
}

/// <summary>
/// Per-fight state of a combatant's limited-use stat block entries: what is left of
/// each "(Recharge 5-6)", "(3/Day)" or "(Recharge after a Short or Long Rest)".
/// </summary>
/// <remarks>
/// <para>
/// The monster-side sibling of <see cref="FeatureState"/>, keyed by entry name because
/// that is how both <c>Encounter.Attack</c> and <c>Encounter.UseEntry</c> address an
/// entry — one dictionary, so an attack-shaped entry and any other entry with the same
/// limit are gated by the same state rather than two copies of it.
/// </para>
/// <para>
/// The SRD's three limits, read for a combat-only game where rests happen between
/// fights: Recharge starts charged and rolls a d6 at the start of each of the
/// creature's turns while spent; X/Day grants X uses (a Long Rest restores them, and no
/// fight contains one); Recharge after a Short or Long Rest grants one. What a rest
/// restores between fights is the gauntlet's concern, not this type's.
/// </para>
/// <para>
/// An entry with no printed limit is not tracked at all, and
/// <see cref="IsAvailable"/> answers true for it — unlimited is the common case and
/// storing it would be inventing state.
/// </para>
/// </remarks>
public sealed class UsageState
{
    private sealed class TrackedEntry
    {
        public required UsageLimit Limit { get; init; }

        public int UsesRemaining { get; set; }
    }

    private readonly Dictionary<string, TrackedEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    internal UsageState(IEnumerable<MonsterEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Usage is not { } limit)
            {
                continue;
            }

            _entries.TryAdd(entry.Name, new TrackedEntry
            {
                Limit = limit,
                UsesRemaining = limit.Kind == UsageLimitKind.PerDay ? limit.UsesPerDay ?? 1 : 1,
            });
        }
    }

    /// <summary>True when the entry carries a printed usage limit at all.</summary>
    public bool Tracks(string entryName) => _entries.ContainsKey(entryName);

    /// <summary>True when the entry can be used right now. Untracked entries always can.</summary>
    public bool IsAvailable(string entryName) =>
        !_entries.TryGetValue(entryName, out var tracked) || tracked.UsesRemaining > 0;

    /// <summary>The printed limit on an entry, or null when it has none.</summary>
    public UsageLimit? LimitFor(string entryName) =>
        _entries.TryGetValue(entryName, out var tracked) ? tracked.Limit : null;

    /// <summary>Uses left of a tracked entry, or null when the entry is not limited.</summary>
    public int? UsesRemaining(string entryName) =>
        _entries.TryGetValue(entryName, out var tracked) ? tracked.UsesRemaining : null;

    internal void Spend(string entryName)
    {
        if (_entries.TryGetValue(entryName, out var tracked))
        {
            tracked.UsesRemaining = Math.Max(0, tracked.UsesRemaining - 1);
        }
    }

    /// <summary>
    /// The spent Recharge entries waiting on a d6, with the roll that brings each back.
    /// Ordered by name so the dice are consumed in a reproducible order.
    /// </summary>
    internal IEnumerable<(string Name, int Minimum)> AwaitingRecharge() =>
        _entries
            .Where(pair => pair.Value.Limit is { Kind: UsageLimitKind.Recharge, RechargeMinimum: not null }
                && pair.Value.UsesRemaining == 0)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (pair.Key, pair.Value.Limit.RechargeMinimum!.Value));

    internal void Recharge(string entryName)
    {
        if (_entries.TryGetValue(entryName, out var tracked))
        {
            tracked.UsesRemaining = 1;
        }
    }
}

/// <summary>
/// When a condition ends, resolved against a particular creature's turn counter.
/// </summary>
/// <remarks>
/// <para>
/// The turn number is what makes "next" unambiguous. A rider applied during the devil's
/// own turn and one applied during somebody else's — on an Opportunity Attack, say —
/// both read "until the start of the devil's next turn", and they mean different moments.
/// Recording <see cref="OwnerTurnNumber"/> as the owner's turn count <em>at the moment of
/// application, plus one</em> resolves both without the expiry needing to know which case
/// it was.
/// </para>
/// <para>
/// The owner is often not the creature carrying the condition, which is why this is not
/// simply a countdown on the bearer.
/// </para>
/// </remarks>
/// <param name="OwnerId">The combatant whose turns are counted.</param>
/// <param name="Clock">Which boundary of that turn ends it.</param>
/// <param name="OwnerTurnNumber">The owner's turn number at which it ends.</param>
public sealed record ConditionExpiry(string OwnerId, ConditionClock Clock, int OwnerTurnNumber);

/// <summary>A condition a creature currently has, and everything that governs its end.</summary>
/// <param name="Condition">The condition.</param>
/// <param name="SourceId">
/// The combatant that imposed it, when one did. Null for a condition with no author —
/// the Unconscious a creature gets from dropping to 0 hit points.
/// </param>
/// <param name="Expiry">When it ends on its own. Null when only the rules can end it.</param>
/// <param name="EscapeDifficultyClass">
/// The DC to escape. Only Grappled prints one, and the SRD prints it as part of the
/// condition rather than of the creature, so it is carried here.
/// </param>
/// <param name="GrappleRangeFeet">
/// The grapple's range — the reach of the attack that made it. The SRD ends a grapple
/// when "the distance between the Grappled target and the grappler exceeds the grapple's
/// range", so that distance has to be remembered. Only set for Grappled.
/// </param>
/// <param name="RepeatSaveAbility">
/// The ability of the save the bearer repeats at the end of each of its turns, ending
/// the condition on a success — Hold Person's way out. Null when no repeat is printed.
/// </param>
/// <param name="RepeatSaveDifficultyClass">The DC that repeated save rolls against.</param>
/// <param name="TiedToConcentration">
/// True when the condition lives only while its source concentrates on the spell that
/// imposed it: the sweep in <c>Encounter.EndConcentration</c> takes it away the moment
/// the Concentration ends, however it ended.
/// </param>
public sealed record ActiveCondition(
    ConditionType Condition,
    string? SourceId = null,
    ConditionExpiry? Expiry = null,
    int? EscapeDifficultyClass = null,
    int? GrappleRangeFeet = null,
    Ability? RepeatSaveAbility = null,
    int? RepeatSaveDifficultyClass = null,
    bool TiedToConcentration = false);

/// <summary>
/// What a creature brings into a fight from an earlier one.
/// </summary>
/// <remarks>
/// Every field beyond hit points is nullable and means "unchanged from full" when
/// absent, so a caller carrying only wounds does not have to restate resources it does
/// not track. A creature carried in at 0 hit points arrives down — dead if its stat
/// block says so, Unconscious if it makes Death Saving Throws.
/// </remarks>
/// <param name="CurrentHitPoints">Hit points on arrival, clamped to the maximum.</param>
/// <param name="RagesRemaining">Rages left, or null for all of them.</param>
/// <param name="SecondWindRemaining">Second Wind uses left, or null for all.</param>
/// <param name="ActionSurgeRemaining">Action Surge uses left, or null for all.</param>
/// <param name="ChannelDivinityRemaining">Channel Divinity uses left, or null for all.</param>
/// <param name="SpellSlotsRemaining">Slots left by level, or null for a full complement.</param>
/// <param name="Potions">
/// Potions of Healing carried in, by potency. Null and empty both mean none — unlike the
/// resources above, "unchanged from full" is not a meaningful default for a consumable
/// nobody starts with.
/// </param>
public sealed record CombatantCarryOver(
    int CurrentHitPoints,
    int? RagesRemaining = null,
    int? SecondWindRemaining = null,
    int? ActionSurgeRemaining = null,
    IReadOnlyDictionary<int, int>? SpellSlotsRemaining = null,
    IReadOnlyDictionary<HealingPotion, int>? Potions = null,
    int? ChannelDivinityRemaining = null);

/// <summary>
/// What a combatant is carrying that a fight can consume.
/// </summary>
/// <remarks>
/// A sibling of <see cref="FeatureState"/> and <see cref="UsageState"/> rather than part
/// of either: a potion is not a class feature and not a stat block entry, and it is the
/// one thing a fight spends that <em>no</em> rest brings back. Nothing seeds this from a
/// character sheet, because a potion is state rather than a choice — it comes in from
/// the run, or not at all.
/// </remarks>
public sealed class InventoryState
{
    private readonly Dictionary<HealingPotion, int> _potions = [];

    /// <summary>Potions carried, by potency. Only potencies actually held appear.</summary>
    public IReadOnlyDictionary<HealingPotion, int> Potions => _potions;

    /// <summary>How many of one potency are left.</summary>
    public int CountOf(HealingPotion potency) => _potions.GetValueOrDefault(potency);

    /// <summary>How many potions of any potency are left.</summary>
    public int TotalPotions => _potions.Values.Sum();

    /// <summary>
    /// The weakest potion carried, or null when there is none.
    /// </summary>
    /// <remarks>
    /// What a client or the tactics policy should reach for by default: spending a
    /// supreme potion on a scratch wastes the difference, and the weakest one that is
    /// available is never the wrong end of that trade by much.
    /// </remarks>
    public HealingPotion? Weakest => _potions
        .Where(pair => pair.Value > 0)
        .Select(pair => (HealingPotion?)pair.Key)
        .OrderBy(potency => potency)
        .FirstOrDefault();

    internal void Seed(IReadOnlyDictionary<HealingPotion, int> potions)
    {
        _potions.Clear();

        foreach (var (potency, count) in potions.Where(pair => pair.Value > 0))
        {
            _potions[potency] = count;
        }
    }

    internal void Spend(HealingPotion potency)
    {
        if (!_potions.TryGetValue(potency, out var count) || count <= 0)
        {
            return;
        }

        if (count == 1)
        {
            _potions.Remove(potency);
        }
        else
        {
            _potions[potency] = count - 1;
        }
    }
}

/// <summary>A creature taking part in a fight, and everything about it that changes.</summary>
public sealed class Combatant
{
    /// <summary>
    /// Conditions whose printed text includes "You have the Incapacitated condition".
    /// Adding one brings Incapacitated with it; removing the last of them takes the
    /// Incapacitated it brought back out.
    /// </summary>
    private static readonly ConditionType[] BringsIncapacitated =
    [
        ConditionType.Paralyzed,
        ConditionType.Stunned,
        ConditionType.Unconscious,
    ];

    private readonly Dictionary<ConditionType, ActiveCondition> _conditions = [];

    /// <param name="carriedOver">
    /// What this creature brings in from an earlier fight — wounds and spent resources.
    /// Null starts it at full strength, which is every fight that stands alone.
    /// </param>
    public Combatant(
        string id,
        string name,
        string sideId,
        CombatantStats stats,
        GridPosition position,
        CombatantCarryOver? carriedOver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sideId);
        ArgumentNullException.ThrowIfNull(stats);

        Id = id;
        Name = name;
        SideId = sideId;
        Stats = stats;
        Position = position;
        Uses = new UsageState(stats.Entries);
        _traits = Rules.MonsterTraitRegistry.TraitsOf(stats.Entries);

        if (stats.Character is { } character)
        {
            Features.RagesRemaining = character.RageUses;
            Features.SecondWindRemaining = character.SecondWindUses;
            Features.ActionSurgeRemaining = character.ActionSurgeUses;
            Features.ChannelDivinityRemaining = character.ChannelDivinityUses;

            foreach (var (level, slots) in character.SpellSlots)
            {
                Features.SpellSlotsRemaining[level] = slots;
            }
        }

        // Full strength unless the creature is carrying wounds and spent resources in
        // from an earlier fight, which is what makes a gauntlet a run rather than a
        // series of unrelated encounters.
        CurrentHitPoints = stats.MaximumHitPoints;

        if (carriedOver is not null)
        {
            ApplyCarryOver(carriedOver);
        }
    }

    /// <summary>Seeds this combatant from what it brought out of an earlier fight.</summary>
    private void ApplyCarryOver(CombatantCarryOver carriedOver)
    {
        CurrentHitPoints = Math.Clamp(carriedOver.CurrentHitPoints, 0, Stats.MaximumHitPoints);

        Features.RagesRemaining = carriedOver.RagesRemaining ?? Features.RagesRemaining;
        Features.SecondWindRemaining = carriedOver.SecondWindRemaining ?? Features.SecondWindRemaining;
        Features.ActionSurgeRemaining = carriedOver.ActionSurgeRemaining ?? Features.ActionSurgeRemaining;
        Features.ChannelDivinityRemaining = carriedOver.ChannelDivinityRemaining ?? Features.ChannelDivinityRemaining;

        if (carriedOver.SpellSlotsRemaining is { } slots)
        {
            Features.SpellSlotsRemaining.Clear();

            foreach (var (level, remaining) in slots)
            {
                Features.SpellSlotsRemaining[level] = remaining;
            }
        }

        if (carriedOver.Potions is { } potions)
        {
            Inventory.Seed(potions);
        }

        // A creature brought in at 0 hit points is already down. Going through the same
        // path a blow would take keeps Unconscious, Prone and the dying state consistent
        // with how every other creature reaches 0.
        if (CurrentHitPoints == 0)
        {
            if (Stats.DiesAtZeroHitPoints)
            {
                MarkDead();
            }
            else
            {
                AddCondition(ConditionType.Unconscious);
            }
        }
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Which side of the fight this combatant is on. Same value means allied.</summary>
    public string SideId { get; }

    public CombatantStats Stats { get; }

    public GridPosition Position { get; private set; }

    public int CurrentHitPoints { get; private set; }

    /// <summary>
    /// Temporary hit points, which absorb damage before real ones and never stack —
    /// a new pool replaces the old rather than adding to it.
    /// </summary>
    public int TemporaryHitPoints { get; private set; }

    public TurnResources Turn { get; } = new();

    /// <summary>Class feature state for this fight.</summary>
    public FeatureState Features { get; } = new();

    /// <summary>Limited-use entry state for this fight — recharges and uses per day.</summary>
    public UsageState Uses { get; }

    /// <summary>Consumables carried into this fight, and what is left of them.</summary>
    public InventoryState Inventory { get; } = new();

    private readonly IReadOnlyCollection<Rules.MonsterTrait> _traits;

    /// <summary>
    /// Whether this creature's stat block prints a trait the engine executes. Resolved
    /// once from <c>MonsterTraitRegistry</c>; always false for a character.
    /// </summary>
    public bool HasTrait(Rules.MonsterTrait trait) => _traits.Contains(trait);

    public IReadOnlyCollection<ConditionType> Conditions => _conditions.Keys;

    /// <summary>Every condition the creature has, with its source and expiry.</summary>
    public IReadOnlyCollection<ActiveCondition> ActiveConditions => _conditions.Values;

    /// <summary>
    /// How many turns this creature has begun. The clock every condition duration is
    /// measured against; see <see cref="ConditionExpiry"/>.
    /// </summary>
    public int TurnsBegun { get; private set; }

    /// <summary>True once the creature is dead and out of the fight for good.</summary>
    public bool IsDead { get; private set; }

    /// <summary>Death Saving Throw successes, 0–3. Reset when hit points are regained.</summary>
    public int DeathSaveSuccesses { get; private set; }

    /// <summary>Death Saving Throw failures, 0–3.</summary>
    public int DeathSaveFailures { get; private set; }

    /// <summary>True when the creature is at 0 hit points but has stopped making Death Saves.</summary>
    public bool IsStable { get; private set; }

    /// <summary>Initiative result, set when the fight begins.</summary>
    public int Initiative { get; private set; }

    /// <summary>True when the creature is at 0 hit points, not dead, and not yet stable.</summary>
    public bool IsDying => !IsDead && CurrentHitPoints == 0 && !IsStable;

    /// <summary>
    /// True when the creature can still act. Dead, dying and Incapacitated creatures
    /// cannot; the Unconscious condition brings Incapacitated with it.
    /// </summary>
    public bool CanAct => !IsDead && CurrentHitPoints > 0 && !HasCondition(ConditionType.Incapacitated);

    /// <summary>True when the creature is still a threat — alive, conscious and able to act.</summary>
    public bool IsActive => CanAct;

    public bool HasCondition(ConditionType condition) => _conditions.ContainsKey(condition);

    /// <summary>The condition with its source and expiry, or null if the creature has not got it.</summary>
    public ActiveCondition? ConditionState(ConditionType condition) =>
        _conditions.TryGetValue(condition, out var active) ? active : null;

    /// <summary>
    /// Adds a condition unless the creature is immune to it.
    /// </summary>
    /// <remarks>
    /// Re-applying a condition the creature already has refreshes it rather than being
    /// ignored, but never shortens it: an application that ends on a timer cannot displace
    /// one that only the rules can end. Otherwise a wolf knocking a Prone creature Prone
    /// again would hand it an expiry it did not have and stand it up for free.
    /// </remarks>
    /// <returns>True when the creature did not already have the condition.</returns>
    public bool AddCondition(ConditionType condition, string? sourceId = null, ConditionExpiry? expiry = null) =>
        AddCondition(new ActiveCondition(condition, sourceId, expiry));

    /// <inheritdoc cref="AddCondition(ConditionType, string?, ConditionExpiry?)"/>
    public bool AddCondition(ActiveCondition active)
    {
        ArgumentNullException.ThrowIfNull(active);

        var condition = active.Condition;

        if (Stats.ConditionImmunities.Contains(condition))
        {
            return false;
        }

        if (_conditions.TryGetValue(condition, out var existing))
        {
            _conditions[condition] = existing with
            {
                SourceId = active.SourceId ?? existing.SourceId,
                Expiry = existing.Expiry is null ? null : active.Expiry,
                EscapeDifficultyClass = active.EscapeDifficultyClass ?? existing.EscapeDifficultyClass,
                GrappleRangeFeet = active.GrappleRangeFeet ?? existing.GrappleRangeFeet,
            };

            return false;
        }

        _conditions[condition] = active;
        var sourceId = active.SourceId;
        var expiry = active.Expiry;

        // Paralyzed, Stunned and Unconscious bring Incapacitated with them, and
        // Unconscious brings Prone too, per each condition's own definition. Modelling
        // that here means nothing else has to remember it. The brought conditions
        // inherit the source and expiry, so a Stunned that wears off does not leave its
        // Incapacitated behind — and TryAdd never displaces one imposed in its own right.
        if (BringsIncapacitated.Contains(condition))
        {
            _conditions.TryAdd(ConditionType.Incapacitated, new ActiveCondition(ConditionType.Incapacitated, sourceId, expiry));
        }

        if (condition == ConditionType.Unconscious)
        {
            _conditions.TryAdd(ConditionType.Prone, new ActiveCondition(ConditionType.Prone, sourceId, expiry));
        }

        return true;
    }

    public bool RemoveCondition(ConditionType condition)
    {
        if (!_conditions.Remove(condition, out var removed))
        {
            return false;
        }

        // Removing the last Incapacitated-bringer takes the Incapacitated it brought
        // back out — recognised by the source and expiry it inherited, so an
        // Incapacitated imposed in its own right (a Ghast's Claw, with its own clock)
        // survives its bearer being knocked out and healed. Prone is deliberately left
        // behind: "When this condition ends, you remain Prone."
        if (BringsIncapacitated.Contains(condition)
            && !BringsIncapacitated.Any(HasCondition)
            && _conditions.TryGetValue(ConditionType.Incapacitated, out var incapacitated)
            && string.Equals(incapacitated.SourceId, removed.SourceId, StringComparison.Ordinal)
            && Equals(incapacitated.Expiry, removed.Expiry))
        {
            _conditions.Remove(ConditionType.Incapacitated);
        }

        return true;
    }

    /// <summary>
    /// Ends every condition due to expire at this boundary of the owner's turn, and says
    /// which ones ended.
    /// </summary>
    internal IReadOnlyList<ConditionType> ExpireConditions(string ownerId, ConditionClock clock, int ownerTurnNumber)
    {
        var due = _conditions.Values
            .Where(active => active.Expiry is { } expiry
                && expiry.Clock == clock
                && string.Equals(expiry.OwnerId, ownerId, StringComparison.Ordinal)
                && ownerTurnNumber >= expiry.OwnerTurnNumber)
            .Select(active => active.Condition)
            .OrderBy(condition => condition)
            .ToArray();

        foreach (var condition in due)
        {
            RemoveCondition(condition);
        }

        return due;
    }

    internal void BeginTurnClock() => TurnsBegun++;

    internal void SetInitiative(int value) => Initiative = value;

    internal void MoveTo(GridPosition position) => Position = position;

    internal void SetTemporaryHitPoints(int value) => TemporaryHitPoints = Math.Max(0, value);

    /// <summary>Reduces hit points, never below zero. Returns the damage actually taken.</summary>
    internal int ReduceHitPoints(int amount)
    {
        var taken = Math.Min(amount, CurrentHitPoints);
        CurrentHitPoints -= taken;
        return taken;
    }

    /// <summary>Restores hit points up to the maximum, clearing the dying state.</summary>
    internal void RegainHitPoints(int amount)
    {
        if (IsDead)
        {
            return;
        }

        CurrentHitPoints = Math.Min(Stats.MaximumHitPoints, CurrentHitPoints + amount);

        if (CurrentHitPoints > 0)
        {
            ResetDeathSaves();
            IsStable = false;
            RemoveCondition(ConditionType.Unconscious);
        }
    }

    internal void MarkDead()
    {
        IsDead = true;
        IsStable = false;
        CurrentHitPoints = 0;
        AddCondition(ConditionType.Unconscious);
    }

    /// <summary>
    /// The encounter round this creature died in, stamped by the encounter beside its
    /// own "is dead" narration. Null means the death was not seen this fight — a
    /// combatant brought in dead, or hand-built that way — and Revivify's window
    /// deliberately reads null as "too long ago": refusing a revival the rules might
    /// have allowed is recoverable, reviving one they forbid is not.
    /// </summary>
    public int? DiedInRound { get; private set; }

    internal void RecordDeathRound(int round) => DiedInRound = round;

    /// <summary>
    /// Returns the dead to life with the given hit points — Revivify's transition, the
    /// one thing <see cref="RegainHitPoints"/> deliberately refuses to do.
    /// </summary>
    /// <remarks>
    /// Clearing <see cref="IsDead"/> first and then healing through the ordinary path
    /// keeps everything one transition should touch consistent: the death saves reset,
    /// Unconscious lifts, and stability clears exactly as they would for a downed
    /// creature brought back up.
    /// </remarks>
    internal void ReturnToLife(int hitPoints)
    {
        if (!IsDead)
        {
            return;
        }

        IsDead = false;
        DiedInRound = null;
        RegainHitPoints(hitPoints);
    }

    internal void MarkStable()
    {
        IsStable = true;
        ResetDeathSaves();
    }

    /// <summary>
    /// Ends stability without restoring hit points. Taking damage at 0 hit points does
    /// this: the creature is back to making Death Saving Throws.
    /// </summary>
    internal void ClearStable() => IsStable = false;

    internal void AddDeathSaveSuccess() => DeathSaveSuccesses = Math.Min(3, DeathSaveSuccesses + 1);

    internal void AddDeathSaveFailure(int count = 1) => DeathSaveFailures = Math.Min(3, DeathSaveFailures + count);

    internal void ResetDeathSaves()
    {
        DeathSaveSuccesses = 0;
        DeathSaveFailures = 0;
    }

    public override string ToString() =>
        $"{Name} [{CurrentHitPoints}/{Stats.MaximumHitPoints}] at {Position}";
}
