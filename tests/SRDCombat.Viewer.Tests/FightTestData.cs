using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// Hand-authored fights, for the client tests that need an <see cref="Encounter"/> to
/// ask names of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hand-authored, and no content is loaded.</b> These tests are about the client's
/// own rules — which name gets which colour, how big a figure draws — so a real stat
/// block would only make them fail when the bestiary is re-extracted. It also keeps
/// this assembly off the corpus entirely: #319 counts 27 loads across the suite, and
/// this project adds none. The same reasoning <c>CombatTestData</c> states in
/// <c>SRDCombat.Core.Tests</c>, for the same reason.
/// </para>
/// <para>
/// Deliberately smaller than <c>CombatTestData</c> rather than shared with it: that
/// type is <c>internal</c> to another test assembly, and lifting it into the shared
/// test-support project is #318's job, not this one's. What is here is the four fields
/// <see cref="LogHighlighter"/> reads and nothing else — if a third project wants the
/// same builders, that is the trigger to do #318 rather than to copy this a second
/// time.
/// </para>
/// </remarks>
internal static class FightTestData
{
    public const string Heroes = "heroes";
    public const string Monsters = "monsters";

    public static CombatAttack Attack(string name) =>
        new(
            name,
            AttackKind.Melee,
            AttackBonus: 4,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("1d8 + 2"), DamageType.Slashing, 6)]);

    public static MonsterEntry Entry(string name) =>
        new(name, MonsterEntrySection.Trait, $"{name}. Something the creature does.");

    public static CombatantFeatures Character(params ClassFeature[] features) =>
        new(
            features,
            AttacksPerAction: 1,
            SneakAttackDamage: null,
            RageDamageBonus: 0,
            RageUses: 0,
            SecondWindUses: 0,
            ActionSurgeUses: 0,
            Level: 1);

    public static CombatantStats Stats(
        IReadOnlyList<CombatAttack>? attacks = null,
        IReadOnlyList<MonsterEntry>? entries = null,
        CombatantFeatures? character = null) =>
        new(
            ArmorClass: 13,
            MaximumHitPoints: 20,
            SpeedFeet: 30,
            InitiativeBonus: 2,
            Abilities(),
            ProficiencyBonus: 2,
            CreatureSize.Medium,
            new Dictionary<DamageType, DamageResponse>(),
            [],
            attacks ?? [Attack("Sword")],
            DiesAtZeroHitPoints: true)
        {
            Entries = entries ?? [],
            Character = character,
        };

    public static Combatant Combatant(
        string name,
        string sideId = Monsters,
        CombatantStats? stats = null,
        int x = 0) =>
        new(name, name, sideId, stats ?? Stats(), new GridPosition(x, 0));

    /// <summary>A fight with the given combatants, on ground big enough to hold them.</summary>
    public static Encounter Fight(params Combatant[] combatants) =>
        Encounter.Start(new Battlefield(20, 20), combatants, new SeededRandomSource(1));

    private static Dictionary<Ability, MonsterAbility> Abilities() =>
        new()
        {
            [Ability.Strength] = new(14, 2),
            [Ability.Dexterity] = new(14, 2),
            [Ability.Constitution] = new(14, 2),
            [Ability.Intelligence] = new(10, 0),
            [Ability.Wisdom] = new(10, 0),
            [Ability.Charisma] = new(10, 0),
        };
}
