using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Squad AI slice 2 (#123): the party's shared kill priority, and who consults it.
/// </summary>
public class FocusFireTests
{
    [Fact]
    public void TheFocusTargetIsTheMostThreatPerHitPoint()
    {
        // A glass cannon (hits for ~10, holds 8) outranks a meat wall (hits for ~3,
        // holds 40): the cannon's death buys the most safety soonest, even though the
        // wall would die to fewer swings than the old weakest-first choice implies.
        var (encounter, hero) = Stage(
            cannonHitPoints: 8,
            wallHitPoints: 40);

        var focus = PartyDoctrine.FocusTarget(encounter, hero);

        Assert.Equal("cannon", focus?.Id);
    }

    [Fact]
    public void CharactersConvergeOnTheFocusTarget()
    {
        // The wall is weaker in hit points, so the old nearest-weakest choice would
        // split the party across enemies; the doctrine sends the character at the
        // cannon while both are equally attackable.
        var (encounter, hero) = Stage(cannonHitPoints: 20, wallHitPoints: 10);

        var target = PartyDoctrine.ChooseTarget(encounter, hero, NearestByOldRule(encounter, hero));

        Assert.Equal("cannon", target?.Id);
    }

    [Fact]
    public void AReachableEnemyBeatsAnUnreachableFocus()
    {
        // The cannon has walked out of the hero's reach; the wall is adjacent. A turn
        // in reach of an enemy is never spent walking, so the fallback takes the wall.
        var (encounter, hero) = Stage(cannonHitPoints: 8, wallHitPoints: 40, cannonX: 8);

        var target = PartyDoctrine.ChooseTarget(encounter, hero, NearestByOldRule(encounter, hero));

        Assert.Equal("wall", target?.Id);
    }

    [Fact]
    public void WithNothingInReach_TheWholeSideWalksAtTheSameKill()
    {
        // Both enemies out of reach: the character heads for the focus target rather
        // than its nearest, which is what makes two characters converge.
        var (encounter, hero) = Stage(
            cannonHitPoints: 8,
            wallHitPoints: 40,
            cannonX: 8,
            wallX: 6,
            heroReachOnly: true);

        var target = PartyDoctrine.ChooseTarget(encounter, hero, NearestByOldRule(encounter, hero));

        Assert.Equal("cannon", target?.Id);
    }

    [Fact]
    public void MonstersDoNotConsultTheDoctrine()
    {
        // The same shape from the monster's side of the line: ChooseTarget hands a
        // character-less actor its old nearest-weakest answer untouched.
        var (encounter, _) = Stage(cannonHitPoints: 20, wallHitPoints: 10);
        var monster = encounter.Combatants.Single(combatant => combatant.Id == "wall");

        var nearest = NearestByOldRule(encounter, monster);
        var target = PartyDoctrine.ChooseTarget(encounter, monster, nearest);

        Assert.Same(nearest, target);
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A character "hero" at (0,1) facing a hard-hitting "cannon" and a soft-hitting
    /// "wall", both in bow reach unless moved or the hero is made melee-only.
    /// </summary>
    private static (Encounter Encounter, Combatant Hero) Stage(
        int cannonHitPoints,
        int wallHitPoints,
        int cannonX = 3,
        int wallX = 2,
        bool heroReachOnly = false)
    {
        var shell = CombatTestData.Character("hero");

        var stats = shell.Stats with
        {
            InitiativeBonus = 10,
            Attacks = heroReachOnly
                ? [CombatTestData.MeleeAttack(bonus: 4)]
                : [CombatTestData.RangedAttack(bonus: 4, normalFeet: 25, longFeet: 25)],
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 1),
        };

        var hero = new Combatant("hero", "Hero", CombatTestData.Heroes, stats, new GridPosition(0, 1));

        var cannon = CombatTestData.Combatant(
            "cannon",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                maximumHitPoints: cannonHitPoints,
                initiativeBonus: -5,
                attacks: [CombatTestData.MeleeAttack("Claw", bonus: 6, damage: "2d8 + 2")]),
            x: cannonX,
            y: 1);

        var wall = CombatTestData.Combatant(
            "wall",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                maximumHitPoints: wallHitPoints,
                initiativeBonus: -10,
                attacks: [CombatTestData.MeleeAttack("Slam", bonus: 2, damage: "1d4")]),
            x: wallX,
            y: 1);

        return (
            Encounter.Start(new Battlefield(10, 3), [hero, cannon, wall], new ScriptedRandomSource(20, 5, 1)),
            hero);
    }

    /// <summary>The policy's old choice, for handing to <c>ChooseTarget</c> as the fallback.</summary>
    private static Combatant? NearestByOldRule(Encounter encounter, Combatant actor)
    {
        var enemies = encounter.EnemiesOf(actor).ToArray();

        var inReach = enemies
            .Where(enemy => actor.Stats.Attacks.Any(attack =>
                attack.CanReach(actor.Position.DistanceFeetTo(enemy.Position))))
            .ToArray();

        return (inReach.Length > 0 ? inReach : enemies)
            .OrderBy(enemy => inReach.Length > 0 ? enemy.CurrentHitPoints : 0)
            .ThenBy(enemy => actor.Position.DistanceFeetTo(enemy.Position))
            .ThenBy(enemy => enemy.CurrentHitPoints)
            .ThenBy(enemy => enemy.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
