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
    int SpellAttackBonus = 0)
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
    /// The creature's Multiattack, when its stat block has one. Null for a character —
    /// a character's several attacks come from Extra Attack instead.
    /// </summary>
    public MultiattackEffect? Multiattack { get; init; }

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
        Ability? spellcastingAbility = null)
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
                    : 0),
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
                entry.AppliedConditions.Where(ConditionRules.CanBeImposed).ToArray()))
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
            // Only a Multiattack whose named attacks the creature actually has is worth
            // carrying: several stat blocks name an attack granted by a trait or a
            // spell, and letting those through would hand out swings the creature has no
            // way to make.
            Multiattack = UsableMultiattack(monster, attacks),
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
        IsDodging = false;
        HasDisengaged = false;
    }

    public void SpendAction() => HasAction = false;

    /// <summary>Gives the action back — what Action Surge buys.</summary>
    public void RestoreAction() => HasAction = true;

    public void SpendBonusAction() => HasBonusAction = false;

    public void SpendReaction() => HasReaction = false;

    public void SpendMovement(int feet) => MovementFeet = Math.Max(0, MovementFeet - feet);

    /// <summary>The Dash action: gain extra movement equal to your Speed.</summary>
    public void AddMovement(int feet) => MovementFeet += feet;

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

    /// <summary>Sneak Attack is once per turn, not once per attack.</summary>
    public bool SneakAttackUsedThisTurn { get; internal set; }

    /// <summary>True once Reckless Attack has been declared this turn.</summary>
    public bool IsRecklessThisTurn { get; internal set; }

    /// <summary>
    /// Whether the creature attacked on its turn. Rage ends if a turn passes without
    /// the Barbarian attacking or forcing a saving throw.
    /// </summary>
    public bool AttackedThisTurn { get; internal set; }

    /// <summary>
    /// Attacks still available from the current Attack action. Extra Attack is modelled
    /// as the action buying several attacks rather than as several actions.
    /// </summary>
    public int AttacksRemainingThisAction { get; internal set; }

    /// <summary>Spell slots left this rest, by spell level.</summary>
    public Dictionary<int, int> SpellSlotsRemaining { get; } = [];

    /// <summary>
    /// The spell this creature is concentrating on, if any. A creature can concentrate
    /// on only one spell at a time.
    /// </summary>
    public string? ConcentratingOn { get; internal set; }

    internal void BeginTurn()
    {
        SneakAttackUsedThisTurn = false;
        IsRecklessThisTurn = false;
        AttackedThisTurn = false;
        AttacksRemainingThisAction = 0;
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
public sealed record ActiveCondition(
    ConditionType Condition,
    string? SourceId = null,
    ConditionExpiry? Expiry = null);

/// <summary>A creature taking part in a fight, and everything about it that changes.</summary>
public sealed class Combatant
{
    private readonly Dictionary<ConditionType, ActiveCondition> _conditions = [];

    public Combatant(string id, string name, string sideId, CombatantStats stats, GridPosition position)
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
        CurrentHitPoints = stats.MaximumHitPoints;

        if (stats.Character is { } character)
        {
            Features.RagesRemaining = character.RageUses;
            Features.SecondWindRemaining = character.SecondWindUses;
            Features.ActionSurgeRemaining = character.ActionSurgeUses;

            foreach (var (level, slots) in character.SpellSlots)
            {
                Features.SpellSlotsRemaining[level] = slots;
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
    public bool AddCondition(ConditionType condition, string? sourceId = null, ConditionExpiry? expiry = null)
    {
        if (Stats.ConditionImmunities.Contains(condition))
        {
            return false;
        }

        if (_conditions.TryGetValue(condition, out var existing))
        {
            _conditions[condition] = existing with
            {
                SourceId = sourceId ?? existing.SourceId,
                Expiry = existing.Expiry is null ? null : expiry,
            };

            return false;
        }

        _conditions[condition] = new ActiveCondition(condition, sourceId, expiry);

        // Unconscious brings Incapacitated and Prone with it, per the condition's own
        // definition. Modelling that here means nothing else has to remember it. They
        // inherit its expiry, so an Unconscious that wears off does not leave them behind.
        if (condition == ConditionType.Unconscious)
        {
            _conditions.TryAdd(ConditionType.Incapacitated, new ActiveCondition(ConditionType.Incapacitated, sourceId, expiry));
            _conditions.TryAdd(ConditionType.Prone, new ActiveCondition(ConditionType.Prone, sourceId, expiry));
        }

        return true;
    }

    public bool RemoveCondition(ConditionType condition)
    {
        var removed = _conditions.Remove(condition);

        if (removed && condition == ConditionType.Unconscious)
        {
            _conditions.Remove(ConditionType.Incapacitated);
        }

        return removed;
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
