using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game;

/// <summary>
/// Everything a drawn <see cref="SiteType"/> places on the board before
/// <see cref="TerrainGenerator"/>'s dressing pass runs — the "structures, then dressing"
/// half of the battlefield-overhaul architecture (<c>docs/2026-08-25-battlefield-overhaul-design.md</c>
/// §3), S3's own two sites (<see cref="SiteType.Crossing"/> and
/// <see cref="SiteType.CentralWall"/>).
/// </summary>
/// <param name="Site">
/// The site this plan realizes. Always the type <see cref="DrawSite"/> returned, even
/// when placement rejected and every collection below is empty — a rejected structure
/// still reads as <see cref="SiteType.OpenField"/> on the board it produced, exactly as
/// design §8.1's "a room that cannot stand leaves no half-room" describes, so this field
/// exists for callers that want to know what was <em>attempted</em>, not what landed;
/// <see cref="Pieces"/> is the honest record of what landed.
/// </param>
/// <param name="Walls">Total-Cover squares this site placed.</param>
/// <param name="LowObstacles">Half-Cover squares this site placed.</param>
/// <param name="DifficultTerrain">Difficult Terrain squares this site placed.</param>
/// <param name="ProtectedSquares">
/// Carved gaps and fords — never impassable, and off limits to the dressing pass that
/// follows, so it can never wall up what the site deliberately left open.
/// </param>
/// <param name="Pieces">
/// The named structures this site placed, for <see cref="Battlefield.Pieces"/>. Empty
/// when placement rejected.
/// </param>
public sealed record SitePlan(
    SiteType Site,
    IReadOnlyCollection<GridPosition> Walls,
    IReadOnlyCollection<GridPosition> LowObstacles,
    IReadOnlyCollection<GridPosition> DifficultTerrain,
    IReadOnlyCollection<GridPosition> ProtectedSquares,
    IReadOnlyList<TerrainPiece> Pieces)
{
    /// <summary>The no-structure plan: dressing only, nothing threaded into it.</summary>
    public static SitePlan Empty(SiteType site) => new(site, [], [], [], [], []);
}

/// <summary>
/// Draws the fight's site (design §3, §4) and places its primary structure —
/// S3 implements <see cref="SiteType.Crossing"/> and <see cref="SiteType.CentralWall"/>;
/// <see cref="SiteType.RuinedRooms"/> and <see cref="SiteType.BoulderFieldOrGrove"/> are
/// S4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Runs between layout and dressing</b> (design §3): <c>EncounterFactory.Assemble</c>
/// draws the site and calls <see cref="Place"/> once spawns are fitted (so the site can
/// read every reserved square, not just anchors) and before
/// <see cref="TerrainGenerator.Generate"/> — the site's squares, protected gaps/fords and
/// <see cref="TerrainPiece"/>s are threaded into that call's new <c>preplaced*</c> and
/// <c>protectedSquares</c> parameters, so dressing scatters around the structure rather
/// than the structure being carved into a dressed board after the fact.
/// </para>
/// <para>
/// <b>Site structures never touch a spawn's 3x3 clearance</b> (issue #436 point 4, the
/// same rule §5 states for dressing): every square a site would place is checked against
/// <see cref="TerrainGenerator.ClearedSquares"/> exactly as dressing's own <c>InRegion</c>
/// is, and <b>placement rejects rather than displaces a spawn</b> — a site that cannot
/// stand clear of every reserved square places nothing, and the board reads as
/// <see cref="SiteType.OpenField"/>. Difficult Terrain cannot affect connectivity (design
/// §4.5) but is still rejected out of the clearance, per the same stated rule.
/// </para>
/// <para>
/// <b>Central wall's Blocked squares are admitted whole-or-nothing</b>, span-aware (design
/// §8.1's "the check run once per structure"): every wall square the run would place
/// (Total Cover and, on a ruined draw, Half Cover — both impassable) is checked with
/// <see cref="GridConnectivity.StaysConnected"/> against every reserved square, for the
/// fight's largest body, before any of it is committed. Crossing places no candidate at
/// all into that check, because Difficult Terrain is passable and cannot fail it by
/// construction (design §4.5) — its only legality gate is the clearance rule above.
/// </para>
/// <para>
/// <b>Rejection consumes fixed dice</b> (design principle 6, §8.2): every roll a site
/// spends is drawn before any legality check runs, in a pattern that depends only on
/// values already drawn (the density draw's own "kind decides the next roll" pattern) —
/// never on whether the attempt turns out to be legal. A seed that draws a central wall
/// and fails its clearance check spends exactly the dice a seed that draws a central wall
/// and succeeds spends; only the outcome differs.
/// </para>
/// <para>
/// <b>Surrounded never draws a structural site — the implementer's-choice branch design
/// §4.6 and issue #436 point 2 both leave open.</b> The design states two options
/// ("re-roll to open field, or place as arcs of the ring... arcs preferred") and leaves
/// the choice to whoever implements S3, provided it is stated here. <b>This slice
/// re-rolls to open field</b>, not arcs: an arc is a wall or band that follows the ring's
/// curve around the party's own block, which is a materially different placement
/// algorithm from a straight run (no single centroid, no single contested rectangle, and
/// its own clearance and connectivity reasoning) rather than a parameter on this one —
/// building it now would be a second, less-tested site generator inside this slice's
/// budget. Re-roll to open field is the same "no structure, dressing only" board
/// Surrounded already draws 40% of the time (S1's density-only boards), so it costs
/// Surrounded no regression and no new risk, at the price of Surrounded never getting a
/// structural site until arcs are built. <b>Flagged for designer sign-off</b>: this is a
/// stated reading rather than the design's own preferred one, and a follow-up issue for
/// Surrounded arcs is left to file once a designer confirms the ring-arc shape (which
/// SRD reading of "the ring" an arc actually is — see the point 6.6 in the S3 PR that
/// flagged this).
/// </para>
/// </remarks>
public static class SiteGenerator
{
    /// <summary>
    /// Every carved gap and ford is at least this wide, from the first slice that carves
    /// one (design §8.1) — <c>Math.Max(2, largestSpanSquares)</c>, never the raw span
    /// parameter alone, so a Medium-only fight (<c>largestSpanSquares == 1</c>) still
    /// gets a route wide enough for a body one size larger than anything actually on the
    /// field today, per §8.1's "generated at width >= 2 from the first slice".
    /// </summary>
    internal static int GapWidth(int largestSpanSquares) => Math.Max(2, largestSpanSquares);

    /// <summary>
    /// Draws this fight's site: open field 60%, crossing 20%, central wall 20% (issue
    /// #436's catalogue for this slice — the design's own final table in §4 lands with
    /// S4's rooms and boulder fields, per the design's "weights shift to the design
    /// doc's final table when S4 lands"). Under <see cref="BattleLayout.Surrounded"/> a
    /// structural draw is overridden to <see cref="SiteType.OpenField"/> without spending
    /// an extra die — see the class remarks on why re-roll rather than arcs, and why an
    /// override rather than an actual second roll: the override is a deterministic
    /// function of the first roll and the layout, which keeps the whole draw a fixed
    /// single-roll pattern regardless of layout, the same discipline
    /// <see cref="TerrainGenerator.DrawDensity"/> already states for its own draw.
    /// </summary>
    public static SiteType DrawSite(IRandomSource random, BattleLayout layout)
    {
        ArgumentNullException.ThrowIfNull(random);

        var drawn = random.Roll(5) switch
        {
            1 or 2 or 3 => SiteType.OpenField,
            4 => SiteType.Crossing,
            _ => SiteType.CentralWall,
        };

        return layout == BattleLayout.Surrounded ? SiteType.OpenField : drawn;
    }

    /// <summary>
    /// Places <paramref name="site"/>'s primary structure, or returns
    /// <see cref="SitePlan.Empty"/> for <see cref="SiteType.OpenField"/> and for any site
    /// this slice does not yet implement (S4's rooms and boulder fields — a defensive
    /// no-op rather than an exception, since a caller passing a not-yet-implemented site
    /// is a forward-compatibility question, not a refusal-worthy one).
    /// </summary>
    /// <param name="site">What <see cref="DrawSite"/> (or a test) chose.</param>
    /// <param name="width">Battlefield width in squares.</param>
    /// <param name="height">Battlefield height in squares.</param>
    /// <param name="layout">The fight's opening shape — decides the contested band (design §4.6).</param>
    /// <param name="partyReserved">Every square any party member's body occupies.</param>
    /// <param name="monsterReserved">Every square any monster's body occupies.</param>
    /// <param name="random">The fight's own seeded dice.</param>
    /// <param name="largestSpanSquares">The biggest body that will stand on this field, in squares on a side.</param>
    public static SitePlan Place(
        SiteType site,
        int width,
        int height,
        BattleLayout layout,
        IReadOnlyList<GridPosition> partyReserved,
        IReadOnlyList<GridPosition> monsterReserved,
        IRandomSource random,
        int largestSpanSquares = 1)
    {
        ArgumentNullException.ThrowIfNull(partyReserved);
        ArgumentNullException.ThrowIfNull(monsterReserved);
        ArgumentNullException.ThrowIfNull(random);

        // Belt and braces alongside DrawSite's own override: whatever site a caller
        // passes, Surrounded never stands a structure (see the class remarks).
        if (layout == BattleLayout.Surrounded || site is SiteType.OpenField or SiteType.RuinedRooms or SiteType.BoulderFieldOrGrove)
        {
            return SitePlan.Empty(site);
        }

        var reservedSet = new HashSet<GridPosition>(partyReserved.Concat(monsterReserved));
        var clearedSquares = TerrainGenerator.ClearedSquares(reservedSet);
        var region = TerrainGenerator.ContestedRegions(layout, partyReserved, monsterReserved, width, height)[0];
        var gapWidth = GapWidth(largestSpanSquares);

        return site == SiteType.Crossing
            ? PlaceCrossing(width, height, region, reservedSet, clearedSquares, gapWidth, random)
            : PlaceCentralWall(width, height, region, reservedSet, clearedSquares, gapWidth, random, largestSpanSquares);
    }

    /// <summary>
    /// A band of Difficult Terrain spanning the board's height, 2-4 squares deep, its
    /// whole extent inside the layout's contested band (design §4.5), with 1-2 clear
    /// fords carved through it at <paramref name="gapWidth"/>. Difficult Terrain is
    /// passable, so this structure is checked only for the spawn-clearance rule — it
    /// cannot fail a connectivity check by construction (design §4.5), and none is run.
    /// </summary>
    private static SitePlan PlaceCrossing(
        int width,
        int height,
        TerrainGenerator.Region region,
        HashSet<GridPosition> reservedSet,
        HashSet<GridPosition> clearedSquares,
        int gapWidth,
        IRandomSource random)
    {
        // Fixed roll pattern, spent before any legality check: depth (2-4), the band's
        // left edge within the contested band (its whole depth stays inside the band, a
        // stronger placement than "centroid inside" but a simple, always-legible one to
        // state), how many fords (1 or 2), then that many ford positions.
        var depth = random.Roll(3) + 1;
        var startSpan = Math.Max(1, region.Width - depth + 1);
        var bandStartX = Math.Clamp(region.MinX + random.Roll(startSpan) - 1, 0, Math.Max(0, width - depth));
        var fordCount = random.Roll(2);
        var fordRanges = FordRanges(height, fordCount, gapWidth);

        if (fordRanges is null || bandStartX < 0 || bandStartX + depth > width)
        {
            return SitePlan.Empty(SiteType.Crossing);
        }

        var fordSquares = new HashSet<GridPosition>();

        foreach (var (start, end) in fordRanges)
        {
            for (var y = start; y <= end; y++)
            {
                for (var x = bandStartX; x < bandStartX + depth; x++)
                {
                    fordSquares.Add(new GridPosition(x, y));
                }
            }
        }

        var difficultSquares = new List<GridPosition>();

        for (var y = 0; y < height; y++)
        {
            for (var x = bandStartX; x < bandStartX + depth; x++)
            {
                var square = new GridPosition(x, y);

                if (!fordSquares.Contains(square))
                {
                    difficultSquares.Add(square);
                }
            }
        }

        // The clearance rule binds every square the site would place, ford squares
        // included — a ford standing on a spawn's own clearance is still a structure
        // touching that clearance.
        if (difficultSquares.Any(clearedSquares.Contains) || fordSquares.Any(clearedSquares.Contains))
        {
            return SitePlan.Empty(SiteType.Crossing);
        }

        var pieces = new List<TerrainPiece>
        {
            new(TerrainPieceKind.DifficultRegion, difficultSquares, SiteType.Crossing),
        };

        pieces.AddRange(fordRanges.Select(range => new TerrainPiece(
            TerrainPieceKind.Gap,
            Enumerable.Range(bandStartX, depth)
                .SelectMany(x => Enumerable.Range(range.Start, range.End - range.Start + 1).Select(y => new GridPosition(x, y)))
                .ToArray(),
            SiteType.Crossing)));

        return new SitePlan(SiteType.Crossing, [], [], difficultSquares, fordSquares, pieces);
    }

    /// <summary>
    /// A wall run spanning most of the board's height, its whole extent inside the
    /// layout's contested band (design §4.2), off-centre along the vertical axis when
    /// there is room to be (design principle 5), with 1-2 carved gaps at
    /// <paramref name="gapWidth"/>. A ruined variant (coin flip) turns a contiguous
    /// stretch of up to a third of the run's length into Half Cover rather than Total
    /// Cover — a degraded, shootable stretch (design §4.2) — without moving the gaps: a
    /// ruined stretch that overlaps a gap simply leaves the gap exactly as it was, since
    /// a gap square was never a wall square to begin with.
    /// </summary>
    private static SitePlan PlaceCentralWall(
        int width,
        int height,
        TerrainGenerator.Region region,
        HashSet<GridPosition> reservedSet,
        HashSet<GridPosition> clearedSquares,
        int gapWidth,
        IRandomSource random,
        int largestSpanSquares)
    {
        // "Most of the board's height": four-fifths of it, floored, never shorter than
        // what two gaps at gapWidth plus one wall square on every side of each need to
        // stand at all — the shortest run this design's own numbers (gapWidth up to a
        // handful of squares on the largest fielded body) can still carve two fords
        // into and still read as a wall rather than a doorway.
        var runLength = Math.Clamp((height * 4) / 5, Math.Min(height, (gapWidth * 2) + 3), height);

        // Fixed roll pattern: which column (within the contested band), how far off
        // centre the run sits vertically, how many gaps (1-2), whether this draw ruins a
        // stretch, and — only when it does, the same "branch already drawn decides the
        // next roll" pattern DrawDensity and the dressing loop's own wall-orientation
        // roll already use — where that stretch starts.
        var wallX = region.MinX + random.Roll(region.Width) - 1;
        var leftover = height - runLength;
        var topOffset = leftover > 0 ? random.Roll(leftover + 1) - 1 : 0;
        var runStart = topOffset;
        var gapCount = random.Roll(2);
        var gapRanges = FordRanges(runLength, gapCount, gapWidth, offset: runStart);

        var ruined = random.Roll(2) == 1;
        var ruinedLength = Math.Max(1, runLength / 3);
        var ruinedStartSpan = Math.Max(1, runLength - ruinedLength + 1);
        var ruinedStart = ruined ? runStart + random.Roll(ruinedStartSpan) - 1 : 0;

        if (gapRanges is null || wallX < 0 || wallX >= width)
        {
            return SitePlan.Empty(SiteType.CentralWall);
        }

        var gapSquares = new HashSet<GridPosition>(
            gapRanges.SelectMany(range => Enumerable.Range(range.Start, range.End - range.Start + 1))
                .Select(y => new GridPosition(wallX, y)));

        var ruinedSquares = ruined
            ? new HashSet<GridPosition>(
                Enumerable.Range(ruinedStart, ruinedLength)
                    .Select(y => new GridPosition(wallX, y))
                    .Where(square => !gapSquares.Contains(square)))
            : [];

        var wallSquares = Enumerable.Range(runStart, runLength)
            .Select(y => new GridPosition(wallX, y))
            .Where(square => !gapSquares.Contains(square) && !ruinedSquares.Contains(square))
            .ToList();

        var allImpassable = wallSquares.Concat(ruinedSquares).ToArray();

        if (allImpassable.Any(clearedSquares.Contains) || gapSquares.Any(clearedSquares.Contains))
        {
            return SitePlan.Empty(SiteType.CentralWall);
        }

        if (!GridConnectivity.StaysConnected([], allImpassable, reservedSet, width, height, largestSpanSquares))
        {
            return SitePlan.Empty(SiteType.CentralWall);
        }

        var pieces = new List<TerrainPiece>();

        if (wallSquares.Count > 0)
        {
            pieces.Add(new TerrainPiece(TerrainPieceKind.WallRun, wallSquares, SiteType.CentralWall));
        }

        if (ruinedSquares.Count > 0)
        {
            pieces.Add(new TerrainPiece(TerrainPieceKind.LowObstacleCluster, [.. ruinedSquares], SiteType.CentralWall));
        }

        pieces.AddRange(gapRanges.Select(range => new TerrainPiece(
            TerrainPieceKind.Gap,
            Enumerable.Range(range.Start, range.End - range.Start + 1).Select(y => new GridPosition(wallX, y)).ToArray(),
            SiteType.CentralWall)));

        return new SitePlan(SiteType.CentralWall, wallSquares, ruinedSquares, [], gapSquares, pieces);
    }

    /// <summary>
    /// Divides a run of <paramref name="length"/> squares (starting at
    /// <paramref name="offset"/>) into <paramref name="count"/> evenly spaced gaps of
    /// <paramref name="width"/>, each with at least one square of run on both sides —
    /// the shared geometry central wall's gaps and crossing's fords both use, since a
    /// ford is exactly a gap carved through a different kind of structure. Returns null
    /// when the run is too short to fit <paramref name="count"/> non-overlapping gaps of
    /// this width with a square of run at each end and between them, which is this
    /// method's whole legality gate — callers reject the structure rather than shrink
    /// the gap.
    /// </summary>
    private static (int Start, int End)[]? FordRanges(int length, int count, int width, int offset = 0)
    {
        // A run/band square is needed before the first gap, between every pair of gaps,
        // and after the last (count + 1 buffer squares), plus count * width for the gaps
        // themselves. Checked before any Math.Clamp below runs, so a run too short to
        // carve this many gaps at this width never hands Clamp a min above its max.
        var minimumLength = (count * width) + count + 1;

        if (length < minimumLength)
        {
            return null;
        }

        var segment = length / (count + 1);
        var maxStart = offset + length - width - 1;
        var ranges = new List<(int Start, int End)>(count);

        for (var index = 1; index <= count; index++)
        {
            var idealStart = offset + (segment * index) - (width / 2);
            var start = Math.Clamp(idealStart, offset + 1, maxStart);

            ranges.Add((start, start + width - 1));
        }

        for (var index = 1; index < ranges.Count; index++)
        {
            if (ranges[index].Start <= ranges[index - 1].End)
            {
                return null;
            }
        }

        return [.. ranges];
    }
}
