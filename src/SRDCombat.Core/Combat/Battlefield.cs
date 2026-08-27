namespace SRDCombat.Core.Combat;

/// <summary>A square on the battle grid. Each square is 5 feet.</summary>
public readonly record struct GridPosition(int X, int Y)
{
    /// <summary>
    /// Distance in feet between two squares, by the SRD's grid rules.
    /// </summary>
    /// <remarks>
    /// The SRD counts range by squares along the shortest route, and a diagonal step
    /// costs the same as an orthogonal one — so the distance is the larger of the two
    /// axis deltas, times five. This is why a creature 3 squares east and 3 squares
    /// north is 15 feet away, not 30.
    /// </remarks>
    public int DistanceFeetTo(GridPosition other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y)) * Battlefield.FeetPerSquare;

    /// <summary>True when the two squares touch, orthogonally or diagonally.</summary>
    public bool IsAdjacentTo(GridPosition other) => this != other && DistanceFeetTo(other) <= Battlefield.FeetPerSquare;

    /// <summary>The eight squares surrounding this one.</summary>
    public IEnumerable<GridPosition> Neighbours()
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0)
                {
                    yield return new GridPosition(X + dx, Y + dy);
                }
            }
        }
    }

    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// The grid a fight happens on: its bounds, the squares nothing can enter, and the
/// squares that cost extra to enter.
/// </summary>
/// <remarks>
/// <para>
/// Occupancy is deliberately <em>not</em> stored here. Which creature stands where
/// changes constantly during a fight and belongs to the encounter's mutable state; the
/// battlefield is the terrain, which does not move.
/// </para>
/// <para>
/// <see cref="Pieces"/> is pure description (battlefield-overhaul design §7): the three
/// square sets above remain the sole rules authority for cover, movement and every other
/// engine path, unchanged by this class growing a shape. A <see cref="Battlefield"/> is
/// always rebuilt from the fight's seed — <see cref="Game.EncounterFactory"/> constructs
/// one fresh per fight and it is never a field on anything <c>SaveFile</c> serializes —
/// so <see cref="Pieces"/> carries no save-format concern: a save that predates this
/// property resumes into a <see cref="Battlefield"/> regenerated from scratch, pieces
/// included, exactly like every other terrain square.
/// </para>
/// </remarks>
public sealed class Battlefield
{
    /// <summary>Every grid square is 5 feet on a side.</summary>
    public const int FeetPerSquare = 5;

    private readonly HashSet<GridPosition> _blocked;
    private readonly HashSet<GridPosition> _difficultTerrain;
    private readonly HashSet<GridPosition> _lowObstacles;
    private readonly IReadOnlyList<TerrainPiece> _pieces;

    public Battlefield(
        int width,
        int height,
        IEnumerable<GridPosition>? blocked = null,
        IEnumerable<GridPosition>? difficultTerrain = null,
        IEnumerable<GridPosition>? lowObstacles = null,
        IEnumerable<TerrainPiece>? pieces = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        _blocked = [.. blocked ?? []];
        _difficultTerrain = [.. difficultTerrain ?? []];
        _lowObstacles = [.. lowObstacles ?? []];
        _pieces = pieces is null ? [] : [.. pieces];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Full-height walls: impassable, and Total Cover to hide behind.</summary>
    public IReadOnlyCollection<GridPosition> Blocked => _blocked;

    public IReadOnlyCollection<GridPosition> DifficultTerrain => _difficultTerrain;

    /// <summary>
    /// Half-height obstacles — crates, boulders: impassable like a wall, but shot over
    /// rather than blocking outright. What crossing one is worth is
    /// <c>CoverRules</c>' business, not this class's.
    /// </summary>
    public IReadOnlyCollection<GridPosition> LowObstacles => _lowObstacles;

    /// <summary>
    /// The named structures generation placed — wall runs, low-obstacle clusters,
    /// difficult regions, gaps — for clients and tests. Description only: see the class
    /// remarks for why the three square sets above stay the rules authority. Empty for a
    /// <see cref="Battlefield"/> built with no <c>pieces</c> argument, which every
    /// pre-existing caller (test fixtures included) still is.
    /// </summary>
    public IReadOnlyList<TerrainPiece> Pieces => _pieces;

    /// <summary>True when the square exists on this battlefield.</summary>
    public bool IsInBounds(GridPosition position) =>
        position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height;

    /// <summary>True when the square exists and nothing permanently blocks it.</summary>
    public bool IsPassable(GridPosition position) =>
        IsInBounds(position) && !_blocked.Contains(position) && !_lowObstacles.Contains(position);

    /// <summary>
    /// The movement cost in feet of entering a square: 5 normally, 10 for Difficult
    /// Terrain — the SRD's "1 square, or 2 for Difficult Terrain" expressed in feet.
    /// </summary>
    public int EnterCostFeet(GridPosition position) =>
        _difficultTerrain.Contains(position) ? FeetPerSquare * 2 : FeetPerSquare;

    /// <summary>Every square on the battlefield, row by row.</summary>
    public IEnumerable<GridPosition> AllSquares()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                yield return new GridPosition(x, y);
            }
        }
    }
}
