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
    /// Two 3 by 3 clearings that share exactly one corner square. Both are legal anchors
    /// for a Huge creature and both cover the shared square, but no Huge creature can
    /// cross between them — so the squares are not connected, whichever order they are
    /// asked in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The relation this check implements is "one component reaches all of them", not
    /// "the components that reach the first square, between them, reach all of them".
    /// Seeding the flood from every legal anchor covering the first square and unioning
    /// their reach gets the second, and the two only diverge from span 3 upward: at span
    /// 2 any two legal anchors covering a common square are one step apart and so already
    /// in the same component, which is why an entire suite of tests passed over the gap.
    /// </para>
    /// <para>
    /// The order-dependence is the tell, and it was live: the production caller passes a
    /// <c>HashSet</c>, so which square happened to be enumerated first decided the answer.
    /// Both orderings are pinned for that reason — a fix that merely made the two agree
    /// by seeding differently would still be answering the wrong question.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwoClearingsSharingOneCornerAreNotConnectedForAHugeCreature()
    {
        // Free: (0,0)-(2,2) and (2,2)-(4,4), meeting only at (2,2). Everything else walled.
        var blocked = new[]
        {
            new GridPosition(3, 0), new GridPosition(4, 0),
            new GridPosition(3, 1), new GridPosition(4, 1),
            new GridPosition(0, 3), new GridPosition(1, 3),
            new GridPosition(0, 4), new GridPosition(1, 4),
        };

        var shared = new GridPosition(2, 2);
        var west = new GridPosition(0, 0);
        var east = new GridPosition(4, 4);

        Assert.False(
            GridConnectivity.StaysConnected(blocked, [], [shared, west, east], 5, 5, spanSquares: 3),
            "The shared corner asked first: the two clearings' components were unioned.");

        Assert.False(
            GridConnectivity.StaysConnected(blocked, [], [west, shared, east], 5, 5, spanSquares: 3),
            "A clearing asked first: the same board must give the same answer.");

        // The board is not degenerate — each clearing on its own is genuinely connected,
        // so the falses above are about crossing between them rather than about there
        // being nowhere for a Huge creature to stand at all.
        Assert.True(GridConnectivity.StaysConnected(blocked, [], [west, shared], 5, 5, spanSquares: 3));
        Assert.True(GridConnectivity.StaysConnected(blocked, [], [shared, east], 5, 5, spanSquares: 3));
    }

    /// <summary>
    /// Why the per-component relation is byte-identical below span 3: at span 2, two legal
    /// anchors covering the same square are at most one step apart, so they are already in
    /// one component and per-component flooding cannot differ from unioning.
    /// </summary>
    [Fact]
    public void AtSpanTwoAnchorsSharingASquareAreAlwaysOneStepApart()
    {
        var square = new GridPosition(3, 3);

        var anchors = new[]
        {
            new GridPosition(2, 2), new GridPosition(3, 2),
            new GridPosition(2, 3), new GridPosition(3, 3),
        };

        Assert.All(anchors, anchor => Assert.True(new CreatureSpace(anchor, 2).Contains(square)));

        Assert.All(
            anchors.SelectMany(one => anchors.Select(other => (one, other))),
            pair => Assert.True(
                Math.Max(Math.Abs(pair.one.X - pair.other.X), Math.Abs(pair.one.Y - pair.other.Y)) <= 1));
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
