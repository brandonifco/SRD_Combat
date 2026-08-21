using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Which squares a side can see, for the fog of war.
/// </summary>
/// <remarks>
/// A display judgement rather than a rule, so what is worth pinning is the sight
/// reading itself: walls block, sight is the union of the whole side's, and the two
/// conditions that close a viewer's eyes take that viewer out of the union. The
/// judgement is <c>CoverRules.LineBlocked</c> — the same one Total Cover refuses
/// attacks with — so these tests build walls and never restate the geometry.
/// </remarks>
public class PartyVisionTests
{
    private const string Party = "party";
    private const string Monsters = "monsters";

    [Fact]
    public void AWallHidesWhatSitsBehindIt()
    {
        // A wall column at x=2 between the viewer at (0,1) and the square at (4,1).
        var field = Field(walls: [new(2, 0), new(2, 1), new(2, 2)]);
        var viewer = Combatant("viewer", Party, 0, 1);

        var visible = PartyVision.VisibleSquares(field, [viewer], Party);

        Assert.DoesNotContain(new GridPosition(4, 1), visible);
        Assert.Contains(new GridPosition(1, 1), visible);
        Assert.Contains(viewer.Position, visible);
    }

    [Fact]
    public void SightIsTheUnionOfTheWholeSides()
    {
        // The first viewer is walled off from (4,1); the second stands past the wall
        // with a clear line. One open eye anywhere on the side lights the square.
        var field = Field(walls: [new(2, 0), new(2, 1), new(2, 2)]);
        var walled = Combatant("walled", Party, 0, 1);
        var scout = Combatant("scout", Party, 3, 4);

        var visible = PartyVision.VisibleSquares(field, [walled, scout], Party);

        Assert.Contains(new GridPosition(4, 1), visible);
    }

    [Fact]
    public void ClosedEyesContributeNothing()
    {
        // The only viewer with a line past the wall is Unconscious, then Blinded, then
        // dead — each in turn takes it out of the union and the square goes dark.
        var field = Field(walls: [new(2, 0), new(2, 1), new(2, 2)]);
        var walled = Combatant("walled", Party, 0, 1);
        var scout = Combatant("scout", Party, 3, 4);

        scout.AddCondition(ConditionType.Unconscious);
        Assert.DoesNotContain(
            new GridPosition(4, 1),
            PartyVision.VisibleSquares(field, [walled, scout], Party));

        var blinded = Combatant("blinded", Party, 3, 4);
        blinded.AddCondition(ConditionType.Blinded);
        Assert.DoesNotContain(
            new GridPosition(4, 1),
            PartyVision.VisibleSquares(field, [walled, blinded], Party));

        var dead = Combatant("dead", Party, 3, 4);
        DamageRules.Apply(dead, 1_000, DamageType.Slashing);
        Assert.True(dead.IsDead);
        Assert.DoesNotContain(
            new GridPosition(4, 1),
            PartyVision.VisibleSquares(field, [walled, dead], Party));
    }

    [Fact]
    public void OnlyTheAskedSideIsConsulted()
    {
        // A monster with a perfect view of (4,1) lights nothing for the party.
        var field = Field(walls: [new(2, 0), new(2, 1), new(2, 2)]);
        var walled = Combatant("walled", Party, 0, 1);
        var enemy = Combatant("enemy", Monsters, 4, 4);

        var visible = PartyVision.VisibleSquares(field, [walled, enemy], Party);

        Assert.DoesNotContain(new GridPosition(4, 1), visible);
    }

    [Fact]
    public void CreaturesAreNotWalls()
    {
        // A line of bodies between viewer and square: crowds grant cover, never
        // darkness — the square stays lit.
        var field = Field(walls: []);
        var viewer = Combatant("viewer", Party, 0, 1);
        var nearBody = Combatant("near", Monsters, 1, 1);
        var farBody = Combatant("far", Monsters, 2, 1);

        var visible = PartyVision.VisibleSquares(field, [viewer, nearBody, farBody], Party);

        Assert.Contains(new GridPosition(4, 1), visible);
    }

    private static Battlefield Field(IReadOnlyCollection<GridPosition> walls) =>
        new(6, 6, walls);

    private static Combatant Combatant(string id, string side, int x, int y)
    {
        var abilities = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => new MonsterAbility(12, 1));

        return new Combatant(
            id,
            id,
            side,
            new CombatantStats(
                14, 30, 30, 0, abilities, 2, CreatureSize.Medium,
                new Dictionary<DamageType, DamageResponse>(), [], [],
                DiesAtZeroHitPoints: side == Monsters),
            new GridPosition(x, y));
    }
}
