using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Combat;

/// <summary>
/// Works out which squares an area of effect covers.
/// </summary>
/// <remarks>
/// <para>
/// The SRD describes areas geometrically, for a table with a ruler. On a grid they have
/// to become a set of squares, and the SRD's own grid rules do not spell out how. What
/// follows is a deliberate, stated interpretation rather than a derivation:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Emanation and Sphere</b> — every square within the radius, measured the same way
/// every other distance in this engine is measured. An Emanation is centred on the
/// caster, a Sphere on a chosen point. <b>An Emanation excludes its origin square</b>,
/// and that one is not an interpretation: the glossary says "An Emanation's origin
/// (creature or object) isn't included in the area of effect unless its creator decides
/// otherwise" (printed page 181). The trailing clause is a choice the creator makes, and
/// nothing in this engine offers it, so the exclusion is unconditional here. A Sphere
/// keeps its centre square — it is centred on a point rather than extending from a
/// creature, and the glossary gives it no such exclusion.
/// </item>
/// <item>
/// <b>Cone</b> — squares within the cone's length of the origin whose direction from the
/// origin is within 45° of the direction cast. That is the common grid reading of a cone
/// and matches its printed width at the far end closely enough.
/// </item>
/// <item>
/// <b>Line</b> — squares within the line's length along the direction cast and within
/// half its width perpendicular to it. The origin square itself is excluded, as it is
/// for a Cone: both extend <em>from</em> their user, and a breath weapon that caught
/// the breather would be read as a bug at any table. With the Emanation exclusion
/// verified above, all three shapes that extend from a creature now agree — and only
/// the Emanation's exclusion is printed rather than inferred.
/// </item>
/// <item>
/// <b>Cube</b> — an axis-aligned square of the given side, centred on the chosen point.
/// </item>
/// </list>
/// <para>
/// Cylinder is not modelled; it needs a height this engine has no concept of, and a
/// spell using one is reported rather than silently treated as a Sphere.
/// </para>
/// </remarks>
public static class AreaTargeting
{
    /// <summary>True when the engine can resolve this area's shape at all.</summary>
    public static bool CanResolve(AreaShape shape) => shape != AreaShape.Cylinder;

    /// <summary>
    /// Every square the area covers.
    /// </summary>
    /// <param name="area">The area's shape and size.</param>
    /// <param name="origin">
    /// The caster's square. Used as the point of origin for a Cone, a Line and an
    /// Emanation.
    /// </param>
    /// <param name="target">
    /// The chosen point. Centres a Sphere or a Cube, and gives the direction of a Cone
    /// or a Line.
    /// </param>
    /// <param name="battlefield">Bounds the result to squares that exist.</param>
    public static IReadOnlyList<GridPosition> Cover(
        EffectArea area,
        GridPosition origin,
        GridPosition target,
        Battlefield battlefield)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(battlefield);

        if (!CanResolve(area.Shape))
        {
            return [];
        }

        return battlefield.AllSquares().Where(square => Covers(area, origin, target, square)).ToArray();
    }

    private static bool Covers(EffectArea area, GridPosition origin, GridPosition target, GridPosition square) =>
        area.Shape switch
        {
            // "An Emanation's origin (creature or object) isn't included in the area of
            // effect unless its creator decides otherwise" — glossary, printed page 181.
            AreaShape.Emanation => square != origin && square.DistanceFeetTo(origin) <= area.SizeFeet,
            AreaShape.Sphere => square.DistanceFeetTo(target) <= area.SizeFeet,
            AreaShape.Cube => square.DistanceFeetTo(target) <= area.SizeFeet / 2,
            AreaShape.Cone => InCone(area, origin, target, square),
            AreaShape.Line => InLine(area, origin, target, square),
            _ => false,
        };

    private static bool InCone(EffectArea area, GridPosition origin, GridPosition target, GridPosition square)
    {
        if (square == origin || square.DistanceFeetTo(origin) > area.SizeFeet)
        {
            return false;
        }

        var (dirX, dirY) = Direction(origin, target);
        var toSquare = ((double)(square.X - origin.X), (double)(square.Y - origin.Y));
        var length = Math.Sqrt((toSquare.Item1 * toSquare.Item1) + (toSquare.Item2 * toSquare.Item2));

        if (length == 0)
        {
            return false;
        }

        // Within 45 degrees of the direction cast: cos(45°) is about 0.7071.
        var cosine = ((toSquare.Item1 * dirX) + (toSquare.Item2 * dirY)) / length;

        return cosine >= 0.7071;
    }

    private static bool InLine(EffectArea area, GridPosition origin, GridPosition target, GridPosition square)
    {
        // The line extends from its user, who is not standing in it — the same exclusion
        // InCone makes explicitly.
        if (square == origin)
        {
            return false;
        }

        var (dirX, dirY) = Direction(origin, target);
        var toSquare = ((double)(square.X - origin.X), (double)(square.Y - origin.Y));

        // Distance along the line, and distance away from it.
        var along = (toSquare.Item1 * dirX) + (toSquare.Item2 * dirY);
        var across = Math.Abs((toSquare.Item1 * dirY) - (toSquare.Item2 * dirX));

        if (along < 0 || along * Battlefield.FeetPerSquare > area.SizeFeet)
        {
            return false;
        }

        var halfWidthSquares = (area.WidthFeet ?? Battlefield.FeetPerSquare) / 2.0 / Battlefield.FeetPerSquare;

        return across <= Math.Max(0.5, halfWidthSquares);
    }

    /// <summary>The unit vector from the origin toward the target.</summary>
    private static (double X, double Y) Direction(GridPosition origin, GridPosition target)
    {
        double x = target.X - origin.X;
        double y = target.Y - origin.Y;
        var length = Math.Sqrt((x * x) + (y * y));

        // A target on the caster's own square has no direction; point east so the area
        // still resolves to something rather than to nothing.
        return length == 0 ? (1, 0) : (x / length, y / length);
    }
}
