namespace SRDCombat.Core.Combat;

/// <summary>
/// Whether a set of squares can still reach one another once something is placed on the
/// grid — asked for a creature of a given size, not for a point.
/// </summary>
/// <remarks>
/// <para>
/// Terrain generation needs this: a fight both sides cannot reach each other in is not a
/// fight, so an obstacle that would cut one spawn off from another is discarded rather
/// than placed. That check used to be a private helper inside <c>TerrainGenerator</c> that
/// walked single squares, which answers the question for a Medium creature and nobody
/// else. A corridor two squares wide connects a battlefield for every character in this
/// game and wedges the Ogre following them through it — the new stall class #429 names.
/// </para>
/// <para>
/// <b>The generalization is erosion.</b> A creature of span <c>K</c> stands somewhere when
/// the whole <c>K × K</c> block anchored there is free, so the squares it can occupy are
/// the free space eroded by its own footprint. Two of those anchors are one step apart
/// when both are legal and their anchors touch — the same eight-way step
/// <see cref="MovementRules.FindPath"/> takes — so "can a span-K creature get from here to
/// there" is connectivity over the eroded set. At <c>K = 1</c> the erosion is the identity
/// and this is exactly the single-square flood fill it replaces.
/// </para>
/// <para>
/// <b>The squares that must stay connected are single squares, not anchors</b>, and they
/// are served by a component when any legal anchor in it <em>overlaps</em> them: a spawn
/// square is where a creature stands, and a Huge creature standing on it occupies eight
/// other squares as well. <b>One component has to serve all of them</b> — the point of the
/// question is that a creature can walk from any of these squares to any other, and a
/// square each of two unconnected components reach is a square a creature cannot get
/// between. Unioning the reach of several components would answer a different question and
/// answer it wrongly: two 3 by 3 clearings sharing a single corner square each have a legal
/// anchor covering that square, so the union covers both clearings while no Huge creature
/// can cross from one to the other. <b>This is only sound while every such square has a free
/// <c>K × K</c> block around it</b> — otherwise a spawn could be unreachable by construction
/// and the check would reject every draw. Both callers guarantee it and must keep doing so:
/// <c>TerrainGenerator</c> never places terrain on a spawn square and is handed every
/// square of every reserved footprint, and the battlefield overhaul's spawn-clearance rule
/// guarantees the same block directly. A caller that stops guaranteeing it will see the
/// symptom as bare fields rather than as a wrong answer.
/// </para>
/// </remarks>
public static class GridConnectivity
{
    /// <summary>
    /// Whether every square in <paramref name="mustConnect"/> can still reach every other,
    /// for a creature <paramref name="spanSquares"/> squares on a side, once
    /// <paramref name="candidates"/> is added to <paramref name="impassable"/>.
    /// </summary>
    /// <param name="impassable">Squares nothing can enter — walls and low obstacles already placed.</param>
    /// <param name="candidates">The footprint being considered, judged as if it were placed.</param>
    /// <param name="mustConnect">
    /// The squares that must remain mutually reachable — spawn squares. Fewer than two is
    /// trivially connected.
    /// </param>
    /// <param name="width">Battlefield width in squares.</param>
    /// <param name="height">Battlefield height in squares.</param>
    /// <param name="spanSquares">
    /// The size of creature the route has to admit, in squares on a side. One asks the
    /// question for a Medium creature; <c>MonsterPool.LargestSpan</c> is what a caller
    /// should derive it from rather than hardcoding a number that goes stale.
    /// </param>
    public static bool StaysConnected(
        IReadOnlyCollection<GridPosition> impassable,
        IReadOnlyCollection<GridPosition> candidates,
        IReadOnlyCollection<GridPosition> mustConnect,
        int width,
        int height,
        int spanSquares = 1)
    {
        ArgumentNullException.ThrowIfNull(impassable);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(mustConnect);

        if (mustConnect.Count <= 1)
        {
            return true;
        }

        var span = Math.Max(1, spanSquares);
        var blocked = new HashSet<GridPosition>(impassable);
        blocked.UnionWith(candidates);

        bool Fits(GridPosition anchor) =>
            anchor.X >= 0
            && anchor.Y >= 0
            && anchor.X + span <= width
            && anchor.Y + span <= height
            && !new CreatureSpace(anchor, span).Squares().Any(blocked.Contains);

        // Start from wherever a creature of this size could stand on the first square that
        // has to connect. The anchors of a K x K block covering it run from K-1 back, and
        // there can be several — up to K squared of them.
        var first = mustConnect.First();
        var seeds = AnchorsCovering(first, span).Where(Fits).ToArray();

        // Each seed's component is flooded and asked the question on its own. Unioning
        // their reach instead would answer a different and wrong question: at span 3 two
        // legal anchors covering the same square can be two apart, so they can sit in
        // different components of the eroded space — two 3 by 3 clearings that share one
        // corner square, which no Huge creature can actually cross between. Their union
        // covers everything and connects nothing, and because the seeds come from the
        // *first* square in the caller's collection, the answer would depend on the order
        // a HashSet happened to enumerate. One component has to reach all of it.
        var assigned = new HashSet<GridPosition>();

        foreach (var seed in seeds)
        {
            // Already flooded as part of an earlier seed's component.
            if (!assigned.Add(seed))
            {
                continue;
            }

            var frontier = new Queue<GridPosition>();
            frontier.Enqueue(seed);

            var covered = new HashSet<GridPosition>(new CreatureSpace(seed, span).Squares());

            while (frontier.TryDequeue(out var current))
            {
                foreach (var next in current.Neighbours())
                {
                    if (!Fits(next) || !assigned.Add(next))
                    {
                        continue;
                    }

                    covered.UnionWith(new CreatureSpace(next, span).Squares());
                    frontier.Enqueue(next);
                }
            }

            if (mustConnect.All(covered.Contains))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every anchor whose span-sided block covers this square.</summary>
    private static IEnumerable<GridPosition> AnchorsCovering(GridPosition square, int span)
    {
        for (var dy = 0; dy < span; dy++)
        {
            for (var dx = 0; dx < span; dx++)
            {
                yield return new GridPosition(square.X - dx, square.Y - dy);
            }
        }
    }
}
