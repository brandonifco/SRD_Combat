using SRDCombat.Core.Combat;

namespace SRDCombat.Core.Tests.Combat;

public class BattlefieldTests
{
    [Theory]
    [InlineData(0, 0, 1, 0, 5)]
    [InlineData(0, 0, 3, 0, 15)]
    // The rule worth pinning: a diagonal step costs the same as an orthogonal one, so
    // three squares diagonally is 15 feet, not 30 and not 21.
    [InlineData(0, 0, 3, 3, 15)]
    [InlineData(0, 0, 3, 1, 15)]
    [InlineData(2, 2, 2, 2, 0)]
    public void DistanceFeetTo_CountsSquaresByTheShortestRoute(int x1, int y1, int x2, int y2, int expected) =>
        Assert.Equal(expected, new GridPosition(x1, y1).DistanceFeetTo(new GridPosition(x2, y2)));

    [Fact]
    public void IsAdjacentTo_IncludesDiagonalsButNotItself()
    {
        var origin = new GridPosition(2, 2);

        Assert.True(origin.IsAdjacentTo(new GridPosition(3, 3)));
        Assert.True(origin.IsAdjacentTo(new GridPosition(2, 1)));
        Assert.False(origin.IsAdjacentTo(origin));
        Assert.False(origin.IsAdjacentTo(new GridPosition(4, 2)));
    }

    [Fact]
    public void Neighbours_AreTheEightSurroundingSquares() =>
        Assert.Equal(8, new GridPosition(5, 5).Neighbours().Distinct().Count());

    [Fact]
    public void IsPassable_ExcludesOutOfBoundsAndBlockedSquares()
    {
        var field = new Battlefield(4, 4, blocked: [new GridPosition(1, 1)]);

        Assert.True(field.IsPassable(new GridPosition(0, 0)));
        Assert.False(field.IsPassable(new GridPosition(1, 1)));
        Assert.False(field.IsPassable(new GridPosition(4, 0)));
        Assert.False(field.IsPassable(new GridPosition(-1, 0)));
    }

    [Fact]
    public void EnterCostFeet_DoublesForDifficultTerrain()
    {
        var field = new Battlefield(4, 4, difficultTerrain: [new GridPosition(2, 2)]);

        Assert.Equal(5, field.EnterCostFeet(new GridPosition(0, 0)));
        Assert.Equal(10, field.EnterCostFeet(new GridPosition(2, 2)));
    }

    [Fact]
    public void AllSquares_CoversTheGrid() => Assert.Equal(12, new Battlefield(4, 3).AllSquares().Count());

    [Fact]
    public void Pieces_DefaultsToEmptyWhenNoneAreGiven() => Assert.Empty(new Battlefield(4, 4).Pieces);

    [Fact]
    public void Pieces_CarriesWhateverTheCallerPasses()
    {
        var piece = new TerrainPiece(
            TerrainPieceKind.WallRun,
            [new GridPosition(1, 1), new GridPosition(1, 2)],
            SiteType.CentralWall);

        var field = new Battlefield(4, 4, pieces: [piece]);

        Assert.Single(field.Pieces);
        Assert.Same(piece, field.Pieces[0]);

        // Description only, per the class remarks: a piece the caller never reflected
        // into the rules-authority sets does not make those squares impassable.
        Assert.True(field.IsPassable(new GridPosition(1, 1)));
    }
}
