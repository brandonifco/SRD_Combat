namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The merchant's stall had no floor at all (#704): <c>DrawShop</c> laid every offer into
/// one column with nothing to stop its running <c>y</c> from passing
/// <c>FightScreen.ScreenHeight</c>, so a stall with enough offers pushed its own Back
/// button — and the last few offers before it — below the bottom of the window. 25
/// offers already did it at 1920×1080 (the issue's own reproduction).
/// </summary>
/// <remarks>
/// <para>
/// <b>These pin the invariant, not the pixel arithmetic</b> — the same discipline
/// <c>HitPointBarPlacementTests</c> uses for <c>FightScreen.BarTop</c>/<c>GroundLine</c>.
/// <see cref="BackButtonBottom"/> below adds up the same constants <c>DrawShop</c> itself
/// draws with (<see cref="ShopLayout.HeaderHeight"/>, <see cref="ShopLayout.Fit"/>'s own
/// window, <see cref="ShopLayout.NoticeReserve"/>, <see cref="ShopLayout.BackButtonGap"/>
/// and <see cref="ShopLayout.BackButtonHeight"/>) rather than a second copy of the
/// numbers, so retuning any one of them moves both sides together.
/// </para>
/// <para>
/// <b>Knockout-verified (#704):</b> restoring <see cref="ShopLayout.Fit"/> to the old,
/// unbounded behaviour — returning every offer from <c>firstIndex</c> to the end
/// regardless of <c>roomBelowHeader</c> — fails <see cref="BackButtonNeverLeavesTheViewport"/>
/// and <see cref="BackButtonStaysOnScreenAtEveryScrollPosition"/> at both resolutions
/// (6 of 16 tests in this file, RED), the same shape as the issue's own reproduction: 25
/// offers overflowing a 1080-tall window.
/// </para>
/// </remarks>
public class ShopLayoutTests
{
    private const float UiTop = 96f;

    /// <summary>1920×1080 and 1280×720 — the two resolutions the acceptance criteria name.</summary>
    public static TheoryData<float> ScreenHeights => new(1080f, 720f);

    /// <summary>Every offer count the acceptance criteria name, 0 through 40 inclusive.</summary>
    public static TheoryData<int> OfferCounts
    {
        get
        {
            var data = new TheoryData<int>();

            for (var count = 0; count <= 40; count++)
            {
                data.Add(count);
            }

            return data;
        }
    }

    /// <summary>
    /// A representative offer's effect-line count. Three lines is typical of the printed
    /// offers (a weapon's before/after damage plus a mastery change, or armor's AC and
    /// Speed lines) — not the tallest possible, but not the shortest either, which is the
    /// point: the invariant must hold for the offers the stall actually prints, not only
    /// for a favourably short synthetic one.
    /// </summary>
    private const int TypicalEffectLines = 3;

    /// <summary>
    /// Adds up the Back button's bottom edge exactly the way <see cref="PlayMode.DrawShop"/>
    /// does: the header, the reserved "more above" line, the offer window
    /// <see cref="ShopLayout.Fit"/> returns (or the empty-stall message), the optional
    /// notice, the gap, then the button's own height.
    /// </summary>
    private static float BackButtonBottom(
        float screenHeight, int offerCount, int firstIndex = 0, bool hasNotice = false)
    {
        var effectLineCounts = Enumerable.Repeat(TypicalEffectLines, offerCount).ToList();
        var y = UiTop + ShopLayout.HeaderHeight + ShopLayout.MoreLineReserve;

        if (offerCount == 0)
        {
            y += ShopLayout.EmptyMessageHeight;
        }
        else
        {
            var window = ShopLayout.Fit(screenHeight - y, effectLineCounts, firstIndex);
            y += window.ContentHeight + ShopLayout.MoreLineReserve;
        }

        if (hasNotice)
        {
            y += ShopLayout.NoticeReserve;
        }

        return y + ShopLayout.BackButtonGap + ShopLayout.BackButtonHeight;
    }

    /// <summary>
    /// The acceptance criterion, verbatim: Back stays on screen for every offer count 0
    /// through 40, at both named resolutions, whether or not a notice happens to be
    /// showing.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void BackButtonNeverLeavesTheViewport(float screenHeight)
    {
        for (var offerCount = 0; offerCount <= 40; offerCount++)
        {
            foreach (var hasNotice in new[] { false, true })
            {
                var bottom = BackButtonBottom(screenHeight, offerCount, hasNotice: hasNotice);

                Assert.True(
                    bottom <= screenHeight,
                    $"Back button bottom {bottom} is past the window's own bottom edge {screenHeight} "
                        + $"at {offerCount} offers (notice: {hasNotice})");
            }
        }
    }

    /// <summary>
    /// The other half of "on screen": the purse line, drawn at a fixed <c>UiTop + 8</c>
    /// regardless of how many offers follow it, is nowhere near either window's bottom
    /// edge. Named separately from the Back button because it is a different rect, even
    /// though nothing in this change could plausibly move it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void ThePurseLineIsAlwaysOnScreen(float screenHeight)
    {
        Assert.True(UiTop + 8f < screenHeight);
    }

    /// <summary>
    /// Scrolled all the way to the last offer, the window still leaves room for Back —
    /// the same guarantee at every reachable scroll position, not only the top of the
    /// list.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void BackButtonStaysOnScreenAtEveryScrollPosition(float screenHeight)
    {
        for (var offerCount = 1; offerCount <= 40; offerCount++)
        {
            for (var firstIndex = 0; firstIndex < offerCount; firstIndex++)
            {
                var bottom = BackButtonBottom(screenHeight, offerCount, firstIndex);

                Assert.True(
                    bottom <= screenHeight,
                    $"Back button bottom {bottom} is past {screenHeight} at {offerCount} offers, "
                        + $"scrolled to offer {firstIndex}");
            }
        }
    }

    // ---- ShopLayout.Fit on its own ----------------------------------------------------

    [Fact]
    public void FitShowsEveryOfferWhenTheyAllFit()
    {
        var window = ShopLayout.Fit(roomBelowHeader: 1000f, [2, 2, 2], firstIndex: 0);

        Assert.Equal(3, window.VisibleCount);
        Assert.False(window.HasMore);
    }

    [Fact]
    public void FitNeverShowsAnOfferThatDoesNotFit()
    {
        // One row's own height (19 + 2*15 = 49, plus the 4px gap = 53) comfortably fits
        // in 60px, but a second copy of it does not (106 > 60) — so exactly one shows.
        var window = ShopLayout.Fit(roomBelowHeader: 60f + ShopLayout.FooterReserve, [2, 2], firstIndex: 0);

        Assert.Equal(1, window.VisibleCount);
        Assert.True(window.HasMore);
    }

    /// <summary>
    /// The deliberate reading behind #704's fix: an offer whose own height already
    /// exceeds the room available is left for "N more…" to name rather than drawn
    /// regardless, unlike a softer "always show at least one" rule. This is what keeps
    /// <see cref="BackButtonNeverLeavesTheViewport"/> true without qualification — a
    /// looser rule would still let one outsized offer push Back off the bottom.
    /// </summary>
    [Fact]
    public void FitShowsNothingRatherThanOverflowWhenNoRoomIsLeft()
    {
        var window = ShopLayout.Fit(roomBelowHeader: 0f, [1], firstIndex: 0);

        Assert.Equal(0, window.VisibleCount);
        Assert.True(window.HasMore);
        Assert.Equal(0f, window.ContentHeight);
    }

    [Fact]
    public void FitStartsFromTheGivenOffset()
    {
        var window = ShopLayout.Fit(roomBelowHeader: 1000f, [1, 1, 1, 1], firstIndex: 2);

        Assert.Equal(2, window.FirstIndex);
        Assert.Equal(2, window.VisibleCount);
        Assert.False(window.HasMore);
    }

    // ---- ShopLayout.ClampOffset --------------------------------------------------------

    [Theory]
    [InlineData(-5, 10, 0)]
    [InlineData(0, 10, 0)]
    [InlineData(9, 10, 9)]
    [InlineData(50, 10, 9)]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 0, 0)]
    public void ClampOffsetStaysInsideTheOfferList(int requested, int offerCount, int expected)
    {
        Assert.Equal(expected, ShopLayout.ClampOffset(requested, offerCount));
    }
}
