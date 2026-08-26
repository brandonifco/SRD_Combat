using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The footprint model: the printed Creature Size and Space table, the block of squares
/// it produces, and the nearest-square distance printed page 13 asks for.
/// </summary>
public class CreatureSpaceTests
{
    [Theory]
    [InlineData(CreatureSize.Tiny, 1)]
    [InlineData(CreatureSize.Small, 1)]
    [InlineData(CreatureSize.Medium, 1)]
    [InlineData(CreatureSize.Large, 2)]
    [InlineData(CreatureSize.Huge, 3)]
    [InlineData(CreatureSize.Gargantuan, 4)]
    public void SpaceSpanSquares_IsThePrintedTable(CreatureSize size, int expected) =>
        Assert.Equal(expected, CreatureSizeRules.SpaceSpanSquares(size));

    [Fact]
    public void LargeSpace_IsTwoByTwoAnchoredAtItsNorthWestSquare()
    {
        var space = new CreatureSpace(new GridPosition(4, 7), 2);

        Assert.Equal(
            new[] { new GridPosition(4, 7), new GridPosition(5, 7), new GridPosition(4, 8), new GridPosition(5, 8) },
            space.Squares().ToArray());

        Assert.True(space.Contains(new GridPosition(5, 8)));
        Assert.False(space.Contains(new GridPosition(6, 8)));
    }

    [Fact]
    public void HugeSpace_IsNineSquares() =>
        Assert.Equal(9, new CreatureSpace(new GridPosition(0, 0), 3).Squares().Count());

    [Fact]
    public void SpanBelowOne_IsClampedRatherThanEmpty() =>
        Assert.Single(new CreatureSpace(new GridPosition(2, 2), 0).Squares());

    /// <summary>
    /// The reduction the whole slice rests on: for two one-square spaces, the
    /// nearest-square distance is exactly what <see cref="GridPosition.DistanceFeetTo"/>
    /// has always returned. Every production combatant is one square until #429's final
    /// slice, so this is what makes the rewrite of some fifty call sites inert.
    /// </summary>
    [Fact]
    public void SingleSquareSpaces_MeasureExactlyAsPositionsDo()
    {
        for (var x = 0; x < 9; x++)
        {
            for (var y = 0; y < 9; y++)
            {
                var from = new GridPosition(4, 4);
                var to = new GridPosition(x, y);

                Assert.Equal(from.DistanceFeetTo(to), CreatureSpace.Of(from).DistanceFeetTo(CreatureSpace.Of(to)));
                Assert.Equal(from.DistanceFeetTo(to), CreatureSpace.Of(from).DistanceFeetTo(to));
            }
        }
    }

    /// <summary>
    /// Printed page 13: range is counted "from a square adjacent to one of them" and
    /// stops "in the space of the other one". So every square of the ring around a Large
    /// creature's 2 by 2 space is five feet from it, whichever corner of the body it is
    /// nearest — the anchored reading would have called the far corner ten.
    /// </summary>
    [Fact]
    public void RingAroundALargeSpace_IsAllFiveFeetAway()
    {
        var ogre = new CreatureSpace(new GridPosition(5, 5), 2);

        var ring = new[]
        {
            new GridPosition(4, 4), new GridPosition(5, 4), new GridPosition(6, 4), new GridPosition(7, 4),
            new GridPosition(4, 5), new GridPosition(7, 5),
            new GridPosition(4, 6), new GridPosition(7, 6),
            new GridPosition(4, 7), new GridPosition(5, 7), new GridPosition(6, 7), new GridPosition(7, 7),
        };

        Assert.All(ring, square => Assert.Equal(5, ogre.DistanceFeetTo(square)));

        // One square further out is one square further away, on every side.
        Assert.Equal(10, ogre.DistanceFeetTo(new GridPosition(3, 3)));
        Assert.Equal(10, ogre.DistanceFeetTo(new GridPosition(8, 6)));
        Assert.Equal(10, ogre.DistanceFeetTo(new GridPosition(5, 8)));
    }

    [Fact]
    public void SpacesThatShareASquare_AreNoDistanceApart()
    {
        var ogre = new CreatureSpace(new GridPosition(5, 5), 2);

        Assert.True(ogre.Overlaps(CreatureSpace.Of(new GridPosition(6, 6))));
        Assert.Equal(0, ogre.DistanceFeetTo(new GridPosition(6, 6)));
        Assert.False(ogre.Overlaps(CreatureSpace.Of(new GridPosition(7, 6))));
    }

    [Fact]
    public void TwoLargeSpaces_MeasureBetweenTheirNearestCorners()
    {
        var west = new CreatureSpace(new GridPosition(0, 0), 2);
        var east = new CreatureSpace(new GridPosition(2, 0), 2);

        // Bodies touching: five feet apart, not the fifteen an anchor-to-anchor
        // reading of two far corners would have produced.
        Assert.Equal(5, west.DistanceFeetTo(east));
        Assert.Equal(5, east.DistanceFeetTo(west));
    }

    [Fact]
    public void CombatantSpace_FollowsItsSizeAndItsAnchor()
    {
        var ogre = CombatTestData.Combatant(
            "ogre",
            stats: CombatTestData.Stats(size: CreatureSize.Large),
            x: 5,
            y: 5);
        var hero = CombatTestData.Combatant("hero", x: 7, y: 6);

        Assert.Equal(new GridPosition(5, 5), ogre.Space.Anchor);
        Assert.Equal(2, ogre.Space.SpanSquares);

        // Beside the Ogre's east flank, which the anchored reading would have called ten.
        Assert.Equal(5, ogre.DistanceFeetTo(hero));
        Assert.Equal(5, hero.DistanceFeetTo(ogre));

        // A candidate anchor is judged as the whole body that would stand there.
        Assert.Equal(0, ogre.SpaceAt(new GridPosition(6, 6)).DistanceFeetTo(hero.Space));
    }

    /// <summary>
    /// Acceptance criterion 2 of #429: a Large creature with five feet of reach threatens
    /// the whole ring around its space, and the Opportunity Attack fires when a mover
    /// leaves that ring rather than when it leaves the ring around the anchor.
    /// </summary>
    [Fact]
    public void LargeCreature_ThreatensTheRingAroundItsWholeSpace()
    {
        var ogre = CombatTestData.Combatant(
            "ogre",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(size: CreatureSize.Large),
            x: 5,
            y: 5);
        var hero = CombatTestData.Combatant("hero", x: 7, y: 7);

        // (7,7) touches the Ogre's south-east corner and (8,8) does not, so this step
        // leaves its reach. Anchored at (5,5), (7,7) would have been out of reach
        // already and nothing would have fired.
        var provoked = MovementRules.FindOpportunityAttackers(
            hero,
            new GridPosition(7, 7),
            new GridPosition(8, 8),
            [hero, ogre]);

        Assert.Equal(new[] { "ogre" }, provoked.Select(attacker => attacker.Id).ToArray());

        // Sliding along the far side of the same ring stays in reach and provokes
        // nothing.
        Assert.Empty(MovementRules.FindOpportunityAttackers(
            hero,
            new GridPosition(7, 7),
            new GridPosition(7, 6),
            [hero, ogre]));
    }

    /// <summary>
    /// The mover's own body counts too: a Large creature is still in a threatened ring
    /// while any square of it is, so it does not provoke a step early.
    /// </summary>
    [Fact]
    public void LargeMover_ProvokesOnlyOnceItsWholeBodyLeavesTheReach()
    {
        var goblin = CombatTestData.Combatant("goblin", sideId: CombatTestData.Monsters, x: 4, y: 4);
        var ogre = CombatTestData.Combatant(
            "ogre",
            stats: CombatTestData.Stats(size: CreatureSize.Large),
            x: 5,
            y: 5);

        // The Ogre's body spans (5,5)-(6,6) and its north-west corner touches the
        // goblin. Sliding the anchor north to (5,4) puts the body at (5,4)-(6,5), whose
        // north-west corner is still beside the goblin: no Opportunity Attack, because
        // the creature has not left the reach.
        Assert.Empty(MovementRules.FindOpportunityAttackers(
            ogre,
            new GridPosition(5, 5),
            new GridPosition(5, 4),
            [ogre, goblin]));

        // Stepping east instead carries the whole body clear — (6,5)-(7,6) is ten feet
        // from the goblin — and that is what provokes.
        Assert.Equal(
            new[] { "goblin" },
            MovementRules.FindOpportunityAttackers(
                ogre,
                new GridPosition(5, 5),
                new GridPosition(6, 5),
                [ogre, goblin])
                .Select(attacker => attacker.Id)
                .ToArray());
    }
}
