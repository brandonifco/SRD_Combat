namespace SRDCombat.Core.Combat;

/// <summary>
/// The squares a creature stands in: a square block of grid squares, anchored at its
/// north-west corner.
/// </summary>
/// <remarks>
/// <para>
/// <b>The printed rule.</b> The SRD's Creature Size and Space table (printed page 14)
/// gives every size a square space: Small and Medium one square, Large four (2 by 2),
/// Huge nine (3 by 3), Gargantuan sixteen (4 by 4). "A creature's space is the area that
/// it effectively controls in combat and the area it needs to fight effectively." Every
/// printed space is square, which is why one <see cref="SpanSquares"/> describes all of
/// them and why nothing here needs a width and a height.
/// </para>
/// <para>
/// <b>The anchor is the north-west square, and it is the creature's
/// <c>Position</c>.</b> A multi-square creature has one coordinate everything else
/// addresses it by — a destination handed to <c>Encounter.Move</c>, a spawn square, a
/// step along a path — and picking the lowest-x, lowest-y square of the block means the
/// block is <c>[Anchor.X, Anchor.X + span - 1] × [Anchor.Y, Anchor.Y + span - 1]</c>
/// with no rounding anywhere. A centre anchor would need one for every even span.
/// </para>
/// <para>
/// <b>Distance is nearest-square, and that one is printed.</b> Playing on a Grid
/// (printed page 13) counts range "from a square adjacent to one of them" and stops "in
/// the space of the other one", by the shortest route — so the gap between two spaces is
/// measured between their nearest squares, never between their anchors. A Large creature
/// that occupied four squares but measured reach from one of them would contradict that
/// sentence directly, and board control is the whole of what size buys.
/// <see cref="DistanceFeetTo(CreatureSpace)"/> is therefore the axis gap between two
/// intervals rather than the difference of two points, and for two one-square spaces it
/// is exactly <c>GridPosition.DistanceFeetTo</c> — the same
/// <c>max(|dx|, |dy|) × 5</c>, because a one-square interval's gap on an axis is the
/// absolute difference of the two coordinates.
/// </para>
/// <para>
/// Two overlapping spaces are 0 feet apart. That is the arithmetic falling out rather
/// than a rule: creatures are not meant to overlap, and the one case that can produce it
/// — the house rule that lets a move end on a fallen ally, see
/// <c>MovementRules.FindPath</c> — is a state the engine survives rather than throws on.
/// </para>
/// </remarks>
/// <param name="Anchor">The north-west square of the block, and the creature's position.</param>
/// <param name="SpanSquares">
/// How many squares the block is on a side. Clamped to at least one: a creature always
/// stands somewhere, and a zero-span space would make every distance meaningless rather
/// than loud.
/// </param>
public readonly record struct CreatureSpace(GridPosition Anchor, int SpanSquares)
{
    /// <summary>How many squares the block is on a side, never less than one.</summary>
    public int SpanSquares { get; init; } = Math.Max(1, SpanSquares);

    /// <summary>The east-most column of the block.</summary>
    public int MaximumX => Anchor.X + SpanSquares - 1;

    /// <summary>The south-most row of the block.</summary>
    public int MaximumY => Anchor.Y + SpanSquares - 1;

    /// <summary>A single square's space — what every creature has until size is modelled.</summary>
    public static CreatureSpace Of(GridPosition square) => new(square, 1);

    /// <summary>Every square the creature stands in, row by row.</summary>
    /// <remarks>
    /// Ordered north-west to south-east and deterministic, because callers that reserve
    /// or displace footprints iterate it and a seed has to replay.
    /// </remarks>
    public IEnumerable<GridPosition> Squares()
    {
        for (var y = Anchor.Y; y <= MaximumY; y++)
        {
            for (var x = Anchor.X; x <= MaximumX; x++)
            {
                yield return new GridPosition(x, y);
            }
        }
    }

    /// <summary>True when the creature stands in this square.</summary>
    public bool Contains(GridPosition square) =>
        square.X >= Anchor.X && square.X <= MaximumX
        && square.Y >= Anchor.Y && square.Y <= MaximumY;

    /// <summary>True when the two spaces share at least one square.</summary>
    public bool Overlaps(CreatureSpace other) =>
        Anchor.X <= other.MaximumX && other.Anchor.X <= MaximumX
        && Anchor.Y <= other.MaximumY && other.Anchor.Y <= MaximumY;

    /// <summary>
    /// The distance in feet between this space and another, counted between their
    /// nearest squares — printed page 13's "from a square adjacent to one of them".
    /// </summary>
    public int DistanceFeetTo(CreatureSpace other) =>
        Math.Max(
            AxisGap(Anchor.X, MaximumX, other.Anchor.X, other.MaximumX),
            AxisGap(Anchor.Y, MaximumY, other.Anchor.Y, other.MaximumY))
        * Battlefield.FeetPerSquare;

    /// <summary>
    /// The distance in feet between this space and a square — a chosen point, an aimed
    /// area's origin, a candidate destination.
    /// </summary>
    public int DistanceFeetTo(GridPosition square) => DistanceFeetTo(Of(square));

    /// <summary>The same block moved so that its anchor lands on another square.</summary>
    public CreatureSpace MovedTo(GridPosition anchor) => this with { Anchor = anchor };

    /// <summary>
    /// The gap between two closed integer intervals: nought when they meet or overlap,
    /// otherwise the number of squares between them.
    /// </summary>
    private static int AxisGap(int min, int max, int otherMin, int otherMax) =>
        Math.Max(0, Math.Max(min - otherMax, otherMin - max));

    public override string ToString() =>
        SpanSquares == 1 ? Anchor.ToString() : $"{Anchor}×{SpanSquares}";
}
