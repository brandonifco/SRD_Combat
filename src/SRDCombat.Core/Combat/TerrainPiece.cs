namespace SRDCombat.Core.Combat;

/// <summary>
/// What kind of structure a <see cref="TerrainPiece"/> is, per the battlefield-overhaul
/// vocabulary (<c>docs/2026-08-25-battlefield-overhaul-design.md</c> §7).
/// </summary>
/// <remarks>
/// A corner, a T-join and a room shell are not their own kinds: the design states them
/// as <em>compositions</em> of runs ("corners and T-joins as compositions of runs, room
/// shells (runs with carved doorways)") — a room is several <see cref="WallRun"/> pieces
/// framing a rectangle, with <see cref="Gap"/> pieces standing in for its doorways. A
/// consuming site generator recognises the composite shape by reading several pieces
/// together, not by a kind this enum would have to grow for every new arrangement.
/// </remarks>
public enum TerrainPieceKind
{
    /// <summary>
    /// A run of impassable, Total Cover squares — the design's 1×N/N×1 wall run once site
    /// generators draw one (S3+), and, in this slice, the existing dressing pass's blocky
    /// 2×4/4×2 wall footprints, which are a degenerate wall run two squares thick rather
    /// than one. See <see cref="TerrainGenerator"/>'s remarks on why dressing still maps
    /// onto this vocabulary rather than growing a fifth kind for itself.
    /// </summary>
    WallRun,

    /// <summary>
    /// A group of impassable, Half Cover squares (shot over rather than blocking) — the
    /// design's low-obstacle clusters, which may abut into organic clumps from S4 onward.
    /// This slice's dressing pass still keeps every footprint separated (see
    /// <see cref="TerrainGenerator"/>), so every cluster today is exactly one 2×2 footprint.
    /// </summary>
    LowObstacleCluster,

    /// <summary>
    /// A run or patch of Difficult Terrain — a river, a bog, scree, or (today) the
    /// dressing pass's random-walk patch. Passable, so a piece of this kind never affects
    /// <see cref="GridConnectivity"/>.
    /// </summary>
    DifficultRegion,

    /// <summary>
    /// A deliberately passable break inside a structure — a carved gap in a wall, a ford
    /// through a difficult band, a doorway in a room shell. No site generator produces one
    /// yet (that starts at S3); the kind exists now so <see cref="TerrainPiece"/>'s shape
    /// does not have to change under S3's structures. A gap's squares are never impassable
    /// and are exactly the squares a caller would thread as <c>protectedSquares</c> into
    /// <see cref="TerrainGenerator.Generate"/> to keep dressing off them.
    /// </summary>
    Gap,
}

/// <summary>
/// Which site placed a <see cref="TerrainPiece"/>, per the site catalogue
/// (<c>docs/2026-08-25-battlefield-overhaul-design.md</c> §4). Pure description for
/// clients and tests — nothing in the engine branches on it.
/// </summary>
public enum SiteType
{
    /// <summary>
    /// The palate cleanser (design §4.1): no structure, dressing only. This slice draws
    /// no site at all (that starts at S3), and every board it generates is, by the
    /// design's own framing, exactly this site — "the current game, kept... becomes the
    /// exception rather than the rule" once S3 adds the others. Every
    /// <see cref="TerrainPiece"/> <see cref="TerrainGenerator"/> produces today is
    /// therefore placed by <see cref="OpenField"/>, not by a placeholder "no site" value —
    /// there is no board this slice can generate that the design does not already name.
    /// </summary>
    OpenField,

    /// <summary>A wall run spanning the board with carved gaps (design §4.2). S3.</summary>
    CentralWall,

    /// <summary>One to three ruined room shells (design §4.3). S4.</summary>
    RuinedRooms,

    /// <summary>Loose low-obstacle clusters leaving clear lanes (design §4.4). S4.</summary>
    BoulderFieldOrGrove,

    /// <summary>A band of Difficult Terrain with clear fords (design §4.5). S3.</summary>
    Crossing,
}

/// <summary>
/// A named structure on a <see cref="Battlefield"/> — a wall run, a low-obstacle cluster,
/// a difficult region, or a gap — described for clients and tests. Never a rules
/// authority: <see cref="Battlefield.Blocked"/>, <see cref="Battlefield.DifficultTerrain"/>
/// and <see cref="Battlefield.LowObstacles"/> remain the only squares cover, movement and
/// every engine path read (battlefield-overhaul design §7).
/// </summary>
/// <param name="Kind">What kind of structure this is.</param>
/// <param name="Squares">
/// Every square the structure occupies. Not required to be a filled rectangle or even
/// contiguous — a <see cref="TerrainPieceKind.DifficultRegion"/> patch grown by a random
/// walk can skip a square the walk crossed but rejected (already impassable, or outside
/// the eligible region), and the piece still describes exactly the squares that landed.
/// </param>
/// <param name="PlacedBy">Which site's generator placed this structure.</param>
/// <remarks>
/// <b>The never-touch rule is retired as a model constraint</b> (design §7): nothing here
/// assumes, or lets a caller assume, that two pieces' squares are disjoint or unadjacent.
/// <see cref="TerrainGenerator"/>'s dressing pass happens to keep its existing separation
/// behaviour unchanged in this slice — pieces it emits still never touch, because the
/// footprint-placement loop that decides <em>whether a square lands at all</em> is
/// untouched — but that is a property of today's caller, not of this type, and S4's
/// clusters are specified to abut on purpose.
/// </remarks>
public sealed record TerrainPiece(
    TerrainPieceKind Kind,
    IReadOnlyList<GridPosition> Squares,
    SiteType PlacedBy);
