namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The initiative panel and the combat log used to share <c>DrawLog</c>'s own
/// arithmetic — <c>top = UiTop + 16 + (tokenCount * 19) + 26</c> — so every combatant
/// the panel added pushed the log's start down and its own room down with it, worst
/// exactly when a fight had the most combatants to show (#305's own title). These pin
/// <see cref="InitiativePanelLayout.Fit"/>, the seam that replaces it: the log's room
/// now has a floor (<see cref="InitiativePanelLayout.MinLogLines"/>) the panel cannot
/// erode further, because past <see cref="InitiativePanelLayout.PanelCapacity"/> the
/// panel pages instead of growing.
/// </summary>
/// <remarks>
/// <b>These pin the invariant, not the pixel arithmetic</b> — the same discipline
/// <c>HitPointBarPlacementTests</c> and <c>ShopLayoutTests</c> already use. Retuning any
/// of <see cref="InitiativePanelLayout"/>'s own constants moves both sides of every
/// assertion here together, since each reads them rather than a copied literal.
/// </remarks>
public class InitiativePanelLayoutTests
{
    /// <summary>
    /// <c>FightScreen.UiTop</c> (96) plus the 16px gap to the panel's first row —
    /// <c>FightScreen.DrawTurnOrder</c>'s own <c>UiTop + 16</c>, copied here the same
    /// way <c>ShopLayoutTests</c> copies <c>UiTop</c> itself: both are <c>protected</c>
    /// constants on a Godot node this test project cannot construct.
    /// </summary>
    private const float HeaderBottom = 96f + 16f;

    /// <summary>1920×1080 and 1280×720 — the two resolutions the acceptance criteria name.</summary>
    public static TheoryData<float> ScreenHeights => new(1080f, 720f);

    /// <summary>Every initiative count the acceptance criteria name, 1 through 14 inclusive.</summary>
    public static TheoryData<int> CombatantCounts
    {
        get
        {
            var data = new TheoryData<int>();

            for (var count = 1; count <= 14; count++)
            {
                data.Add(count);
            }

            return data;
        }
    }

    private static float RoomBelowHeader(float screenHeight) => screenHeight - HeaderBottom;

    // ---- The acceptance criteria, verbatim -------------------------------------------

    /// <summary>
    /// "The log's usable space no longer shrinks as the initiative list grows" — the
    /// log never drops below <see cref="InitiativePanelLayout.MinLogLines"/>, at every
    /// named count and both named resolutions, active turn anywhere in the order.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (recorded for the PR table):</b> reverting <see
    /// cref="InitiativePanelLayout.PanelCapacity"/> to return <c>int.MaxValue</c> (the
    /// old, uncapped behaviour — every combatant always shown, the log always pushed
    /// down to make room) fails this at 1280×720 from 11 combatants on, each one further
    /// under the 20-line floor than the last.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void LogNeverDropsBelowTheStatedMinimum(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);

        foreach (var count in AllCounts())
        {
            for (var activeIndex = 0; activeIndex < count; activeIndex++)
            {
                var regions = InitiativePanelLayout.Fit(room, count, activeIndex);

                Assert.True(
                    regions.LogLines >= InitiativePanelLayout.MinLogLines,
                    $"log got {regions.LogLines} lines at {count} combatants (active {activeIndex}), "
                        + $"height {screenHeight} — below the floor of {InitiativePanelLayout.MinLogLines}");
            }
        }
    }

    /// <summary>
    /// The opposite polarity of the floor test above: below
    /// <see cref="InitiativePanelLayout.PanelCapacity"/>, nothing pages at all — every
    /// combatant shows, exactly as before this change. A seam that always paged (or
    /// always showed a "+N more" line) would pass the floor test above just as easily,
    /// so that test alone is not proof the panel ever shows everything it can.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void PanelShowsEveryCombatantWhenTheListFitsAboveTheFloor(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);
        var capacity = InitiativePanelLayout.PanelCapacity(room);

        // At least one of the pinned counts must actually be at or under capacity for
        // this test to mean anything at both resolutions — true today (720p's own
        // capacity is 10, comfortably inside 1..14) but asserted rather than assumed.
        Assert.True(capacity >= 1, $"capacity computed as {capacity} at height {screenHeight}");

        foreach (var count in AllCounts())
        {
            if (count > capacity)
            {
                continue;
            }

            var regions = InitiativePanelLayout.Fit(room, count, activeIndex: 0);

            Assert.Equal(0, regions.PanelHiddenCount);
            Assert.Equal(count, regions.PanelVisibleCount);
            Assert.Equal(0, regions.PanelFirstIndex);
        }
    }

    /// <summary>
    /// The other named acceptance criterion: the panel never overlaps the log, at every
    /// pinned count and both resolutions — the window's own bottom edge is never passed
    /// either, the same "never leaves the viewport" standard <c>ShopLayoutTests</c>
    /// holds the stall's Back button to.
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> changing <see cref="InitiativePanelLayout.Fit"/>'s
    /// <c>logLines</c> line from <c>Math.Max(0, (int)(logRoom / LogLineHeight))</c> to
    /// <c>(int)Math.Ceiling(logRoom / LogLineHeight)</c> (rounding the log's own line
    /// count up instead of down) fails this at both resolutions — the log's drawn
    /// content then runs past <c>roomBelowHeader</c> by up to one line's worth of
    /// pixels.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void LogNeverOverflowsTheWindowsOwnBottomEdge(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);

        foreach (var count in AllCounts())
        {
            var regions = InitiativePanelLayout.Fit(room, count, activeIndex: count - 1);
            var logBottom = regions.LogTop + (regions.LogLines * InitiativePanelLayout.LogLineHeight)
                + InitiativePanelLayout.LogFooterMargin;

            Assert.True(
                logBottom <= room,
                $"log bottom {logBottom} is past the room available ({room}) at {count} combatants, height {screenHeight}");
        }
    }

    /// <summary>
    /// The panel's own visible rows never run into the log's first line either — the
    /// same overlap guarantee, read from the panel's side.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void PanelRowsNeverRunPastTheLogsOwnTop(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);

        foreach (var count in AllCounts())
        {
            var regions = InitiativePanelLayout.Fit(room, count, activeIndex: 0);
            var panelBottom = regions.PanelVisibleCount * InitiativePanelLayout.RowHeight;

            Assert.True(
                panelBottom <= regions.LogTop,
                $"panel's own rows end at {panelBottom}, past the log's top of {regions.LogTop} "
                    + $"at {count} combatants, height {screenHeight}");
        }
    }

    /// <summary>
    /// The busiest named case (14 combatants): the active combatant's row is inside the
    /// visible window whichever seat it occupies in the initiative order, at both
    /// resolutions.
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> pinning <c>firstIndex</c> at 0 regardless of
    /// <paramref name="activeIndex"/> (the naive "always show the top of the list", the
    /// shape the original arithmetic had no windowing concept to get wrong, but the
    /// wrong choice once paging is added) fails this at 1280×720 for every
    /// <c>activeIndex</c> from 10 through 13 — capacity there is 10, so the active
    /// combatant falls off the end of a window that never moves.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void ActiveCombatantRowIsAlwaysVisibleAtTheBusiestCount(float screenHeight)
    {
        const int count = 14;
        var room = RoomBelowHeader(screenHeight);

        for (var activeIndex = 0; activeIndex < count; activeIndex++)
        {
            var regions = InitiativePanelLayout.Fit(room, count, activeIndex);

            Assert.InRange(activeIndex, regions.PanelFirstIndex, regions.PanelFirstIndex + regions.PanelVisibleCount - 1);
        }
    }

    // ---- InitiativePanelLayout.Fit on its own ------------------------------------------

    [Fact]
    public void FitShowsEveryCombatantWhenTheyAllFit()
    {
        var regions = InitiativePanelLayout.Fit(roomBelowHeader: 1000f, combatantCount: 4, activeIndex: 1);

        Assert.Equal(0, regions.PanelFirstIndex);
        Assert.Equal(4, regions.PanelVisibleCount);
        Assert.Equal(0, regions.PanelHiddenCount);
    }

    [Fact]
    public void FitPagesWhenMoreCombatantsThanCapacityFit()
    {
        // Room for exactly 2 rows: MinLogLines(20)*17 + margin(20) + gap(26) + more(16)
        // = 402, plus 2 rows of 19 = 440.
        var room = 402f + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 0);

        Assert.Equal(2, regions.PanelVisibleCount);
        Assert.Equal(3, regions.PanelHiddenCount);
    }

    [Fact]
    public void FitWindowStartsAtTheActiveIndexWhenThereIsRoomToSlide()
    {
        var room = 402f + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 1);

        Assert.Equal(1, regions.PanelFirstIndex);
        Assert.Equal(2, regions.PanelVisibleCount);
    }

    [Fact]
    public void FitWindowClampsToTheListsOwnEndNearTheTail()
    {
        var room = 402f + (2 * InitiativePanelLayout.RowHeight);

        // Active is the very last combatant (index 4 of 5); a window starting there
        // would run past the list, so it clamps back to [3, 5).
        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 4);

        Assert.Equal(3, regions.PanelFirstIndex);
        Assert.Equal(2, regions.PanelVisibleCount);
        Assert.InRange(4, regions.PanelFirstIndex, regions.PanelFirstIndex + regions.PanelVisibleCount - 1);
    }

    [Fact]
    public void FitToleratesAnOutOfRangeActiveIndex()
    {
        // Between rounds, or with no active combatant at all, an out-of-range index
        // (here, -1) behaves as index 0 rather than throwing.
        var regions = InitiativePanelLayout.Fit(roomBelowHeader: 1000f, combatantCount: 4, activeIndex: -1);

        Assert.Equal(0, regions.PanelFirstIndex);
        Assert.Equal(4, regions.PanelVisibleCount);
    }

    [Fact]
    public void FitHandlesAnEmptyCombatantList()
    {
        var regions = InitiativePanelLayout.Fit(roomBelowHeader: 1000f, combatantCount: 0, activeIndex: 0);

        Assert.Equal(0, regions.PanelVisibleCount);
        Assert.Equal(0, regions.PanelHiddenCount);
    }

    // ---- InitiativePanelLayout.PanelCapacity -------------------------------------------

    /// <summary>
    /// The exact capacity at both named resolutions, hand-computed from the seam's own
    /// constants — pinned so a retuned constant's effect on real screens is visible in
    /// the diff rather than only in a passing floor test.
    /// </summary>
    [Theory]
    [InlineData(1080f, 29)]
    [InlineData(720f, 10)]
    public void PanelCapacity_MatchesTheNamedResolutions(float screenHeight, int expectedCapacity)
    {
        Assert.Equal(expectedCapacity, InitiativePanelLayout.PanelCapacity(RoomBelowHeader(screenHeight)));
    }

    [Fact]
    public void PanelCapacity_NeverGoesNegative()
    {
        Assert.Equal(0, InitiativePanelLayout.PanelCapacity(roomBelowHeader: 0f));
        Assert.Equal(0, InitiativePanelLayout.PanelCapacity(roomBelowHeader: -500f));
    }

    private static IEnumerable<int> AllCounts()
    {
        for (var count = 1; count <= 14; count++)
        {
            yield return count;
        }
    }
}
