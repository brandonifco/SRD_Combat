using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The scripted fight the frozen transcript is taken from: three adventurers against
/// four raiders, across a battlefield with cover to route around and mud to slow the
/// crossing.
/// </summary>
/// <remarks>
/// <para>
/// Every combatant is hand-authored rather than loaded from the SRD bestiary. That is
/// deliberate — this fixture pins the <em>engine's</em> behaviour, and building it from
/// real stat blocks would make it churn every time the content is re-extracted, for
/// reasons that have nothing to do with combat.
/// </para>
/// <para>
/// The composition is chosen to exercise the parts most likely to break: mixed melee
/// and ranged, a reach weapon, terrain that forces pathing decisions, adventurers who
/// fall unconscious and roll Death Saving Throws rather than dying outright, and
/// raiders who die instantly at 0 hit points.
/// </para>
/// </remarks>
internal static class SkirmishScenario
{
    /// <summary>
    /// The seed the frozen transcript was recorded with.
    /// </summary>
    /// <remarks>
    /// Chosen for <em>coverage</em>, not arbitrarily: the fight it produces has to reach
    /// the interactions most likely to break, which <c>TheFightExercisesTheHardParts</c>
    /// asserts. The original 20260819 stopped qualifying when the tactics policy learned
    /// to focus fire — the adventurers began winning quickly enough that none of them
    /// went down, so the fight covered no Death Saving Throws at all. Its successor
    /// 20260869 stopped qualifying when the policy learned to use cover (#106) and the
    /// fight it produced no longer downed anybody either. The composition is unchanged
    /// both times; only the dice moved.
    /// </remarks>
    public const int Seed = 20260807;

    public static Encounter Create(IRandomSource? random = null) =>
        Encounter.Start(Field(), Combatants(), random ?? new SeededRandomSource(Seed));

    private static Battlefield Field() => new(
        width: 12,
        height: 8,
        blocked:
        [
            // A low wall in the middle of the field.
            new GridPosition(5, 2),
            new GridPosition(5, 3),
            new GridPosition(5, 4),
        ],
        difficultTerrain:
        [
            new GridPosition(4, 1),
            new GridPosition(5, 1),
            new GridPosition(6, 1),
            new GridPosition(4, 6),
            new GridPosition(5, 6),
            new GridPosition(6, 6),
        ]);

    private static IEnumerable<Combatant> Combatants() =>
    [
        Adventurer("ferrin", "Ferrin", armorClass: 17, hitPoints: 28, initiative: 1, x: 1, y: 3, attack: Longsword()),
        Adventurer("bruk", "Bruk", armorClass: 15, hitPoints: 26, initiative: 0, x: 1, y: 4, attack: Halberd()),
        Adventurer("sela", "Sela", armorClass: 14, hitPoints: 19, initiative: 3, x: 0, y: 3, attack: Shortbow()),

        Raider("raider-a", "Raider Vex", x: 10, y: 2),
        Raider("raider-b", "Raider Sap", x: 10, y: 3),
        Raider("raider-c", "Raider Nick", x: 10, y: 4),
        Archer("raider-d", "Raider Slow", x: 11, y: 3),
    ];

    private static Combatant Adventurer(
        string id,
        string name,
        int armorClass,
        int hitPoints,
        int initiative,
        int x,
        int y,
        CombatAttack attack) =>
        new(
            id,
            name,
            "the party",
            Stats(armorClass, hitPoints, 30, initiative, [attack], diesAtZeroHitPoints: false),
            new GridPosition(x, y));

    private static Combatant Raider(string id, string name, int x, int y) =>
        new(
            id,
            name,
            "the raiders",
            Stats(13, 11, 30, 2, [Scimitar()], diesAtZeroHitPoints: true),
            new GridPosition(x, y));

    private static Combatant Archer(string id, string name, int x, int y) =>
        new(
            id,
            name,
            "the raiders",
            Stats(12, 9, 30, 2, [Shortbow(bonus: 3, damage: "1d6 + 1")], diesAtZeroHitPoints: true),
            new GridPosition(x, y));

    private static CombatantStats Stats(
        int armorClass,
        int hitPoints,
        int speed,
        int initiativeBonus,
        IReadOnlyList<CombatAttack> attacks,
        bool diesAtZeroHitPoints) =>
        new(
            armorClass,
            hitPoints,
            speed,
            initiativeBonus,
            new Dictionary<Ability, MonsterAbility>
            {
                [Ability.Strength] = new(15, 2),
                [Ability.Dexterity] = new(14, 2),
                [Ability.Constitution] = new(14, 2),
                [Ability.Intelligence] = new(10, 0),
                [Ability.Wisdom] = new(11, 0),
                [Ability.Charisma] = new(10, 0),
            },
            ProficiencyBonus: 2,
            CreatureSize.Medium,
            new Dictionary<DamageType, DamageResponse>(),
            [],
            attacks,
            diesAtZeroHitPoints);

    private static CombatAttack Longsword() => Melee("Longsword", 5, 5, "1d8 + 3", DamageType.Slashing);

    // Reach 10 ft., so it can strike over the wall and threatens a wider area for
    // Opportunity Attacks than everything else on the field.
    private static CombatAttack Halberd() => Melee("Halberd", 4, 10, "1d10 + 2", DamageType.Slashing);

    private static CombatAttack Scimitar() => Melee("Scimitar", 4, 5, "1d6 + 2", DamageType.Slashing);

    private static CombatAttack Melee(string name, int bonus, int reach, string damage, DamageType type)
    {
        var dice = DiceExpression.Parse(damage);

        return new CombatAttack(name, AttackKind.Melee, bonus, reach, null, null,
            [new AttackDamage(dice, type, dice.Average)]);
    }

    private static CombatAttack Shortbow(int bonus = 5, string damage = "1d6 + 3")
    {
        var dice = DiceExpression.Parse(damage);

        return new CombatAttack(
            "Shortbow",
            AttackKind.Ranged,
            bonus,
            null,
            NormalRangeFeet: 80,
            LongRangeFeet: 320,
            [new AttackDamage(dice, DamageType.Piercing, dice.Average)]);
    }
}
