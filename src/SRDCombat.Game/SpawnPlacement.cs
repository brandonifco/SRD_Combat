using SRDCombat.Core.Combat;

namespace SRDCombat.Game;

/// <summary>
/// Turns a layout's intended spawn squares into anchors whole bodies actually fit on.
/// </summary>
/// <remarks>
/// <para>
/// <b>A seam rather than a rewrite, on purpose.</b> <see cref="BattleLayout"/> and
/// <c>EncounterFactory.PlaceSides</c> decide the <em>shape</em> of an opening — two facing
/// columns, two corner groups, a party surrounded — and that is a design decision with its
/// own measured history. Footprints are a separate question: whichever shape was drawn,
/// nobody may start half inside a neighbour or half off the board. Keeping the two apart
/// means the battlefield overhaul can replace the shapes without touching the legality
/// rule, and this rule can grow without relitigating the shapes.
/// </para>
/// <para>
/// <b>Intent is preserved wherever it is already legal.</b> A creature whose body fits
/// where the layout put it does not move at all, so at one square per creature — which is
/// every fight until #429's final slice — this returns its input unchanged and no
/// deployment shifts by a square. Only a creature whose body will not fit is relocated,
/// and then to the nearest anchor that works, searched outward from where the layout meant
/// it to be, so the shape survives as closely as the bodies allow.
/// </para>
/// <para>
/// <b>Deterministic, because a seed replays.</b> The search visits neighbours in a fixed
/// order and creatures are placed in the order the layout listed them, so the same fight
/// deploys the same way every time. No dice are consumed here at all, which is what keeps
/// the terrain draw that follows aligned with the dice stream.
/// </para>
/// </remarks>
public static class SpawnPlacement
{
    /// <summary>
    /// Anchors for each creature: the intended square where the body fits there, the
    /// nearest legal anchor to it where it does not.
    /// </summary>
    /// <param name="intended">Where the layout wants each creature, in order.</param>
    /// <param name="spans">Each creature's space, in squares on a side, in the same order.</param>
    /// <param name="width">Battlefield width in squares.</param>
    /// <param name="height">Battlefield height in squares.</param>
    /// <exception cref="InvalidOperationException">
    /// When a creature's body fits nowhere on the battlefield that is not already taken.
    /// Thrown rather than clamped: a fight whose creatures cannot be deployed is not a
    /// fight, and quietly stacking two bodies would hand the encounter an illegal opening
    /// that every later rule would have to survive. The board is sized for its creatures,
    /// so this is a bug report rather than a game state.
    /// </exception>
    public static IReadOnlyList<GridPosition> Fit(
        IReadOnlyList<GridPosition> intended,
        IReadOnlyList<int> spans,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(intended);
        ArgumentNullException.ThrowIfNull(spans);

        if (intended.Count != spans.Count)
        {
            throw new ArgumentException("Every spawn needs a span.", nameof(spans));
        }

        var taken = new HashSet<GridPosition>();
        var placed = new GridPosition[intended.Count];

        for (var index = 0; index < intended.Count; index++)
        {
            var span = Math.Max(1, spans[index]);
            var anchor = Nearest(intended[index], span, width, height, taken)
                ?? throw new InvalidOperationException(
                    $"No room on a {width} by {height} battlefield for a {span} by {span} creature near {intended[index]}.");

            placed[index] = anchor;

            foreach (var square in new CreatureSpace(anchor, span).Squares())
            {
                taken.Add(square);
            }
        }

        return placed;
    }

    /// <summary>
    /// The intended square if the body fits there, otherwise the nearest square it does,
    /// searched outward. Null when the battlefield has no room at all.
    /// </summary>
    private static GridPosition? Nearest(
        GridPosition intended,
        int span,
        int width,
        int height,
        HashSet<GridPosition> taken)
    {
        bool Fits(GridPosition anchor) =>
            anchor.X >= 0
            && anchor.Y >= 0
            && anchor.X + span <= width
            && anchor.Y + span <= height
            && !new CreatureSpace(anchor, span).Squares().Any(taken.Contains);

        if (Fits(intended))
        {
            return intended;
        }

        var seen = new HashSet<GridPosition> { intended };
        var queue = new Queue<GridPosition>();
        queue.Enqueue(intended);

        while (queue.TryDequeue(out var current))
        {
            // Ordered so the search is deterministic whatever the neighbour order is —
            // the same tie-break Encounter.NearestFreeAnchor uses for displacement.
            foreach (var next in current.Neighbours()
                .Where(seen.Add)
                .OrderBy(square => square.X)
                .ThenBy(square => square.Y))
            {
                if (next.X < 0 || next.Y < 0 || next.X >= width || next.Y >= height)
                {
                    continue;
                }

                if (Fits(next))
                {
                    return next;
                }

                queue.Enqueue(next);
            }
        }

        return null;
    }
}
