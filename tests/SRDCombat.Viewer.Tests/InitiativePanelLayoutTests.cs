namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The initiative panel and the combat log used to share <c>DrawLog</c>'s own
/// arithmetic — <c>top = UiTop + 16 + (tokenCount * 19) + 26</c> — so every combatant
/// the panel added pushed the log's start down and its own room down with it, worst
/// exactly when a fight had the most combatants to show (#305's own title). These pin
/// <see cref="InitiativePanelLayout.Fit"/>, the seam that replaces it: the log's room
/// now has a floor (<see cref="InitiativePanelLayout.MinLogLines"/>) the panel cannot
/// erode further once it pages, past <see cref="InitiativePanelLayout.PanelCapacity"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>These pin the invariant, not the pixel arithmetic</b> — the same discipline
/// <c>HitPointBarPlacementTests</c> and <c>ShopLayoutTests</c> already use. Retuning any
/// of <see cref="InitiativePanelLayout"/>'s own constants moves both sides of every
/// assertion here together, since each reads them rather than a copied literal — <see
/// cref="ReservedForFloor"/> below is the one shared derivation every test that needs
/// the 402px reserved budget reads, rather than each hardcoding it (PR #746 review).
/// The one deliberate exception is <see cref="MoreLineNeverCollidesWithTheLogLabel"/>,
/// which mirrors <c>FightScreen.DrawTurnOrder</c>/<c>DrawLog</c>'s own literal drawing
/// arithmetic on purpose, the same way <c>ShopLayoutTests.BackButtonBottom</c> mirrors
/// <c>DrawShop</c>'s — a test of what is actually drawn has to add up the same numbers
/// the drawing call does, not a shortcut that happens to agree with it today.
/// </para>
/// <para>
/// <b>Where this floor binds and where it does not</b> (qc's review of PR #746):
/// 1920×1080 is the only resolution this client currently runs at, and <see
/// cref="InitiativePanelLayout.PanelCapacity"/> there is 29 — above every combatant
/// count this project fields (a party of four plus a warband of up to ten, 14 total),
/// so at that resolution <see cref="InitiativePanelLayout.Fit"/> never pages for the
/// counts pinned here and returns the same log room the fight already had before this
/// PR. <see cref="LogNeverDropsBelowTheStatedMinimum"/> is still meaningful there — it
/// proves the floor holds, including where it holds only because nothing yet needs it —
/// but it is not evidence the floor changes anything a player can reach today. At
/// 1280×720 capacity is 10, so these tests exercise real paging from 11 combatants on.
/// Whether the capacity/floor constants themselves should change is Brandon's decision,
/// not this PR's — they are pinned as measured, not retuned, pending that answer.
/// </para>
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

    private static float RoomBelowHeader(float screenHeight) => screenHeight - HeaderBottom;

    /// <summary>
    /// The room <see cref="InitiativePanelLayout.PanelCapacity"/> reserves below the
    /// panel's rows before any of them fit — <see cref="InitiativePanelLayout.MoreLineReserve"/>
    /// plus <see cref="InitiativePanelLayout.GapAfterPanel"/> plus <see
    /// cref="InitiativePanelLayout.LogFooterMargin"/> plus <see
    /// cref="InitiativePanelLayout.MinLogLines"/> lines at <see
    /// cref="InitiativePanelLayout.LogLineHeight"/> each — read from the constants
    /// themselves (PR #746 review) rather than the <c>402f</c> three tests used to
    /// hardcode, which would have silently gone stale the moment any one of those
    /// constants moved.
    /// </summary>
    private static float ReservedForFloor =>
        InitiativePanelLayout.MoreLineReserve
        + InitiativePanelLayout.GapAfterPanel
        + InitiativePanelLayout.LogFooterMargin
        + (InitiativePanelLayout.MinLogLines * InitiativePanelLayout.LogLineHeight);

    private static IEnumerable<int> AllCounts()
    {
        for (var count = 1; count <= 14; count++)
        {
            yield return count;
        }
    }

    // ---- The acceptance criteria, verbatim -------------------------------------------

    /// <summary>
    /// "The log's usable space no longer shrinks as the initiative list grows" — the
    /// log never drops below <see cref="InitiativePanelLayout.MinLogLines"/>, at every
    /// named count and both named resolutions, active turn anywhere in the order.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh for PR #746's review, not the earlier, wrong count):</b>
    /// reverting <see cref="InitiativePanelLayout.PanelCapacity"/> to return
    /// <c>int.MaxValue</c> (the old, uncapped behaviour — every combatant always shown,
    /// the log always pushed down to make room) first fails at 1280×720 with **12**
    /// combatants, computing **19 lines** — one under the 20-line floor. 11 combatants
    /// computes exactly 20 and still passes; every count from 12 on fails further.
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
    /// combatant shows, exactly as before this change, and the log's own top carries no
    /// reserve for a "more below" line it never draws. A seam that always paged (or
    /// always reserved the more-line's own room) would pass the floor test above just
    /// as easily, so that test alone is not proof the panel ever shows everything it
    /// can, or that an unpaged fight costs the log nothing new.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (item 3, PR #746 review):</b> making <c>moreLineSpace</c>
    /// unconditional (always <c>MoreLineReserve</c>, not only when <c>hiddenBelow &gt;
    /// 0</c>) fails the <c>LogTop</c> assertion below at every pinned count and both
    /// heights — e.g. 1 combatant at 1080p computes <c>LogTop = 45</c> where the
    /// unconditional stub computes <c>61</c>.
    /// </remarks>
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

            Assert.Equal(0, regions.HiddenAboveCount);
            Assert.Equal(0, regions.HiddenBelowCount);
            Assert.Equal(count, regions.PanelVisibleCount);
            Assert.Equal(0, regions.PanelFirstIndex);

            // Nothing pages, so nothing is reserved for a "more below" line either —
            // LogTop is exactly the rows plus the fixed gap, with no MoreLineReserve
            // added (item 3, PR #746 review).
            Assert.Equal(
                (count * InitiativePanelLayout.RowHeight) + InitiativePanelLayout.GapAfterPanel,
                regions.LogTop);
        }
    }

    /// <summary>
    /// The other named acceptance criterion: the log's own drawn content never runs
    /// past the window's bottom edge, at every pinned count and both resolutions — the
    /// same "never leaves the viewport" standard <c>ShopLayoutTests</c> holds the
    /// stall's Back button to.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh for PR #746's review, not the earlier, wrong
    /// numbers):</b> changing <see cref="InitiativePanelLayout.Fit"/>'s <c>logLines</c>
    /// line from <c>Math.Max(0, (int)(logRoom / LogLineHeight))</c> to <c>(int)
    /// Math.Ceiling(logRoom / LogLineHeight)</c> (rounding the log's own line count up
    /// instead of down) fails this at both resolutions: **1 combatant at 1920×1080**
    /// overflows the window's bottom edge by **15px** (log bottom 983 against a room of
    /// 968); the first 1280×720 failure is also **1 combatant**, overflowing by **1px**
    /// (log bottom 609 against a room of 608).
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
    /// What the panel actually draws below its rows never collides with what
    /// <c>DrawLog</c> draws above the log's own content — read from the same literal
    /// offsets <c>FightScreen.DrawTurnOrder</c>/<c>DrawLog</c> draw with (<see
    /// cref="InitiativePanelLayout.MoreLineBaselineOffset"/>, <see
    /// cref="InitiativePanelLayout.MoreLineTextHeight"/>, <see
    /// cref="InitiativePanelLayout.LogLabelBaselineOffset"/>), the way
    /// <c>ShopLayoutTests.BackButtonBottom</c> reads <c>DrawShop</c>'s own arithmetic
    /// rather than re-deriving a shortcut for it.
    /// </summary>
    /// <remarks>
    /// <b>Replaces the earlier <c>PanelRowsNeverRunPastTheLogsOwnTop</c></b> (PR #746
    /// review, item 2): that test asserted <c>panelBottom &lt;= regions.LogTop</c>,
    /// which is algebraically true for every input the moment <c>LogTop</c> is defined
    /// as <c>panelBottom + moreLineSpace + GapAfterPanel</c> with both addends
    /// non-negative — it could not go red short of a negative <c>GapAfterPanel</c>, so
    /// it proved nothing about the two lines actually drawn in that gap.
    /// </remarks>
    /// <remarks>
    /// <b>Knockout (measured):</b> retuning <see
    /// cref="InitiativePanelLayout.MoreLineReserve"/> from 16 to 4 fails this — the
    /// first red is 12 combatants at 1280×720, where the "more below" line's own
    /// bottom (232) runs past the "COMBAT LOG" label's baseline (227), a 5px collision.
    /// (That retuning also moves <see cref="InitiativePanelLayout.PanelCapacity"/>
    /// itself, since <c>MoreLineReserve</c> is part of its reserved budget too, so
    /// <c>PanelCapacity_MatchesTheNamedResolutions</c> goes red alongside this one —
    /// expected collateral of retuning a constant two things read, not a second bug.)
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void MoreLineNeverCollidesWithTheLogLabel(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);

        foreach (var count in AllCounts())
        {
            var regions = InitiativePanelLayout.Fit(room, count, activeIndex: 0);

            if (regions.HiddenBelowCount == 0)
            {
                // Nothing draws a "more below" line when nothing is hidden below —
                // there is nothing here to collide with the log label.
                continue;
            }

            var panelBottom = regions.PanelVisibleCount * InitiativePanelLayout.RowHeight;
            var moreLineBaseline = panelBottom + InitiativePanelLayout.MoreLineBaselineOffset;
            var moreLineBottom = moreLineBaseline + InitiativePanelLayout.MoreLineTextHeight;
            var logLabelBaseline = regions.LogTop - InitiativePanelLayout.LogLabelBaselineOffset;

            Assert.True(
                moreLineBottom <= logLabelBaseline,
                $"the \"N more below\" line's own drawn rect (bottom {moreLineBottom}) runs into the "
                    + $"COMBAT LOG label's baseline ({logLabelBaseline}) at {count} combatants, height {screenHeight}");
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

    /// <summary>
    /// However small the window, the active combatant's own row still shows — the log's
    /// floor is what yields at a room this degenerate, not the active row's visibility
    /// (PR #746 review, item 4: <see cref="InitiativePanelLayout.PanelCapacity"/> can
    /// legitimately compute 0 at a small enough <c>roomBelowHeader</c>).
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> removing <see cref="InitiativePanelLayout.Fit"/>'s
    /// <c>Math.Max(1, capacity)</c> (using the raw, possibly-zero <c>capacity</c> as
    /// <c>visibleCount</c> instead) fails this at every <c>activeIndex</c>: with 0
    /// visible rows, <c>PanelVisibleCount &gt;= 1</c> is false, and the window
    /// <c>[firstIndex, firstIndex)</c> is empty, so <c>Assert.InRange</c> throws rather
    /// than passing for any index.
    /// </remarks>
    [Theory]
    [InlineData(0f)]
    [InlineData(200f)]
    public void ActiveRowAlwaysShowsEvenWhenTheWindowIsTooSmallForTheLogsOwnFloor(float tinyRoom)
    {
        // Sanity: this room really is the degenerate case under test — PanelCapacity
        // itself computes 0, so without the Math.Max(1, …) guard nothing would show.
        Assert.Equal(0, InitiativePanelLayout.PanelCapacity(tinyRoom));

        const int count = 6;

        for (var activeIndex = 0; activeIndex < count; activeIndex++)
        {
            var regions = InitiativePanelLayout.Fit(tinyRoom, count, activeIndex);

            Assert.True(regions.PanelVisibleCount >= 1, $"panel showed {regions.PanelVisibleCount} rows at a {tinyRoom}px room");
            Assert.InRange(activeIndex, regions.PanelFirstIndex, regions.PanelFirstIndex + regions.PanelVisibleCount - 1);
        }
    }

    // ---- Hidden combatants are reported on the side they are actually hidden ----------
    // (PR #746 review, item 5)

    [Fact]
    public void HiddenAboveCountNamesCombatantsEarlierInTurnOrder()
    {
        // capacity 2 (see FitPagesWhenMoreCombatantsThanCapacityFit); active at the
        // list's own tail slides the window to [3, 5), hiding 0-2 above and nothing
        // below.
        var room = ReservedForFloor + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 4);

        Assert.Equal(3, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
    }

    [Fact]
    public void HiddenBelowCountNamesCombatantsLaterInTurnOrder()
    {
        var room = ReservedForFloor + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 0);

        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(3, regions.HiddenBelowCount);
    }

    /// <summary>
    /// A window that starts mid-list hides combatants on both sides at once — a single
    /// combined "+N more" count could never say this correctly no matter which side it
    /// was drawn on.
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> reverting to a single combined count (e.g. reporting the total
    /// as <c>HiddenBelowCount</c> and always 0 for <c>HiddenAboveCount</c>) fails this —
    /// the total here is 3 (1 above, 2 below), which a combined-below reading would
    /// report as 0 above, 3 below instead of 1 above, 2 below.
    /// </remarks>
    [Fact]
    public void BothDirectionsCanBeHiddenAtOnce()
    {
        var room = ReservedForFloor + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 1);

        Assert.Equal(1, regions.HiddenAboveCount);
        Assert.Equal(2, regions.HiddenBelowCount);
    }

    /// <summary>
    /// Combatants hidden above cost the log nothing — only <see
    /// cref="InitiativePanelLayout.Regions.HiddenBelowCount"/> reserves the "more
    /// below" line's own room, because the "above" count is named in the existing
    /// "INITIATIVE" header instead (<c>FightScreen.DrawTurnOrder</c>), which needs no
    /// new space.
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> keying <c>moreLineSpace</c> off <c>(hiddenAbove + hiddenBelow) &gt;
    /// 0</c> instead of <c>hiddenBelow &gt; 0</c> alone fails this — it would add
    /// <see cref="InitiativePanelLayout.MoreLineReserve"/> to <c>LogTop</c> even though
    /// nothing hidden below exists to draw a line for.
    /// </remarks>
    [Fact]
    public void HiddenAboveAloneReservesNoExtraLogSpace()
    {
        var room = ReservedForFloor + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 4);

        Assert.Equal(3, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
        Assert.Equal(
            (2 * InitiativePanelLayout.RowHeight) + InitiativePanelLayout.GapAfterPanel,
            regions.LogTop);
    }

    // ---- InitiativePanelLayout.Fit on its own ------------------------------------------

    [Fact]
    public void FitShowsEveryCombatantWhenTheyAllFit()
    {
        var regions = InitiativePanelLayout.Fit(roomBelowHeader: 1000f, combatantCount: 4, activeIndex: 1);

        Assert.Equal(0, regions.PanelFirstIndex);
        Assert.Equal(4, regions.PanelVisibleCount);
        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
    }

    [Fact]
    public void FitPagesWhenMoreCombatantsThanCapacityFit()
    {
        // Room for exactly 2 rows: ReservedForFloor (402) + 2 rows of 19 = 440.
        var room = ReservedForFloor + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 0);

        Assert.Equal(2, regions.PanelVisibleCount);
        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(3, regions.HiddenBelowCount);
    }

    [Fact]
    public void FitWindowStartsAtTheActiveIndexWhenThereIsRoomToSlide()
    {
        var room = ReservedForFloor + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 1);

        Assert.Equal(1, regions.PanelFirstIndex);
        Assert.Equal(2, regions.PanelVisibleCount);
        Assert.Equal(1, regions.HiddenAboveCount);
        Assert.Equal(2, regions.HiddenBelowCount);
    }

    [Fact]
    public void FitWindowClampsToTheListsOwnEndNearTheTail()
    {
        var room = ReservedForFloor + (2 * InitiativePanelLayout.RowHeight);

        // Active is the very last combatant (index 4 of 5); a window starting there
        // would run past the list, so it clamps back to [3, 5).
        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 4);

        Assert.Equal(3, regions.PanelFirstIndex);
        Assert.Equal(2, regions.PanelVisibleCount);
        Assert.InRange(4, regions.PanelFirstIndex, regions.PanelFirstIndex + regions.PanelVisibleCount - 1);
    }

    /// <summary>
    /// Between rounds, or with no active combatant at all, an out-of-range index
    /// behaves as index 0 rather than throwing — exercised inside an actually-paged
    /// window (unlike a check against an unpaged one, which never reads
    /// <c>activeIndex</c> at all and so cannot tell clamping from indifference).
    /// </summary>
    [Fact]
    public void FitTreatsAnOutOfRangeActiveIndexAsZero()
    {
        var room = ReservedForFloor + (2 * InitiativePanelLayout.RowHeight);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: -1);

        Assert.Equal(0, regions.PanelFirstIndex);
        Assert.Equal(2, regions.PanelVisibleCount);
    }

    [Fact]
    public void FitHandlesAnEmptyCombatantList()
    {
        var regions = InitiativePanelLayout.Fit(roomBelowHeader: 1000f, combatantCount: 0, activeIndex: 0);

        Assert.Equal(0, regions.PanelVisibleCount);
        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
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
}
