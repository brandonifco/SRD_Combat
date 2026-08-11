using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

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
    IReadOnlyList<AttackDamage> Damage)
{
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
public sealed record CombatantFeatures(
    IReadOnlyList<ClassFeature> Features,
    int AttacksPerAction,
    DiceExpression? SneakAttackDamage,
    int RageDamageBonus,
    int RageUses,
    int SecondWindUses,
    int ActionSurgeUses,
    int Level)
{
    public bool Has(ClassFeature feature) => Features.Contains(feature);
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
        int actionSurgeUses = 0)
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
                sheet.Level),
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
                entry.Attack.Damage))
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
            DiesAtZeroHitPoints: true);
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

    internal void BeginTurn()
    {
        SneakAttackUsedThisTurn = false;
        IsRecklessThisTurn = false;
        AttackedThisTurn = false;
        AttacksRemainingThisAction = 0;
    }
}

/// <summary>A creature taking part in a fight, and everything about it that changes.</summary>
public sealed class Combatant
{
    private readonly HashSet<ConditionType> _conditions = [];

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

    public IReadOnlyCollection<ConditionType> Conditions => _conditions;

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

    public bool HasCondition(ConditionType condition) => _conditions.Contains(condition);

    /// <summary>Adds a condition unless the creature is immune to it.</summary>
    public bool AddCondition(ConditionType condition)
    {
        if (Stats.ConditionImmunities.Contains(condition))
        {
            return false;
        }

        var added = _conditions.Add(condition);

        // Unconscious brings Incapacitated and Prone with it, per the condition's own
        // definition. Modelling that here means nothing else has to remember it.
        if (added && condition == ConditionType.Unconscious)
        {
            _conditions.Add(ConditionType.Incapacitated);
            _conditions.Add(ConditionType.Prone);
        }

        return added;
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
