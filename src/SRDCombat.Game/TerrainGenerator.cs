using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game;

/// <summary>
/// How thickly a battlefield is dressed. One tier is drawn per fight
/// (<see cref="TerrainGenerator.DrawDensity"/>) and scales the dressing pass's attempt
/// counts; it never changes what a square can mean, only how many carry one.
/// </summary>
/// <remarks>
/// Weights and target realized coverage (impassable + difficult, over every square) are
/// the design's stated numbers
/// (<c>docs/2026-08-25-battlefield-overhaul-design.md</c> §6), validated by
/// <c>TerrainDensityCoverageTests</c> rather than assumed: sparse 25% draw weight /
/// 3–6% coverage, standard 50% / 7–11%, cluttered 25% / 12–16%. The bands are a mean
/// over a seed sweep, not a per-board guarantee — a single cluttered board can still
/// tail below its band under rejection.
/// </remarks>
public enum TerrainDensity
{
    /// <summary>Today's dial, kept as the floor of the range: ~3.6% coverage, always.</summary>
    Sparse,

    /// <summary>The new midpoint: noticeably more populated without reading as clutter.</summary>
    Standard,

    /// <summary>The densest dial the property test still lets every fight stay winnable at.</summary>
    Cluttered,
}

/// <summary>
/// Scatters terrain across a generated battlefield: walls nothing can enter, low
/// obstacles to duck behind, and Difficult Terrain that costs double to cross.
/// </summary>
/// <remarks>
/// <para>
/// The SRD prints no battlefield-generation rule, so like <c>LootTable</c> this is the
/// project's own design, stated here:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The whole board is in play.</b> Terrain may land anywhere except (a) a reserved
/// square — every square any body occupies, not just its anchor, so a Large or bigger
/// creature clears its whole footprint — (b) any square orthogonally or diagonally
/// adjacent to a reserved square — a free 3×3 block around every one — and (c) protected
/// squares (none are threaded in this slice; the parameter exists for a later slice's
/// carved gaps and fords). This replaces the old rule confining terrain to the columns
/// strictly between the outermost spawns, which left the 8-square flanking margins
/// permanently bare — battlefield-overhaul design §5. Rule (b) is a genuine tightening,
/// not a restatement: the old rule only ever excluded spawn *squares*, so terrain could
/// and did (~26% of boards, per the design's own measurement) stand flush against a
/// spawn. The 3×3 clearance is load-bearing for <see cref="GridConnectivity"/>'s own
/// soundness (design §8.1): its check is only sound while every square that must stay
/// connected already has a free K×K block around it, which this clearance guarantees
/// directly.
/// </item>
/// <item>
/// <b>Dressing leans toward the contested ground.</b> Two-thirds of dressing anchors
/// (both obstacle attempts and Difficult Terrain patches) draw from the fight's
/// contested region, one-third from the whole board — design §5's "mild bias" so the
/// flanks gain texture without the middle losing primacy. The contested region is
/// layout-specific (design §4.6) and approximated here as the rectangle or rectangles
/// stated in <see cref="ContestedRegions"/>'s own remarks — a reading, not a printed
/// rule, since no acceptance test pins its exact shape. Which region a draw uses is
/// itself one roll, spent identically whether the draw lands or is rejected, so
/// rejection never re-times the dice that follow it.
/// </item>
/// <item>
/// <b>One density tier per fight.</b> A seeded draw before any placement picks
/// <see cref="TerrainDensity.Sparse"/>, <see cref="TerrainDensity.Standard"/> or
/// <see cref="TerrainDensity.Cluttered"/> (design §6) and scales the obstacle-attempt
/// and Difficult-Terrain-patch counts by it. One tier for the whole board, not a
/// per-obstacle coin flip, so a field reads coherently instead of half-sparse,
/// half-cluttered.
/// </item>
/// <item>
/// <b>Obstacles are whole footprints, Difficult Terrain comes in patches.</b> Each
/// obstacle attempt is drawn as either a wall (Total Cover, 2×4 squares upright or 4×2
/// lying across the field, a second coin flip per wall, 2026-08-20 at Brandon's
/// direction with the landscape wall art) or a low obstacle (Half Cover, shot over
/// rather than blocking, 2×2 squares), a coin flip per obstacle. The footprint sizes
/// are the drawn art's own (2026-08-20, at Brandon's direction): a rock wall or a tree
/// blocks every square its picture covers, which is why placement is all-or-nothing —
/// a footprint that cannot land whole lands nowhere, so a partial obstacle can never
/// contradict its art. Footprints also never touch each other, orthogonally or by
/// kind, so a client can recover each one from the blocked squares as a connected
/// component. The battlefield-overhaul design (§7) retires this as a <em>model</em>
/// constraint in S2 — <see cref="TerrainPiece"/> does not assume separation — but this
/// loop's own placement behaviour keeps it exactly as printed here until S4's clusters
/// actually use abutment; every board this generator draws is therefore still byte-for-
/// byte what the same seed drew before S2. A draw can still produce a bare field —
/// rejection can refuse every attempt — but it is rare rather than common: variety
/// includes the plain, sparingly.
/// </item>
/// <item>
/// <b>Every fight stays winnable on foot, by every body in it.</b> An obstacle square —
/// wall or low, both being impassable — whose placement would cut any reserved square
/// off from any other is discarded rather than placed, so the guarantee holds whatever
/// the dice drew. Both sides field melee-only creatures, and a fight the sides cannot
/// reach each other in is not a fight. The question is asked for the <em>largest body on
/// the field</em> rather than for a single square (see <see cref="GridConnectivity"/>): a
/// corridor two squares wide connects a battlefield for every character in this game and
/// wedges the Ogre following them through it, which is a stall the single-square check
/// could not see — there is no squeezing rule in this SRD (design §8.1).
/// </item>
/// </list>
/// <para>
/// All randomness comes through <see cref="IRandomSource"/>, so a seed still replays the
/// whole fight, terrain included. The dice are consumed in a fixed pattern whether or not
/// a draw is accepted, which keeps rejection deterministic too.
/// </para>
/// </remarks>
public static class TerrainGenerator
{
    /// <summary>
    /// The density multiplier applied to the base obstacle-attempt and Difficult-Terrain
    /// patch counts. Sparse is 1× — today's dial, left alone — standard and cluttered are
    /// tuned against <c>TerrainDensityCoverageTests</c>' measured realized coverage
    /// rather than guessed, because rejection (reserved clearance, footprint separation,
    /// connectivity) eats a growing share of attempts as density rises, so the target
    /// bands do not scale linearly with the multiplier.
    /// </summary>
    private static double MultiplierFor(TerrainDensity density) => density switch
    {
        TerrainDensity.Sparse => 1.0,
        TerrainDensity.Standard => 3.3,
        TerrainDensity.Cluttered => 6.7,
        _ => 1.0,
    };

    /// <summary>
    /// Draws this fight's density tier: sparse 25%, standard 50%, cluttered 25% (design
    /// §6). Exposed so a caller — chiefly the property test — can learn which tier a
    /// seed drew without re-deriving <see cref="Generate"/>'s internals: calling this
    /// with a fresh <see cref="IRandomSource"/> on the same seed reproduces the exact
    /// roll <see cref="Generate"/> spends first, because it always is the first roll a
    /// generation call makes.
    /// </summary>
    public static TerrainDensity DrawDensity(IRandomSource random) => random.Roll(4) switch
    {
        1 => TerrainDensity.Sparse,
        2 or 3 => TerrainDensity.Standard,
        _ => TerrainDensity.Cluttered,
    };

    /// <summary>Builds a battlefield of the given size with seeded terrain on it.</summary>
    /// <param name="width">Squares across.</param>
    /// <param name="height">Squares down.</param>
    /// <param name="partyReserved">
    /// Every square any party member's body occupies — every square of every footprint,
    /// not just its anchor, so a multi-square body is cleared whole.
    /// </param>
    /// <param name="monsterReserved">
    /// Every square any monster's body occupies, same shape as <paramref name="partyReserved"/>.
    /// </param>
    /// <param name="layout">
    /// The fight's opening shape, which decides where the contested ground is
    /// (<see cref="ContestedRegions"/>).
    /// </param>
    /// <param name="random">The seeded dice the whole fight runs on.</param>
    /// <param name="largestSpanSquares">
    /// The biggest body that will stand on this field, in squares on a side. One asks the
    /// old single-square question and is what a caller with no multi-square creatures
    /// should pass; anything larger makes the connectivity guarantee hold for a body that
    /// size, so a route too narrow for it is not a route.
    /// </param>
    /// <param name="protectedSquares">
    /// Squares terrain may never enter beyond the reserved clearance — empty in this
    /// slice; carried for a later slice's carved gaps and fords.
    /// </param>
    public static Battlefield Generate(
        int width,
        int height,
        IReadOnlyList<GridPosition> partyReserved,
        IReadOnlyList<GridPosition> monsterReserved,
        BattleLayout layout,
        IRandomSource random,
        int largestSpanSquares = 1,
        IReadOnlyCollection<GridPosition>? protectedSquares = null)
    {
        ArgumentNullException.ThrowIfNull(partyReserved);
        ArgumentNullException.ThrowIfNull(monsterReserved);
        ArgumentNullException.ThrowIfNull(random);

        var reservedSet = new HashSet<GridPosition>(partyReserved.Concat(monsterReserved));

        // A free 3x3 block around every reserved square: the square itself, plus its
        // eight neighbours, for every square any body occupies — not just its anchor.
        // See the class remarks on rule (b).
        var clearedSquares = new HashSet<GridPosition>(reservedSet);

        foreach (var square in reservedSet)
        {
            foreach (var neighbour in square.Neighbours())
            {
                clearedSquares.Add(neighbour);
            }
        }

        var protectedSet = protectedSquares is null
            ? new HashSet<GridPosition>()
            : new HashSet<GridPosition>(protectedSquares);

        var walls = new HashSet<GridPosition>();
        var lowObstacles = new HashSet<GridPosition>();
        var impassable = new HashSet<GridPosition>();
        var difficult = new HashSet<GridPosition>();

        // Describes every structure the loops below place, alongside (never instead of)
        // the square sets above — see Battlefield.Pieces and TerrainPiece's remarks. No
        // site draw exists yet (that starts at S3), so every piece this slice can ever
        // produce is placed by the open-field site (TerrainPieceKind and SiteType's own
        // doc comments explain why that is the design's own reading, not a stand-in).
        var pieces = new List<TerrainPiece>();

        bool InRegion(GridPosition square) =>
            square.X >= 0 && square.X < width
            && square.Y >= 0 && square.Y < height
            && !clearedSquares.Contains(square)
            && !protectedSet.Contains(square);

        var wholeBoard = new Region(0, width - 1, 0, height - 1);
        var contestedRegions = ContestedRegions(layout, partyReserved, monsterReserved, width, height);

        // Padded to the same count as the contested candidates, so choosing "whole
        // board" spends exactly the same dice as choosing "contested" — the strip pick
        // below is rolled every time a layout has more than one contested strip,
        // never only when the contested branch is taken.
        var wholeBoardRegions = Enumerable.Repeat(wholeBoard, contestedRegions.Length).ToArray();

        // Rolls per anchor, spent identically whether the draw lands or is rejected:
        // which region (two-thirds contested, one-third whole board), then — for a
        // layout whose contested ground is more than one rectangle (Surrounded's ring,
        // split into strips so a draw never wastes itself on the party's own cleared
        // block at the centre) — which strip, then x, then y within that rectangle's
        // bounds.
        GridPosition DrawAnchor()
        {
            var useContested = random.Roll(3) <= 2;
            var candidates = useContested ? contestedRegions : wholeBoardRegions;
            var region = candidates.Length == 1 ? candidates[0] : candidates[random.Roll(candidates.Length) - 1];

            return new GridPosition(
                region.MinX + random.Roll(region.Width) - 1,
                region.MinY + random.Roll(region.Height) - 1);
        }

        // One orthogonal step: 1 north, 2 east, 3 south, 4 west.
        GridPosition Step(GridPosition from) => random.Roll(4) switch
        {
            1 => new GridPosition(from.X, from.Y - 1),
            2 => new GridPosition(from.X + 1, from.Y),
            3 => new GridPosition(from.X, from.Y + 1),
            _ => new GridPosition(from.X - 1, from.Y),
        };

        var density = DrawDensity(random);
        var multiplier = MultiplierFor(density);

        var obstacleCount = (int)Math.Round((random.Roll(4) + 2) * multiplier);

        for (var obstacle = 0; obstacle < obstacleCount; obstacle++)
        {
            // The dice are consumed identically whether or not the footprint lands, so
            // one rejection never re-times every draw after it. A wall rolls one die
            // more than a low obstacle — its orientation — which is still a fixed
            // pattern per kind, and the kind itself comes off the same stream.
            var isWall = random.Roll(2) == 1;
            var isWallHorizontal = isWall && random.Roll(2) == 1;
            var anchor = DrawAnchor();
            var (footprintWidth, footprintHeight) =
                isWall ? isWallHorizontal ? (4, 2) : (2, 4) : (2, 2);

            var footprint = new List<GridPosition>(footprintWidth * footprintHeight);

            for (var dx = 0; dx < footprintWidth; dx++)
            {
                for (var dy = 0; dy < footprintHeight; dy++)
                {
                    footprint.Add(new GridPosition(anchor.X + dx, anchor.Y + dy));
                }
            }

            // All or nothing: every square legal, nothing already standing there, a
            // clear square of separation from every earlier footprint (which is what
            // lets a client recover footprints as connected components), and the whole
            // block placed without cutting any reserved square off from any other, for
            // the largest body that will walk this field.
            var lands = footprint.All(square =>
                    InRegion(square)
                    && !impassable.Contains(square)
                    && !square.Neighbours().Any(impassable.Contains))
                && GridConnectivity.StaysConnected(
                    impassable, footprint, reservedSet, width, height, largestSpanSquares);

            if (!lands)
            {
                continue;
            }

            var kind = isWall ? walls : lowObstacles;

            foreach (var square in footprint)
            {
                kind.Add(square);
                impassable.Add(square);
            }

            pieces.Add(new TerrainPiece(
                isWall ? TerrainPieceKind.WallRun : TerrainPieceKind.LowObstacleCluster,
                [.. footprint],
                SiteType.OpenField));
        }

        var difficultPatches = (int)Math.Round(Math.Max(0, random.Roll(4) - 1) * multiplier);

        for (var patch = 0; patch < difficultPatches; patch++)
        {
            var current = DrawAnchor();
            var size = random.Roll(4);

            // Deduplicated per patch only — the walk can cross its own trail, and a
            // TerrainPiece's own squares should not repeat, but two patches landing on
            // the same square (already legal for `difficult` itself, a HashSet) still
            // become two pieces: the model does not assume separation (TerrainPiece's
            // remarks).
            var patchSquares = new HashSet<GridPosition>();

            for (var grown = 0; grown < size; grown++)
            {
                if (InRegion(current) && !impassable.Contains(current))
                {
                    difficult.Add(current);
                    patchSquares.Add(current);
                }

                current = Step(current);
            }

            if (patchSquares.Count > 0)
            {
                pieces.Add(new TerrainPiece(TerrainPieceKind.DifficultRegion, [.. patchSquares], SiteType.OpenField));
            }
        }

        return new Battlefield(width, height, walls, difficult, lowObstacles, pieces);
    }

    /// <summary>A simple axis-aligned rectangle of squares, inclusive of both ends.</summary>
    private readonly record struct Region(int MinX, int MaxX, int MinY, int MaxY)
    {
        public int Width => MaxX - MinX + 1;

        public int Height => MaxY - MinY + 1;
    }

    /// <summary>
    /// The fight's contested ground, as one or more axis-aligned rectangles — the
    /// region dressing anchors are two-thirds biased toward (design §4.6, reused here
    /// for dressing per issue #433's own acceptance criteria; §4.6 itself is written
    /// for a later slice's site structures).
    /// </summary>
    /// <remarks>
    /// <para>
    /// No acceptance test pins the contested region's exact shape — only the coverage
    /// bands, the reserved clearance and connectivity are checked — so rectangles stand
    /// in for the design's true shapes (a middle third, a lane union, a ring) wherever
    /// they are not already one. This is a stated reading, not a printed rule:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>Columns:</b> the middle third of the open band strictly between the two sides'
    /// occupied columns, full height, exactly as design §4.6 states. One rectangle.
    /// </item>
    /// <item>
    /// <b>CornerGroups:</b> the whole open band strictly between the party's and the
    /// monsters' occupied columns, full height — left unnarrowed (unlike Columns)
    /// because the design calls this region the union of two approach lanes, one to
    /// each corner group, and those lanes together already span nearly the full height
    /// between the columns. One rectangle. Designer-approved reading for dressing
    /// (2026-08-26, #453); site placement (S3+) states its own CornerGroups reading
    /// per slice.
    /// </item>
    /// <item>
    /// <b>Surrounded:</b> "the ring between the party block and the monster ring",
    /// read literally as an annulus and expressed as four strips — north, south, east,
    /// west — framing the party's own bounding box out to the monster ring's radius.
    /// Framing it this way, rather than using the ring's whole bounding square, matters:
    /// the square's centre is the party block plus its own reserved clearance, so a draw
    /// landing there is guaranteed rejected, and two-thirds of every cluttered board's
    /// anchors landing on dead centre was measured to starve the tier's coverage well
    /// below its floor. Four rectangles.
    /// </item>
    /// </list>
    /// <para>
    /// Both the Surrounded ring and the Columns/CornerGroups band are read from the
    /// <em>extent</em> of each side's reserved squares (min/max over every square any
    /// body on that side occupies), never a single representative square — a multi-square
    /// body's footprint can span more than one column, and reading only its anchor (or
    /// only the first spawn in the list) would misplace the band by however far that body
    /// reaches into what should be open ground. Degenerate inputs (no party or monster
    /// squares, or the two sides' column ranges overlapping so no open band exists between
    /// them) fall back to a single whole-board rectangle.
    /// </para>
    /// </remarks>
    private static Region[] ContestedRegions(
        BattleLayout layout,
        IReadOnlyList<GridPosition> partyReserved,
        IReadOnlyList<GridPosition> monsterReserved,
        int width,
        int height)
    {
        var wholeBoard = new Region(0, width - 1, 0, height - 1);

        if (partyReserved.Count == 0 || monsterReserved.Count == 0)
        {
            return [wholeBoard];
        }

        if (layout == BattleLayout.Surrounded)
        {
            var centreX = (monsterReserved.Min(square => square.X) + monsterReserved.Max(square => square.X)) / 2;
            var centreY = (monsterReserved.Min(square => square.Y) + monsterReserved.Max(square => square.Y)) / 2;
            var ringRadius = monsterReserved.Max(square =>
                Math.Max(Math.Abs(square.X - centreX), Math.Abs(square.Y - centreY)));

            var ringMinX = Math.Max(0, centreX - ringRadius);
            var ringMaxX = Math.Min(width - 1, centreX + ringRadius);
            var ringMinY = Math.Max(0, centreY - ringRadius);
            var ringMaxY = Math.Min(height - 1, centreY + ringRadius);

            var blockMinX = partyReserved.Min(square => square.X);
            var blockMaxX = partyReserved.Max(square => square.X);
            var blockMinY = partyReserved.Min(square => square.Y);
            var blockMaxY = partyReserved.Max(square => square.Y);

            var strips = new List<Region>(4);

            if (ringMinY <= blockMinY - 1)
            {
                strips.Add(new Region(ringMinX, ringMaxX, ringMinY, blockMinY - 1));
            }

            if (blockMaxY + 1 <= ringMaxY)
            {
                strips.Add(new Region(ringMinX, ringMaxX, blockMaxY + 1, ringMaxY));
            }

            if (ringMinX <= blockMinX - 1)
            {
                strips.Add(new Region(ringMinX, blockMinX - 1, blockMinY, blockMaxY));
            }

            if (blockMaxX + 1 <= ringMaxX)
            {
                strips.Add(new Region(blockMaxX + 1, ringMaxX, blockMinY, blockMaxY));
            }

            return strips.Count > 0 ? [.. strips] : [wholeBoard];
        }

        // Columns and CornerGroups both place each side in its own column region — see
        // EncounterFactory.PlaceSides — but a multi-square body can spread across more
        // than one column once SpawnPlacement.Fit has to relocate it, so the band is
        // read from each side's full occupied range rather than a single square.
        var partyMinX = partyReserved.Min(square => square.X);
        var partyMaxX = partyReserved.Max(square => square.X);
        var monsterMinX = monsterReserved.Min(square => square.X);
        var monsterMaxX = monsterReserved.Max(square => square.X);

        int bandMinX;
        int bandMaxX;

        if (partyMaxX < monsterMinX)
        {
            bandMinX = partyMaxX + 1;
            bandMaxX = monsterMinX - 1;
        }
        else if (monsterMaxX < partyMinX)
        {
            bandMinX = monsterMaxX + 1;
            bandMaxX = partyMinX - 1;
        }
        else
        {
            // The two sides' columns overlap on the X axis — not a shape either layout
            // produces today, but not this method's call to fail on — so there is no
            // clean open band between them.
            return [wholeBoard];
        }

        if (bandMaxX < bandMinX)
        {
            return [wholeBoard];
        }

        if (layout == BattleLayout.CornerGroups)
        {
            return [new Region(bandMinX, bandMaxX, 0, height - 1)];
        }

        var bandWidth = bandMaxX - bandMinX + 1;
        var thirdWidth = Math.Max(1, bandWidth / 3);
        var midStart = bandMinX + ((bandWidth - thirdWidth) / 2);
        var midEnd = Math.Min(bandMaxX, midStart + thirdWidth - 1);

        return [new Region(midStart, midEnd, 0, height - 1)];
    }
}
