using SRDCombat.Core.Combat;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Connectivity asked for a body rather than for a point — the check terrain generation
/// rejects a wall with, and the new stall class it exists to prevent.
/// </summary>
public class GridConnectivityTests
{
    private static GridPosition[] Column(int x, int fromY, int toY) =>
        Enumerable.Range(fromY, toY - fromY + 1).Select(y => new GridPosition(x, y)).ToArray();

    [Fact]
    public void ASingleSquareGapConnectsAMediumCreatureAndNotALargeOne()
    {
        // A wall down column 3 of a 7 by 5 field with one square open at (3,2).
        var wall = Column(3, 0, 4).Where(square => square.Y != 2).ToArray();
        var spawns = new[] { new GridPosition(0, 2), new GridPosition(6, 2) };

        Assert.True(GridConnectivity.StaysConnected(wall, [], spawns, 7, 5, spanSquares: 1));
        Assert.False(GridConnectivity.StaysConnected(wall, [], spawns, 7, 5, spanSquares: 2));
    }

    [Fact]
    public void ATwoSquareGapConnectsALargeCreatureAndNotAHugeOne()
    {
        var wall = Column(3, 0, 6).Where(square => square.Y is not (2 or 3)).ToArray();
        var spawns = new[] { new GridPosition(0, 2), new GridPosition(6, 2) };

        Assert.True(GridConnectivity.StaysConnected(wall, [], spawns, 7, 7, spanSquares: 2));
        Assert.False(GridConnectivity.StaysConnected(wall, [], spawns, 7, 7, spanSquares: 3));
    }

    /// <summary>
    /// The candidate is judged as if placed, which is what lets terrain generation reject
    /// a footprint before committing to it.
    /// </summary>
    [Fact]
    public void ACandidateFootprintIsJudgedAsIfItWereAlreadyThere()
    {
        var wall = Column(3, 0, 4).Where(square => square.Y is not (2 or 3)).ToArray();
        var spawns = new[] { new GridPosition(0, 2), new GridPosition(6, 2) };

        Assert.True(GridConnectivity.StaysConnected(wall, [], spawns, 7, 5, spanSquares: 2));

        // Plugging the second row of the gap closes it for a Large creature.
        Assert.False(GridConnectivity.StaysConnected(
            wall,
            [new GridPosition(3, 3)],
            spawns,
            7,
            5,
            spanSquares: 2));
    }

    [Fact]
    public void FewerThanTwoSquaresToConnectIsTriviallyConnected()
    {
        Assert.True(GridConnectivity.StaysConnected([], [], [], 5, 5, spanSquares: 3));
        Assert.True(GridConnectivity.StaysConnected([], [], [new GridPosition(0, 0)], 5, 5, spanSquares: 3));
    }

    [Fact]
    public void ASpawnIsServedByAComponentThatMerelyOverlapsIt()
    {
        // The spawn at (0,0) is a corner square. A Large creature cannot anchor anywhere
        // that keeps it inside the board except (0,0) itself — and that block covers the
        // spawn, which is what "overlaps" means here.
        var spawns = new[] { new GridPosition(0, 0), new GridPosition(5, 5) };

        Assert.True(GridConnectivity.StaysConnected([], [], spawns, 8, 8, spanSquares: 2));
    }

    [Fact]
    public void ASpawnWithNoRoomForTheBodyAroundItIsNotConnected()
    {
        // The documented precondition, failing loudly rather than quietly. The spawn at
        // (0,0) is a corner, so the only 2 by 2 block on the board that covers it is
        // anchored there — and one wall inside that block means no Large creature can
        // stand on the spawn at all, so the field cannot promise one a route to it. The
        // square itself stays perfectly connected for anything Medium.
        var walls = new[] { new GridPosition(1, 1) };
        var spawns = new[] { new GridPosition(0, 0), new GridPosition(5, 5) };

        Assert.True(GridConnectivity.StaysConnected(walls, [], spawns, 8, 8, spanSquares: 1));
        Assert.False(GridConnectivity.StaysConnected(walls, [], spawns, 8, 8, spanSquares: 2));
    }

    /// <summary>
    /// At one square on a side this is the flood fill it replaces, including the diagonal
    /// step — a route through a corner-to-corner gap counts, exactly as
    /// <c>GridPosition.Neighbours</c> has always allowed.
    /// </summary>
    [Fact]
    public void AtOneSquareTheDiagonalStepStillCounts()
    {
        var walls = new[]
        {
            new GridPosition(1, 0), new GridPosition(1, 2),
            new GridPosition(2, 1), new GridPosition(0, 1),
        };

        var spawns = new[] { new GridPosition(0, 0), new GridPosition(2, 2) };

        Assert.True(GridConnectivity.StaysConnected(walls, [], spawns, 5, 5, spanSquares: 1));
    }
}
