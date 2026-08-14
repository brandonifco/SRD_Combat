using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Builders for combat tests.
/// </summary>
/// <remarks>
/// These are hand-authored rather than loaded from the SRD content on purpose. An
/// engine test should fail when the engine changes, not when the bestiary is
/// re-extracted — and the frozen transcript in particular would churn on every content
/// regeneration if it were built from real stat blocks.
/// </remarks>
internal static class CombatTestData
{
    public const string Heroes = "heroes";
    public const string Monsters = "monsters";

    /// <summary>A plain melee attack.</summary>
    public static CombatAttack MeleeAttack(
        string name = "Sword",
        int bonus = 4,
        int reachFeet = 5,
        string damage = "1d8 + 2",
        DamageType type = DamageType.Slashing)
    {
        var dice = DiceExpression.Parse(damage);

        return new CombatAttack(
            name,
            AttackKind.Melee,
            bonus,
            reachFeet,
            null,
            null,
            [new AttackDamage(dice, type, dice.Average)]);
    }

    /// <summary>A ranged attack with a normal and a long range band.</summary>
    public static CombatAttack RangedAttack(
        string name = "Bow",
        int bonus = 4,
        int normalFeet = 80,
        int longFeet = 320,
        string damage = "1d6 + 2",
        DamageType type = DamageType.Piercing)
    {
        var dice = DiceExpression.Parse(damage);

        return new CombatAttack(
            name,
            AttackKind.Ranged,
            bonus,
            null,
            normalFeet,
            longFeet,
            [new AttackDamage(dice, type, dice.Average)]);
    }

    public static CombatantStats Stats(
        int armorClass = 13,
        int maximumHitPoints = 20,
        int speedFeet = 30,
        int initiativeBonus = 2,
        bool diesAtZeroHitPoints = true,
        IReadOnlyList<CombatAttack>? attacks = null,
        IReadOnlyDictionary<DamageType, DamageResponse>? damageResponses = null,
        IReadOnlyList<ConditionType>? conditionImmunities = null,
        CreatureSize size = CreatureSize.Medium,
        int intelligence = 10) =>
        new(
            armorClass,
            maximumHitPoints,
            speedFeet,
            initiativeBonus,
            Abilities(intelligence),
            ProficiencyBonus: 2,
            size,
            damageResponses ?? new Dictionary<DamageType, DamageResponse>(),
            conditionImmunities ?? [],
            attacks ?? [MeleeAttack()],
            diesAtZeroHitPoints);

    public static Combatant Combatant(
        string id = "c1",
        string? name = null,
        string sideId = Heroes,
        CombatantStats? stats = null,
        int x = 0,
        int y = 0) =>
        new(id, name ?? id, sideId, stats ?? Stats(), new GridPosition(x, y));

    /// <summary>
    /// A character rather than a monster: drops to 0 hit points and starts making Death
    /// Saving Throws instead of dying outright.
    /// </summary>
    public static Combatant Character(
        string id = "hero",
        string sideId = Heroes,
        int maximumHitPoints = 20,
        int x = 0,
        int y = 0) =>
        Combatant(id, id, sideId, Stats(maximumHitPoints: maximumHitPoints, diesAtZeroHitPoints: false), x, y);

    private static Dictionary<Ability, MonsterAbility> Abilities(int intelligence = 10) =>
        new()
        {
            [Ability.Strength] = new(14, 2),
            [Ability.Dexterity] = new(14, 2),
            [Ability.Constitution] = new(14, 2),
            [Ability.Intelligence] = new(intelligence, 0),
            [Ability.Wisdom] = new(10, 0),
            [Ability.Charisma] = new(10, 0),
        };
}
