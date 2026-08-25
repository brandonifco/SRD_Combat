using SRDCombat.Core.Combat;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Deploying bodies rather than points: intent preserved where it is legal, the nearest
/// legal anchor where it is not, and never two bodies in the same square.
/// </summary>
public class SpawnPlacementTests
{
    private static IReadOnlyList<GridPosition> Column(int x, int count, int top = 0) =>
        Enumerable.Range(0, count).Select(index => new GridPosition(x, top + index)).ToArray();

    /// <summary>
    /// The inertness this slice rests on: at one square per creature nobody moves, so
    /// every deployment in the game is exactly where it was before footprints existed.
    /// </summary>
    [Fact]
    public void AtOneSquareEachNothingMoves()
    {
        var intended = new[]
        {
            new GridPosition(8, 4), new GridPosition(8, 5), new GridPosition(8, 6),
            new GridPosition(20, 4), new GridPosition(20, 5),
        };

        var placed = SpawnPlacement.Fit(intended, [1, 1, 1, 1, 1], 28, 18);

        Assert.Equal(intended, placed);
    }

    [Fact]
    public void ABodyThatFitsWhereTheLayoutPutItDoesNotMove()
    {
        var placed = SpawnPlacement.Fit([new GridPosition(4, 4)], [3], 28, 18);

        Assert.Equal(new GridPosition(4, 4), placed[0]);
    }

    [Fact]
    public void AColumnOfLargeBodiesIsSpreadRatherThanStacked()
    {
        // The layout wants four creatures in consecutive rows. Two-square bodies cannot
        // have consecutive anchors, so three of them move — and none of them overlap.
        var placed = SpawnPlacement.Fit(Column(8, 4, top: 4), [2, 2, 2, 2], 28, 18);

        var spaces = placed.Select(anchor => new CreatureSpace(anchor, 2)).ToArray();

        Assert.All(
            spaces.SelectMany((space, index) => spaces.Skip(index + 1).Select(other => (space, other))),
            pair => Assert.False(pair.space.Overlaps(pair.other)));

        // The first one keeps the square the layout chose, so the shape is anchored
        // where it was meant to be rather than drifting as a whole.
        Assert.Equal(new GridPosition(8, 4), placed[0]);
    }

    [Fact]
    public void ABodyIsPulledInsideTheBoardRatherThanHangingOffIt()
    {
        // Anchored at the last column, a 2 by 2 body would need column 28 of a 28-wide
        // board. The nearest legal anchors are the three squares in column 26, and the
        // search's stated tie-break — lowest x, then lowest y — picks (26,3). Pinning the
        // exact square rather than merely "somewhere legal" is the point: a seed has to
        // deploy the same fight every time.
        var placed = SpawnPlacement.Fit([new GridPosition(27, 4)], [2], 28, 18);

        Assert.Equal(new GridPosition(26, 3), placed[0]);
    }

    [Fact]
    public void MixedSizesNeverOverlapEachOther()
    {
        var intended = Column(8, 6, top: 3);
        var spans = new[] { 1, 3, 1, 2, 1, 2 };

        var placed = SpawnPlacement.Fit(intended, spans, 28, 18);
        var occupied = new HashSet<GridPosition>();

        for (var index = 0; index < placed.Count; index++)
        {
            foreach (var square in new CreatureSpace(placed[index], spans[index]).Squares())
            {
                Assert.True(occupied.Add(square), $"{square} was claimed twice.");
                Assert.InRange(square.X, 0, 27);
                Assert.InRange(square.Y, 0, 17);
            }
        }
    }

    [Fact]
    public void PlacementIsDeterministic()
    {
        var intended = Column(8, 6, top: 3);
        var spans = new[] { 2, 3, 1, 2, 1, 2 };

        Assert.Equal(
            SpawnPlacement.Fit(intended, spans, 28, 18),
            SpawnPlacement.Fit(intended, spans, 28, 18));
    }

    [Fact]
    public void ABoardWithNoRoomForTheBodyIsAnExceptionRatherThanAnOverlap()
    {
        // A 2 by 2 field cannot hold two Large creatures. Silently stacking them would
        // hand the encounter an illegal opening; this is a bug report.
        Assert.Throws<InvalidOperationException>(() =>
            SpawnPlacement.Fit([new GridPosition(0, 0), new GridPosition(1, 1)], [2, 2], 2, 2));
    }

    [Fact]
    public void EverySpawnNeedsASpan() =>
        Assert.Throws<ArgumentException>(() =>
            SpawnPlacement.Fit([new GridPosition(0, 0)], [], 10, 10));
}
