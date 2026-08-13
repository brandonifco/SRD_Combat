using SRDCombat.Core.Combat;

namespace SRDCombat.Core.Rules;

/// <summary>The three printed degrees of cover, and none.</summary>
public enum CoverDegree
{
    None,
    Half,
    ThreeQuarters,
    Total,
}

/// <summary>
/// Cover: what stands between an attacker and a target, and what it is worth.
/// </summary>
/// <remarks>
/// <para>
/// The printed rule (Combat chapter's Cover table, restated in the glossary): Half Cover
/// is a +2 bonus to AC and Dexterity saving throws, Three-Quarters +5, and a target with
/// Total Cover "can't be targeted directly". A target benefits "only when an attack or
/// other effect originates on the opposite side of the cover", and only the most
/// protective degree applies — the degrees never add.
/// </para>
/// <para>
/// The SRD describes cover for a table with miniatures; how a square grid decides what is
/// "behind" is this engine's stated interpretation, the same kind
/// <see cref="AreaTargeting"/> documents:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The ruler runs centre to centre.</b> Cover is judged along the single straight
/// segment between the centres of the two squares — the same line every other distance in
/// this engine already imagines. A square provides cover only when the segment passes
/// through its <em>interior</em>: a segment that only touches a corner slips by, so a
/// perfect diagonal line of sight threads between corner-touching obstacles. A seam is
/// not a wall.
/// </item>
/// <item>
/// <b>A wall crossed is Total Cover.</b> Walls are the battlefield's
/// <see cref="Battlefield.Blocked"/> squares, full-height by definition.
/// </item>
/// <item>
/// <b>A low obstacle crossed is Half Cover; two or more are Three-Quarters.</b> Low
/// obstacles (<see cref="Battlefield.LowObstacles"/>) are the crates and boulders a
/// creature ducks behind but cannot stand in. The printed table grades cover by how much
/// of the body is obscured, and more obstruction along the line obscures more — the
/// mapping from "at least three-quarters of the target" to "two low obstacles" is this
/// engine's reading, stated here.
/// </item>
/// <item>
/// <b>A living creature crossed is Half Cover, and never more.</b> The printed table's
/// Half Cover row names "another creature" as a source, and the caller passes the
/// combatants for it (#108). Three readings, stated: <em>the dead grant nothing</em> — a
/// fallen body lies flat and covers less than half of a standing target, the same line
/// <c>MovementRules</c> draws when it stops counting the dead as occupying;
/// <em>crowds are not walls</em> — however many creatures the line crosses, the degree
/// from creatures alone stays Half, because the table reserves Three-Quarters and Total
/// for objects; and <em>creatures never escalate obstacles</em> — a creature beside a
/// low obstacle is two sources of Half and Half is what applies, the table's own "only
/// the most protective degree" rule.
/// </item>
/// </list>
/// <para>
/// The attacker's and target's own squares never provide cover: the segment starts and
/// ends in their interiors, and "behind" needs something in between.
/// </para>
/// </remarks>
public static class CoverRules
{
    /// <summary>
    /// The printed bonus a degree of cover adds to AC and to Dexterity saving throws.
    /// Zero for Total Cover, which refuses the targeting instead of adjusting it.
    /// </summary>
    public static int Bonus(CoverDegree degree) => degree switch
    {
        CoverDegree.Half => 2,
        CoverDegree.ThreeQuarters => 5,
        _ => 0,
    };

    /// <summary>The short printed name, for narration: "Half Cover".</summary>
    public static string Describe(CoverDegree degree) => degree switch
    {
        CoverDegree.Half => "Half Cover",
        CoverDegree.ThreeQuarters => "Three-Quarters Cover",
        CoverDegree.Total => "Total Cover",
        _ => "no cover",
    };

    /// <summary>
    /// The degree of cover a target square has against an effect originating at another
    /// square.
    /// </summary>
    /// <param name="creatures">
    /// Everyone on the field, when the caller has a field to offer — a living creature
    /// the line crosses grants Half Cover. Null (a bare-geometry unit test) reads only
    /// the terrain. The origin's and target's own squares never count, whoever stands
    /// there: the segment starts and ends in them.
    /// </param>
    public static CoverDegree Between(
        Battlefield field,
        GridPosition origin,
        GridPosition target,
        IReadOnlyCollection<Combatant>? creatures = null)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (origin == target)
        {
            return CoverDegree.None;
        }

        var occupied = creatures is null
            ? null
            : creatures.Where(creature => !creature.IsDead).Select(creature => creature.Position).ToHashSet();

        var lowObstaclesCrossed = 0;
        var creatureCrossed = false;

        var minX = Math.Min(origin.X, target.X);
        var maxX = Math.Max(origin.X, target.X);
        var minY = Math.Min(origin.Y, target.Y);
        var maxY = Math.Max(origin.Y, target.Y);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                var square = new GridPosition(x, y);

                if (square == origin || square == target || !SegmentCrossesInterior(origin, target, square))
                {
                    continue;
                }

                if (field.Blocked.Contains(square))
                {
                    return CoverDegree.Total;
                }

                if (field.LowObstacles.Contains(square))
                {
                    lowObstaclesCrossed++;
                }

                if (occupied is not null && occupied.Contains(square))
                {
                    creatureCrossed = true;
                }
            }
        }

        var fromObstacles = lowObstaclesCrossed switch
        {
            0 => CoverDegree.None,
            1 => CoverDegree.Half,
            _ => CoverDegree.ThreeQuarters,
        };

        // "If a target is behind multiple sources of cover, only the most protective
        // degree of cover applies" — and from creatures alone the degree is Half,
        // however many the line crosses.
        var fromCreatures = creatureCrossed ? CoverDegree.Half : CoverDegree.None;

        return (CoverDegree)Math.Max((int)fromObstacles, (int)fromCreatures);
    }

    /// <summary>
    /// Whether the centre-to-centre line from an origin to a square is blocked outright.
    /// </summary>
    /// <remarks>
    /// The glossary's Areas of Effect entry: a location is excluded from an area when
    /// every straight line to it from the point of origin is blocked, and "to block a
    /// line, an obstruction must provide Total Cover". This engine tests the one
    /// centre-to-centre line it measures everything else with — a stricter reading than
    /// the printed all-lines rule, so an area here hugs corners less generously than a
    /// table might allow. Stated rather than derived.
    /// </remarks>
    public static bool LineBlocked(Battlefield field, GridPosition origin, GridPosition target) =>
        Between(field, origin, target) == CoverDegree.Total;

    /// <summary>
    /// Whether the open segment between two square centres passes through the interior of
    /// a third square.
    /// </summary>
    /// <remarks>
    /// Everything is doubled so it stays in integers: a square's centre lands on odd
    /// coordinates and its boundary on even ones, which means an axis-aligned segment can
    /// never lie along a boundary and no epsilon is needed anywhere. The test is
    /// Liang–Barsky clipping with the parameters kept as fractions: the segment crosses
    /// the interior exactly when its clipped parameter interval has positive length,
    /// because touching only a corner or an edge clips to a single point.
    /// </remarks>
    private static bool SegmentCrossesInterior(GridPosition from, GridPosition to, GridPosition square)
    {
        long ax = (2 * from.X) + 1;
        long ay = (2 * from.Y) + 1;
        long dx = (2 * to.X) + 1 - ax;
        long dy = (2 * to.Y) + 1 - ay;

        long left = 2 * square.X;
        long right = left + 2;
        long top = 2 * square.Y;
        long bottom = top + 2;

        // The parameter interval [lo, hi] starts as the whole segment, each bound a
        // fraction numerator/denominator with the denominator kept positive.
        (long N, long D) lo = (0, 1);
        (long N, long D) hi = (1, 1);

        // Each pass clips against one half-plane: p·t ≤ q keeps the inside.
        foreach (var (p, q) in (ReadOnlySpan<(long, long)>)
            [(-dx, ax - left), (dx, right - ax), (-dy, ay - top), (dy, bottom - ay)])
        {
            if (p == 0)
            {
                if (q < 0)
                {
                    return false;
                }

                continue;
            }

            // The crossing parameter q/p, with the denominator made positive.
            var bound = p > 0 ? (N: q, D: p) : (N: -q, D: -p);

            if (p < 0)
            {
                // Entering bound: raise lo.
                if (bound.N * lo.D > lo.N * bound.D)
                {
                    lo = bound;
                }
            }
            else
            {
                // Leaving bound: lower hi.
                if (bound.N * hi.D < hi.N * bound.D)
                {
                    hi = bound;
                }
            }
        }

        // Positive length, strictly: a corner touch clips to lo == hi and is not cover.
        return lo.N * hi.D < hi.N * lo.D;
    }
}
