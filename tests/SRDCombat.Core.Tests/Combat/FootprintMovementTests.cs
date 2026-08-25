using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Moving a body rather than a point: the whole space has to fit, a step costs one
/// square whatever the body's size, occupancy is overlap, and nothing squeezes.
/// </summary>
/// <remarks>
/// Every creature production builds is one square until #429's final slice, so these
/// build their Large and Huge combatants by asking <see cref="CombatTestData.Stats"/> for
/// a size — which sets the scaffolded <c>SpaceSize</c> the grid reads.
/// </remarks>
public class FootprintMovementTests
{
    private static Combatant Large(
        string id,
        int x,
        int y,
        string sideId = CombatTestData.Heroes,
        int maximumHitPoints = 20) =>
        CombatTestData.Combatant(
            id,
            sideId: sideId,
            stats: CombatTestData.Stats(size: CreatureSize.Large, maximumHitPoints: maximumHitPoints),
            x: x,
            y: y);

    [Fact]
    public void ALargeCreatureCannotEnterAGapItsBodyDoesNotFit()
    {
        // A wall with a one-square gap at (2,2): a Medium creature threads it, a Large
        // one cannot, and there is no squeezing rule in SRD 5.2.1 to let it try.
        var wall = Enumerable.Range(0, 5)
            .Where(y => y != 2)
            .Select(y => new GridPosition(2, y))
            .ToArray();

        var field = new Battlefield(6, 5, blocked: wall);

        var medium = CombatTestData.Combatant("medium", x: 0, y: 2);
        var ogre = Large("ogre", 0, 2);

        Assert.NotNull(MovementRules.FindPath(field, medium, new GridPosition(4, 2), 60, [medium]));
        Assert.Null(MovementRules.FindPath(field, ogre, new GridPosition(4, 2), 60, [ogre]));
    }

    [Fact]
    public void AFootprintThatWouldHangOffTheBoardDoesNotFit()
    {
        var field = new Battlefield(5, 5);
        var ogre = Large("ogre", 0, 0);

        // The anchor is the north-west square, so an anchor on the last column would put
        // half the body outside the battlefield.
        Assert.False(MovementRules.SpaceFits(field, ogre.SpaceAt(new GridPosition(4, 1))));
        Assert.True(MovementRules.SpaceFits(field, ogre.SpaceAt(new GridPosition(3, 1))));
        Assert.Null(MovementRules.FindPath(field, ogre, new GridPosition(4, 1), 60, [ogre]));
    }

    [Fact]
    public void MoveRefusesAnUnfittableSquareWithItsOwnCode()
    {
        var field = new Battlefield(6, 5, blocked: [new GridPosition(3, 1)]);
        var ogre = Large("ogre", 0, 0);
        var foe = CombatTestData.Combatant("foe", sideId: CombatTestData.Monsters, x: 5, y: 4);

        var encounter = Encounter.Start(field, [ogre, foe], new ScriptedRandomSource(15, 10));

        // (2,0) is passable and empty, but the body would reach into the wall at (3,1).
        var refusal = encounter.Move(new GridPosition(2, 0));

        Assert.Equal("movement.no_room", refusal?.Code);
    }

    [Fact]
    public void AnAnchorSquareThatIsItselfAWallIsStillUnreachableRatherThanNoRoom()
    {
        // The narrowness of the new refusal, pinned: it says "you will never fit", so it
        // must not swallow the answer that says "that square is a wall".
        var field = new Battlefield(6, 5, blocked: [new GridPosition(2, 0)]);
        var medium = CombatTestData.Combatant("medium", x: 0, y: 0);
        var foe = CombatTestData.Combatant("foe", sideId: CombatTestData.Monsters, x: 5, y: 4);

        var encounter = Encounter.Start(field, [medium, foe], new ScriptedRandomSource(15, 10));

        Assert.Equal("movement.unreachable", encounter.Move(new GridPosition(2, 0))?.Code);
    }

    [Fact]
    public void AStepCostsOneSquareOfMovementWhateverTheBodysSize()
    {
        // The stated reading: five feet per step of the space, not per square of the
        // footprint. A Huge creature crossing three squares of clear ground pays 15,
        // not 135.
        var field = new Battlefield(10, 10);
        var huge = CombatTestData.Combatant(
            "tree",
            stats: CombatTestData.Stats(size: CreatureSize.Huge),
            x: 0,
            y: 0);

        var path = MovementRules.FindPath(field, huge, new GridPosition(3, 0), 30, [huge]);

        Assert.Equal(3, path?.Steps.Count);
        Assert.Equal(15, path?.CostFeet);
    }

    [Fact]
    public void OneDifficultSquareAnywhereInTheNewGroundMakesTheWholeStepDifficult()
    {
        // The body entering (2,0) and (2,1) pays double because (2,1) is rough, even
        // though (2,0) is clear — and pays it once, because Difficult Terrain does not
        // stack with itself.
        var field = new Battlefield(6, 4, difficultTerrain: [new GridPosition(2, 1)]);
        var ogre = Large("ogre", 0, 0);

        var path = MovementRules.FindPath(field, ogre, new GridPosition(2, 0), 30, [ogre]);

        Assert.Equal(2, path?.Steps.Count);
        Assert.Equal(15, path?.CostFeet);
    }

    [Fact]
    public void AMoveMayNotEndWithBodiesOverlapping()
    {
        var field = new Battlefield(10, 10);
        var ogre = Large("ogre", 0, 0);
        var foe = CombatTestData.Combatant("foe", sideId: CombatTestData.Monsters, x: 4, y: 1);

        // Anchoring at (3,0) or (3,1) would put the Ogre's east column on the foe.
        Assert.Null(MovementRules.FindPath(field, ogre, new GridPosition(3, 1), 60, [ogre, foe]));
        Assert.Null(MovementRules.FindPath(field, ogre, new GridPosition(3, 0), 60, [ogre, foe]));

        // One square short is clear, so the refusal is about the overlap and not about
        // the neighbourhood.
        Assert.NotNull(MovementRules.FindPath(field, ogre, new GridPosition(2, 1), 60, [ogre, foe]));
    }

    [Fact]
    public void AnAllysBodyBlocksThePathAsMuchAsAnEnemysWhenTheMoveWouldEndOnIt()
    {
        var field = new Battlefield(10, 10);
        var ogre = Large("ogre", 0, 0);
        var friend = CombatTestData.Combatant("friend", x: 4, y: 0);

        // Passing through an ally is printed; ending on one is not.
        Assert.Null(MovementRules.FindPath(field, ogre, new GridPosition(3, 0), 60, [ogre, friend]));
        Assert.NotNull(MovementRules.FindPath(field, ogre, new GridPosition(5, 2), 60, [ogre, friend]));
    }

    [Fact]
    public void DisplacementFindsAnAnchorTheWholeBodyFitsIn()
    {
        // A Large creature comes round underneath a Medium one. The sweep has to move
        // the one standing over it to a square its own body fits in, and the body it
        // displaces is four squares wide.
        var standing = Large("standing", 3, 3);
        var fallen = CombatTestData.Combatant(
            "fallen",
            stats: CombatTestData.Stats(diesAtZeroHitPoints: false),
            x: 3,
            y: 3);

        var foe = CombatTestData.Combatant("foe", sideId: CombatTestData.Monsters, x: 9, y: 9);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [standing, fallen, foe],
            new ScriptedRandomSource(15, 10, 1));

        DamageRules.Apply(fallen, fallen.Stats.MaximumHitPoints, DamageType.Bludgeoning);
        DamageRules.Heal(fallen, 5);
        encounter.EndTurn();

        Assert.False(standing.Space.Overlaps(fallen.Space));
        Assert.True(MovementRules.SpaceFits(encounter.Battlefield, standing.Space));
    }

    /// <summary>
    /// The stated membership reading, through the path a fight actually takes: a blast
    /// that covers one square of a Large body catches the creature. Under the anchored
    /// reading the Ogre would have stood in a Fireball and taken nothing.
    /// </summary>
    [Fact]
    public void AnAreaCatchesACreatureWhenAnySquareOfItsBodyIsInIt()
    {
        var blast = new SaveEffect(
            Ability.Dexterity,
            13,
            new EffectArea(AreaShape.Sphere, 5),
            [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Fire, 7)],
            SaveSuccessOutcome.HalfDamage,
            []);

        var roarerStats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries =
            [
                new MonsterEntry(
                    "Blast",
                    MonsterEntrySection.Action,
                    "Blast.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: blast),
            ],
        };

        var caster = CombatTestData.Combatant(
            "caster",
            sideId: CombatTestData.Monsters,
            stats: roarerStats,
            x: 0,
            y: 0);

        // Anchored at (5,5), so the body is (5,5)-(6,6). The blast is centred on (7,7),
        // whose five-foot sphere reaches (6,6) — one square of four, and not the anchor.
        var ogre = Large("ogre", 5, 5, maximumHitPoints: 60);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [caster, ogre],
            new ScriptedRandomSource(15, 10, 1, 1, 3, 3));

        Assert.Null(encounter.UseEntry("Blast", new GridPosition(7, 7)));
        Assert.True(ogre.CurrentHitPoints < ogre.Stats.MaximumHitPoints);
    }

    [Fact]
    public void ALargeTargetIsNotTotallyCoveredByAPillarThatHidesOnlyPartOfIt()
    {
        // A single-square wall on the line to the Ogre's anchor square. Its southern row
        // is in the open, so the attacker gets a shot — Total Cover refuses targeting
        // only when every square of the space is totally covered.
        var field = new Battlefield(12, 12, blocked: [new GridPosition(3, 3)]);
        var ogre = Large("ogre", 5, 3, CombatTestData.Monsters);
        var archer = CombatTestData.Combatant("archer", x: 1, y: 4);

        Assert.Equal(
            CoverDegree.Total,
            CoverRules.Between(field, archer.Position, ogre.Position, [archer, ogre]));

        Assert.NotEqual(
            CoverDegree.Total,
            CoverRules.AgainstSpace(field, archer.Space, ogre.Space, [archer, ogre]));
    }

    [Fact]
    public void ALargeCreatureDoesNotTakeCoverBehindItsOwnBody()
    {
        // The line from the archer to the Ogre's far square passes through the Ogre's
        // near square. That square is the target's own space and grants nothing.
        var field = new Battlefield(12, 12);
        var ogre = Large("ogre", 5, 3, CombatTestData.Monsters);
        var archer = CombatTestData.Combatant("archer", x: 1, y: 3);

        Assert.Equal(
            CoverDegree.None,
            CoverRules.AgainstSpace(field, archer.Space, ogre.Space, [archer, ogre]));
    }

    [Fact]
    public void ACreatureIsStillInTheWayWhenOnlyPartOfItsBodyIs()
    {
        // A Large body straddling the line grants Half Cover exactly as a Medium one on
        // the line does — all of its squares feed the computation.
        var field = new Battlefield(12, 12);
        var archer = CombatTestData.Combatant("archer", x: 1, y: 3);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 8, y: 3);
        var ogre = Large("ogre", 4, 3, CombatTestData.Monsters);

        Assert.Equal(
            CoverDegree.Half,
            CoverRules.AgainstSpace(field, archer.Space, target.Space, [archer, target, ogre]));
    }
}
