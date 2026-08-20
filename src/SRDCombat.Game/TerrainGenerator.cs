using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game;

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
/// <b>Terrain sits strictly between the outermost spawns.</b> Features land only on
/// columns between the leftmost and rightmost spawn columns, exclusive of both, and never
/// on a spawn square, so nobody starts inside a wall, walled off, or with their first
/// step already taxed. For the classic two-column layout that band is exactly the ground
/// between the sides; for a corner or surrounded layout it is the contested middle the
/// spawns enclose. How far apart the sides begin is the layout's decision
/// (<c>EncounterFactory.StartingSeparationFeet</c> and its <c>BattleLayout</c>), and
/// terrain must not quietly remake it.
/// </item>
/// <item>
/// <b>Obstacles are whole footprints, Difficult Terrain comes in patches.</b> Three to
/// six obstacle attempts — each drawn as either a wall (Total Cover, 2×4 squares
/// upright or 4×2 lying across the field, a second coin flip per wall, 2026-08-20 at
/// Brandon's direction with the landscape wall art) or a
/// low obstacle (Half Cover, shot over rather than blocking, 2×2 squares), a coin flip
/// per obstacle — and up to three patches of one to four. The attempt counts were
/// raised from 0–3 and 0–2 on 2026-08-20, after play found the fields too sparse: with
/// rejection, the old dial landed a mean of one footprint per field and a third of
/// fields bare, and the new one lands two to three on most fields with four or five
/// possible. The footprint sizes are the drawn
/// art's own (2026-08-20, at Brandon's direction): a rock wall or a tree blocks every
/// square its picture covers, which is why placement is all-or-nothing — a footprint
/// that cannot land whole lands nowhere, so a partial obstacle can never contradict its
/// art. Footprints also never touch each other, orthogonally or by kind, so a client
/// can recover each one from the blocked squares as a connected component. A draw can
/// still produce a bare field — rejection can refuse every attempt — but it is rare
/// rather than a third of draws: variety includes the plain, sparingly.
/// </item>
/// <item>
/// <b>Every fight stays winnable on foot.</b> An obstacle square — wall or low, both
/// being impassable — whose placement would cut any spawn square off from any other is
/// discarded rather than placed, checked one square at a time, so the guarantee holds
/// whatever the dice drew. Both sides field melee-only creatures, and a fight the sides
/// cannot reach each other in is not a fight.
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
    /// <summary>Builds a battlefield of the given size with seeded terrain on it.</summary>
    /// <param name="width">Squares across.</param>
    /// <param name="height">Squares down.</param>
    /// <param name="spawns">
    /// Every square a combatant will start on. Terrain avoids them, and walls may never
    /// disconnect them from each other.
    /// </param>
    /// <param name="random">The seeded dice the whole fight runs on.</param>
    public static Battlefield Generate(
        int width,
        int height,
        IReadOnlyCollection<GridPosition> spawns,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(spawns);
        ArgumentNullException.ThrowIfNull(random);

        var spawnSet = new HashSet<GridPosition>(spawns);

        // The band strictly between the sides' columns. A degenerate band — the sides
        // adjacent, or a single spawn column — means a bare field, not an exception.
        var regionMinX = spawnSet.Count > 0 ? spawnSet.Min(square => square.X) + 1 : 1;
        var regionMaxX = spawnSet.Count > 0 ? spawnSet.Max(square => square.X) - 1 : 0;

        if (regionMaxX < regionMinX)
        {
            return new Battlefield(width, height);
        }

        var walls = new HashSet<GridPosition>();
        var lowObstacles = new HashSet<GridPosition>();
        var impassable = new HashSet<GridPosition>();
        var difficult = new HashSet<GridPosition>();

        bool InRegion(GridPosition square) =>
            square.X >= regionMinX && square.X <= regionMaxX
            && square.Y >= 0 && square.Y < height
            && !spawnSet.Contains(square);

        GridPosition DrawSquare() => new(
            regionMinX + random.Roll(regionMaxX - regionMinX + 1) - 1,
            random.Roll(height) - 1);

        // One orthogonal step: 1 north, 2 east, 3 south, 4 west.
        GridPosition Step(GridPosition from) => random.Roll(4) switch
        {
            1 => new GridPosition(from.X, from.Y - 1),
            2 => new GridPosition(from.X + 1, from.Y),
            3 => new GridPosition(from.X, from.Y + 1),
            _ => new GridPosition(from.X - 1, from.Y),
        };

        var obstacleCount = random.Roll(4) + 2;

        for (var obstacle = 0; obstacle < obstacleCount; obstacle++)
        {
            // The dice are consumed identically whether or not the footprint lands, so
            // one rejection never re-times every draw after it. A wall rolls one die
            // more than a low obstacle — its orientation — which is still a fixed
            // pattern per kind, and the kind itself comes off the same stream.
            var isWall = random.Roll(2) == 1;
            var isWallHorizontal = isWall && random.Roll(2) == 1;
            var anchor = DrawSquare();
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
            // block placed without cutting any spawn off from any other.
            var lands = footprint.All(square =>
                    InRegion(square)
                    && !impassable.Contains(square)
                    && !square.Neighbours().Any(impassable.Contains))
                && StaysConnected(impassable, footprint, spawnSet, width, height);

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
        }

        var difficultPatches = random.Roll(4) - 1;

        for (var patch = 0; patch < difficultPatches; patch++)
        {
            var current = DrawSquare();
            var size = random.Roll(4);

            for (var grown = 0; grown < size; grown++)
            {
                if (InRegion(current) && !impassable.Contains(current))
                {
                    difficult.Add(current);
                }

                current = Step(current);
            }
        }

        return new Battlefield(width, height, walls, difficult, lowObstacles);
    }

    /// <summary>
    /// Whether every spawn square can still reach every other with this whole footprint
    /// added.
    /// </summary>
    private static bool StaysConnected(
        HashSet<GridPosition> impassable,
        IReadOnlyCollection<GridPosition> candidates,
        HashSet<GridPosition> spawns,
        int width,
        int height)
    {
        if (spawns.Count <= 1)
        {
            return true;
        }

        var candidateSet = new HashSet<GridPosition>(candidates);
        var reached = new HashSet<GridPosition> { spawns.First() };
        var frontier = new Queue<GridPosition>(reached);

        while (frontier.Count > 0)
        {
            foreach (var next in frontier.Dequeue().Neighbours())
            {
                if (next.X < 0 || next.X >= width || next.Y < 0 || next.Y >= height
                    || candidateSet.Contains(next) || impassable.Contains(next) || !reached.Add(next))
                {
                    continue;
                }

                frontier.Enqueue(next);
            }
        }

        return spawns.All(reached.Contains);
    }
}
