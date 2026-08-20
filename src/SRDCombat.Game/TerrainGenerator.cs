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
/// <b>Obstacles come in clusters, Difficult Terrain in patches.</b> Up to three obstacle
/// clusters of one to three squares — each cluster drawn as either a wall (Total Cover)
/// or a low obstacle (Half Cover, shot over rather than blocking), a coin flip per
/// cluster — and up to two patches of one to four. A draw can also produce a bare field,
/// on purpose: variety includes the plain, and the numbers are small because the fields
/// are — a generated battlefield is nine squares wide.
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

        var obstacleClusters = random.Roll(4) - 1;

        for (var cluster = 0; cluster < obstacleClusters; cluster++)
        {
            var kind = random.Roll(2) == 1 ? walls : lowObstacles;
            var current = DrawSquare();
            var size = random.Roll(3);

            for (var grown = 0; grown < size; grown++)
            {
                if (InRegion(current) && !impassable.Contains(current)
                    && StaysConnected(impassable, current, spawnSet, width, height))
                {
                    kind.Add(current);
                    impassable.Add(current);
                }

                current = Step(current);
            }
        }

        var difficultPatches = random.Roll(3) - 1;

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
    /// Whether every spawn square can still reach every other with this obstacle added.
    /// </summary>
    private static bool StaysConnected(
        HashSet<GridPosition> impassable,
        GridPosition candidate,
        HashSet<GridPosition> spawns,
        int width,
        int height)
    {
        if (spawns.Count <= 1)
        {
            return true;
        }

        var reached = new HashSet<GridPosition> { spawns.First() };
        var frontier = new Queue<GridPosition>(reached);

        while (frontier.Count > 0)
        {
            foreach (var next in frontier.Dequeue().Neighbours())
            {
                if (next.X < 0 || next.X >= width || next.Y < 0 || next.Y >= height
                    || next == candidate || impassable.Contains(next) || !reached.Add(next))
                {
                    continue;
                }

                frontier.Enqueue(next);
            }
        }

        return spawns.All(reached.Contains);
    }
}
