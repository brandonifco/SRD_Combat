namespace SRDCombat.Viewer.Tests;

/// <summary>
/// Where the hit point bar sits relative to the figure standing on the same square.
/// </summary>
/// <remarks>
/// <para>
/// #533: the bar's row and the sprite's ground line were independent literals six pixels
/// apart, so the bar was drawn <i>through</i> the bottom six screen-pixels of every token
/// on the board for as long as both have existed. Nothing failed — a figure with a bar
/// across its shins still renders — and on anything tall the overlap is a trailing foot.
/// It was found in play, on a Giant Centipede, where six pixels is a third of the animal.
/// </para>
/// <para>
/// <b>These assert the invariant, not the arithmetic.</b> The bar is allowed to move; what
/// it may not do is climb back over the feet or drop out of its own square into the row
/// below. So the bounds are computed from <see cref="FightScreen.GroundLine"/> and
/// <see cref="FightScreen.BarTop"/> themselves — retune either and these still pass, break
/// the relation between them and they do not. Knockout-verified (#416): restoring the old
/// <c>centreY + cellPixels / 2 - 8</c> body to <c>BarTop</c> fails
/// <see cref="BarTop_NeverClimbsAboveTheGroundLine"/> at every cell size below.
/// </para>
/// </remarks>
public class HitPointBarPlacementTests
{
    /// <summary>
    /// Every cell size the camera actually produces. <c>CellPixels</c> is continuous — it
    /// is a zoom, not a step — so these are sampled across the range rather than a single
    /// nominal 48, and the fractional entries are there because a fitted cell is rarely a
    /// whole number.
    /// </summary>
    public static TheoryData<float> CellSizes => new(12f, 24f, 33.5f, 48f, 66f, 91.25f, 160f);

    [Theory]
    [MemberData(nameof(CellSizes))]
    public void BarTop_NeverClimbsAboveTheGroundLine(float cellPixels)
    {
        foreach (var centreY in new[] { -400f, 0f, 17.5f, 540f, 4000f })
        {
            Assert.True(
                FightScreen.BarTop(centreY, cellPixels) >= FightScreen.GroundLine(centreY, cellPixels),
                $"bar top {FightScreen.BarTop(centreY, cellPixels)} is above the ground line "
                    + $"{FightScreen.GroundLine(centreY, cellPixels)} at cell {cellPixels}, centre {centreY} "
                    + "— it would be drawn through the bottom of the figure standing there");
        }
    }

    /// <summary>
    /// The other bound, and the reason the fix was not simply "push it down until it looks
    /// right": the bar belongs to its own square. A bar whose last row fell past the square's
    /// bottom edge would be drawn over the token in the row below, which depth-sorts in front
    /// of it — the overlap traded for a different one.
    /// </summary>
    [Theory]
    [MemberData(nameof(CellSizes))]
    public void BarBottom_StaysInsideItsOwnSquare(float cellPixels)
    {
        foreach (var centreY in new[] { -400f, 0f, 17.5f, 540f, 4000f })
        {
            var squareBottom = centreY + (cellPixels / 2f);
            var barBottom = FightScreen.BarTop(centreY, cellPixels) + FightScreen.BarHeight;

            Assert.True(
                barBottom <= squareBottom,
                $"bar bottom {barBottom} falls past its square's bottom edge {squareBottom} "
                    + $"at cell {cellPixels}, centre {centreY} — it would be drawn into the row below");
        }
    }

    /// <summary>
    /// The ground line is inside the square, one bar-height above its bottom edge — the
    /// gap the bar exactly fills. This is the
    /// premise the two bounds above rest on; without it they could both hold while the
    /// figure itself stood somewhere else entirely.
    /// </summary>
    [Theory]
    [MemberData(nameof(CellSizes))]
    public void GroundLine_SitsJustInsideTheSquaresBottomEdge(float cellPixels)
    {
        var ground = FightScreen.GroundLine(centreY: 0f, cellPixels);

        Assert.InRange(ground, -cellPixels / 2f, cellPixels / 2f);
        Assert.Equal(cellPixels / 2f, ground + FightScreen.BarHeight, precision: 4);
    }
}
