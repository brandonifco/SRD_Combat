namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The initiative panel and the combat log used to share <c>DrawLog</c>'s own
/// arithmetic — <c>top = UiTop + 16 + (tokenCount * 19) + 26</c> — so every combatant
/// the panel added pushed the log's start down and its own room down with it, worst
/// exactly when a fight had the most combatants to show (#305's own title). These pin
/// <see cref="InitiativePanelLayout.Fit"/>, the seam that replaces it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Round 2 (Brandon's decision, 2026-09-10): the panel pages at 1920×1080 too.</b>
/// The first round left <see cref="InitiativePanelLayout.PanelCapacity"/> at 29 for
/// 1080p — above every combatant count this project fields (a party of four plus a
/// warband of up to ten, 14 total) — so the floor never bound at the resolution the
/// client actually ships at. <see cref="ExactTableForBothResolutions"/> below is the
/// pin for the new, per-height floors (<see
/// cref="InitiativePanelLayout.MinLogLinesAt1080p"/> = 45, <see
/// cref="InitiativePanelLayout.MinLogLinesAt720p"/> = 20, unchanged from round 1):
/// every one of its 28 rows is a number these tests computed and checked, not an
/// estimate.
/// </para>
/// <para>
/// <b>Hiding a row must never cost the log a line</b> (qc's round-2 review: round 1's
/// 720p/11 case hid a row and gained nothing, because a separately reserved "N more
/// below" line cost its own 16px against the 19px a hidden row frees — a one-time cost
/// landing close enough to the freed row's own value that two independent flooring
/// operations could tip either way, including backwards). <see
/// cref="HidingARowNeverCostsTheLogALine"/> pins the fix: the "below" notice now folds
/// into the existing "COMBAT LOG" label (<c>FightScreen.DrawLog</c>), the same way the
/// "above" notice already folds into "INITIATIVE", so <c>LogTop</c> is always exactly
/// <c>visibleCount * RowHeight + GapAfterPanel</c> — paged or not — and hiding one row
/// always frees a clean 19px against a 17px log line, which <b>provably</b> raises the
/// floored line count by at least one every time (19 &gt;= 17, so <c>floor((x + 19) /
/// 17) &gt;= floor(x / 17) + 1</c> unconditionally). This replaces round 1's
/// <c>MoreLineNeverCollidesWithTheLogLabel</c>, which checked two separately-drawn
/// lines for a collision that folding the notice into one label now makes structurally
/// impossible — <see cref="LogLabelNeverOverlapsThePanelsOwnRows"/> keeps a lighter
/// geometry check on the one label that remains.
/// </para>
/// <para>
/// <b>These pin the invariant, not the pixel arithmetic</b> — the same discipline
/// <c>HitPointBarPlacementTests</c> and <c>ShopLayoutTests</c> already use. Retuning any
/// of <see cref="InitiativePanelLayout"/>'s own constants moves both sides of every
/// assertion here together, since each reads them rather than a copied literal.
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

    private static IEnumerable<int> AllCounts()
    {
        for (var count = 1; count <= 14; count++)
        {
            yield return count;
        }
    }

    /// <summary>
    /// A synthetic room that computes to exactly <paramref name="capacity"/> under the
    /// given <paramref name="floor"/> — read from <see cref="InitiativePanelLayout"/>'s
    /// own constants (PR #746 review, round 1, item 6) rather than a hardcoded literal,
    /// so a retuned constant moves this alongside every test that uses it. Used for the
    /// <c>Fit</c>-on-its-own tests below, which need precise control over the capacity
    /// under test rather than a named resolution's own value.
    /// </summary>
    private static float RoomForCapacity(int capacity, int floor) =>
        InitiativePanelLayout.GapAfterPanel
        + InitiativePanelLayout.LogFooterMargin
        + (floor * InitiativePanelLayout.LogLineHeight)
        + (capacity * InitiativePanelLayout.RowHeight);

    // ---- The exact table (PR #746 review, round 2, item 1) -----------------------------

    /// <summary>
    /// Every one of the 28 (count, height) pairs this PR pins, computed once here and
    /// quoted verbatim in the PR body — "quote only numbers the tests computed" applies
    /// to this table as much as to the knockout results below. Capacity is 8 at 1080p,
    /// 11 at 720p; both plateau at their own floor (45, 20) the moment paging starts and
    /// never drop below it again as the count keeps growing.
    /// </summary>
    [Theory]
    [InlineData(1080f, 1, 1, 0, 53)]
    [InlineData(1080f, 2, 2, 0, 52)]
    [InlineData(1080f, 3, 3, 0, 50)]
    [InlineData(1080f, 4, 4, 0, 49)]
    [InlineData(1080f, 5, 5, 0, 48)]
    [InlineData(1080f, 6, 6, 0, 47)]
    [InlineData(1080f, 7, 7, 0, 46)]
    [InlineData(1080f, 8, 8, 0, 45)]
    [InlineData(1080f, 9, 8, 1, 45)]
    [InlineData(1080f, 10, 8, 2, 45)]
    [InlineData(1080f, 11, 8, 3, 45)]
    [InlineData(1080f, 12, 8, 4, 45)]
    [InlineData(1080f, 13, 8, 5, 45)]
    [InlineData(1080f, 14, 8, 6, 45)]
    [InlineData(720f, 1, 1, 0, 31)]
    [InlineData(720f, 2, 2, 0, 30)]
    [InlineData(720f, 3, 3, 0, 29)]
    [InlineData(720f, 4, 4, 0, 28)]
    [InlineData(720f, 5, 5, 0, 27)]
    [InlineData(720f, 6, 6, 0, 26)]
    [InlineData(720f, 7, 7, 0, 25)]
    [InlineData(720f, 8, 8, 0, 24)]
    [InlineData(720f, 9, 9, 0, 23)]
    [InlineData(720f, 10, 10, 0, 21)]
    [InlineData(720f, 11, 11, 0, 20)]
    [InlineData(720f, 12, 11, 1, 20)]
    [InlineData(720f, 13, 11, 2, 20)]
    [InlineData(720f, 14, 11, 3, 20)]
    public void ExactTableForBothResolutions(
        float screenHeight, int count, int expectedShown, int expectedHidden, int expectedLines)
    {
        var regions = InitiativePanelLayout.Fit(RoomBelowHeader(screenHeight), count, activeIndex: 0);

        Assert.Equal(expectedShown, regions.PanelVisibleCount);
        Assert.Equal(expectedHidden, regions.HiddenBelowCount);
        Assert.Equal(expectedLines, regions.LogLines);
    }

    // ---- The acceptance criteria, verbatim -------------------------------------------

    /// <summary>
    /// "The log's usable space no longer shrinks as the initiative list grows" — the
    /// log never drops below its own per-height floor (<see
    /// cref="InitiativePanelLayout.LogLinesFloor"/>), at every named count and both
    /// named resolutions, active turn anywhere in the order.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh):</b> reverting <see
    /// cref="InitiativePanelLayout.PanelCapacity"/> to return <c>int.MaxValue</c> (the
    /// pre-#305 behaviour — every combatant always shown, the log always pushed down to
    /// make room) fails this at both resolutions: 1280×720 first fails at **12
    /// combatants** (`log got 19 lines... below the floor of 20`); 1920×1080 first
    /// fails at **9 combatants** (`log got 44 lines... below the floor of 45`).
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void LogNeverDropsBelowTheStatedMinimum(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);
        var floor = InitiativePanelLayout.LogLinesFloor(room);

        foreach (var count in AllCounts())
        {
            for (var activeIndex = 0; activeIndex < count; activeIndex++)
            {
                var regions = InitiativePanelLayout.Fit(room, count, activeIndex);

                Assert.True(
                    regions.LogLines >= floor,
                    $"log got {regions.LogLines} lines at {count} combatants (active {activeIndex}), "
                        + $"height {screenHeight} — below the floor of {floor}");
            }
        }
    }

    /// <summary>
    /// The other named acceptance criterion: the log's own drawn content never runs
    /// past the window's bottom edge, at every pinned count and both resolutions.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh, unchanged from round 1):</b> changing <see
    /// cref="InitiativePanelLayout.Fit"/>'s <c>logLines</c> line from
    /// <c>Math.Max(0, (int)(logRoom / LogLineHeight))</c> to <c>(int)
    /// Math.Ceiling(logRoom / LogLineHeight)</c> fails this at both resolutions: **1
    /// combatant at 1920×1080** overflows the window's bottom edge by **15px** (log
    /// bottom 983 against a room of 968); the first 1280×720 failure is also **1
    /// combatant**, overflowing by **1px** (log bottom 609 against a room of 608).
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
    /// The requirement qc's round-2 review named directly: paging must never hide a row
    /// without buying the log at least one more line. Compares the fight's own actual
    /// (hidden, logLines) against the counterfactual of showing exactly one more row
    /// (<see cref="InitiativePanelLayout.LogLinesForVisibleCount"/>) — not against a
    /// different combatant count that happens to share the same visible-row plateau,
    /// which this design allows and which is not itself a violation (a bigger warband
    /// beyond capacity keeps the same visible rows and the same log lines, correctly:
    /// nothing more is possible to gain, and nothing more is taken from the log either).
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh):</b> reintroducing round 1's separate "more below"
    /// reserve (adding 16px to <c>logTop</c> whenever <c>hiddenBelow &gt; 0</c>, on top
    /// of the fold-into-the-label fix this test exists to protect) fails this at **9
    /// combatants, 1920×1080** (actual 44 lines, counterfactual 44 — a tie, not a
    /// gain) and at **12 combatants, 1280×720** (actual 19, counterfactual 19).
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void HidingARowNeverCostsTheLogALine(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);

        foreach (var count in AllCounts())
        {
            var regions = InitiativePanelLayout.Fit(room, count, activeIndex: 0);

            if (regions.HiddenAboveCount + regions.HiddenBelowCount == 0)
            {
                continue;
            }

            var oneMoreRowShown = InitiativePanelLayout.LogLinesForVisibleCount(room, regions.PanelVisibleCount + 1);

            Assert.True(
                regions.LogLines > oneMoreRowShown,
                $"hiding a row at {count} combatants, height {screenHeight} bought {regions.LogLines} lines, "
                    + $"no better than the {oneMoreRowShown} lines showing one more row would have");
        }
    }

    /// <summary>
    /// <c>LogTop</c> is always exactly the visible rows plus the one fixed gap — paged
    /// or not — because neither hidden direction reserves a line of its own (round 2).
    /// This is what <see cref="HidingARowNeverCostsTheLogALine"/> rests on; pinned
    /// directly here too since it is the one fact a retuned "bring back a reserve"
    /// change would break first.
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> adding a conditional <c>+ 16f</c> to <c>logTop</c> whenever
    /// <c>hiddenBelow &gt; 0</c> (round 1's design) fails this at every paged count and
    /// both heights — e.g. 9 combatants at 1920×1080 computes <c>LogTop = 178</c> where
    /// the reintroduced reserve computes <c>194</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void LogTopIsAlwaysExactlyTheVisibleRowsPlusTheFixedGap(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);

        foreach (var count in AllCounts())
        {
            var regions = InitiativePanelLayout.Fit(room, count, activeIndex: 0);

            Assert.Equal(
                (regions.PanelVisibleCount * InitiativePanelLayout.RowHeight) + InitiativePanelLayout.GapAfterPanel,
                regions.LogTop);
        }
    }

    /// <summary>
    /// The "COMBAT LOG" label's own baseline (<c>DrawLog</c>'s <c>top -
    /// LogLabelBaselineOffset</c>) never sits above the last panel row's own bottom
    /// edge — the replacement for round 1's <c>MoreLineNeverCollidesWithTheLogLabel</c>,
    /// which checked two separately-drawn lines for a collision that folding the "below"
    /// notice into this one label (round 2) makes structurally impossible; what remains
    /// worth checking is that the one label left still has room to draw at all.
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> shrinking <see cref="InitiativePanelLayout.GapAfterPanel"/> from
    /// 26 to 10 (less than <see cref="InitiativePanelLayout.LogLabelBaselineOffset"/>'s
    /// own 12) fails this at every count and both heights — the label's baseline would
    /// sit 2px *above* the last row's own bottom edge instead of 14px below the rows'
    /// own top-of-gap.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void LogLabelNeverOverlapsThePanelsOwnRows(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);

        foreach (var count in AllCounts())
        {
            var regions = InitiativePanelLayout.Fit(room, count, activeIndex: 0);
            var lastRowBottom = regions.PanelVisibleCount * InitiativePanelLayout.RowHeight;
            var labelBaseline = regions.LogTop - InitiativePanelLayout.LogLabelBaselineOffset;

            Assert.True(
                labelBaseline > lastRowBottom,
                $"the COMBAT LOG label's baseline ({labelBaseline}) does not clear the last panel row's own "
                    + $"bottom edge ({lastRowBottom}) at {count} combatants, height {screenHeight}");
        }
    }

    /// <summary>
    /// The busiest named case (14 combatants): the active combatant's row is inside the
    /// visible window whichever seat it occupies in the initiative order, at both
    /// resolutions — now including 1920×1080, which rounds 1 did not exercise (capacity
    /// there was 29, above 14; round 2's capacity is 8).
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh — round 2 changed which indices fail):</b> pinning
    /// <c>firstIndex</c> at 0 regardless of <c>activeIndex</c> fails this at 1920×1080
    /// for `activeIndex` **8 through 13** (capacity there is 8) and at 1280×720 for
    /// `activeIndex` **11 through 13** (capacity 11) — round 1 only ever exercised the
    /// 720p failure, since 1080p never paged before this round.
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
    /// (<see cref="InitiativePanelLayout.PanelCapacity"/> can legitimately compute 0 at
    /// a small enough <c>roomBelowHeader</c>).
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> removing <see cref="InitiativePanelLayout.Fit"/>'s
    /// <c>Math.Max(1, capacity)</c> fails this at every <c>activeIndex</c>: with 0
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

    [Fact]
    public void HiddenAboveCountNamesCombatantsEarlierInTurnOrder()
    {
        // capacity 2, floor 20 (see RoomForCapacity); active at the list's own tail
        // slides the window to [3, 5), hiding 0-2 above and nothing below.
        var room = RoomForCapacity(capacity: 2, floor: InitiativePanelLayout.MinLogLinesAt720p);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 4);

        Assert.Equal(3, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
    }

    [Fact]
    public void HiddenBelowCountNamesCombatantsLaterInTurnOrder()
    {
        var room = RoomForCapacity(capacity: 2, floor: InitiativePanelLayout.MinLogLinesAt720p);

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
        var room = RoomForCapacity(capacity: 2, floor: InitiativePanelLayout.MinLogLinesAt720p);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 1);

        Assert.Equal(1, regions.HiddenAboveCount);
        Assert.Equal(2, regions.HiddenBelowCount);
    }

    /// <summary>
    /// Combatants hidden above cost the log nothing, and (round 2) neither do
    /// combatants hidden below any more — both are named in text that already draws
    /// regardless of any count ("INITIATIVE" and "COMBAT LOG" respectively), so
    /// <c>LogTop</c> depends only on <c>PanelVisibleCount</c>, never on which direction
    /// (or whether) anything is hidden.
    /// </summary>
    [Fact]
    public void HiddenAboveAloneReservesNoExtraLogSpace()
    {
        var room = RoomForCapacity(capacity: 2, floor: InitiativePanelLayout.MinLogLinesAt720p);

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
        var room = RoomForCapacity(capacity: 2, floor: InitiativePanelLayout.MinLogLinesAt720p);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 0);

        Assert.Equal(2, regions.PanelVisibleCount);
        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(3, regions.HiddenBelowCount);
    }

    [Fact]
    public void FitWindowStartsAtTheActiveIndexWhenThereIsRoomToSlide()
    {
        var room = RoomForCapacity(capacity: 2, floor: InitiativePanelLayout.MinLogLinesAt720p);

        var regions = InitiativePanelLayout.Fit(room, combatantCount: 5, activeIndex: 1);

        Assert.Equal(1, regions.PanelFirstIndex);
        Assert.Equal(2, regions.PanelVisibleCount);
        Assert.Equal(1, regions.HiddenAboveCount);
        Assert.Equal(2, regions.HiddenBelowCount);
    }

    [Fact]
    public void FitWindowClampsToTheListsOwnEndNearTheTail()
    {
        var room = RoomForCapacity(capacity: 2, floor: InitiativePanelLayout.MinLogLinesAt720p);

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
        var room = RoomForCapacity(capacity: 2, floor: InitiativePanelLayout.MinLogLinesAt720p);

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
    /// the diff rather than only in a passing floor test. Round 2: 8 at 1080p (was 29),
    /// 11 at 720p (was 10 under round 1's separate-reserve formula — removing that
    /// reserve from the budget freed a little more room, moving the threshold by one).
    /// </summary>
    [Theory]
    [InlineData(1080f, 8)]
    [InlineData(720f, 11)]
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

    // ---- InitiativePanelLayout.LogLinesFloor -------------------------------------------

    [Theory]
    [InlineData(1080f, 45)]
    [InlineData(720f, 20)]
    public void LogLinesFloor_MatchesTheNamedResolutions(float screenHeight, int expectedFloor)
    {
        Assert.Equal(expectedFloor, InitiativePanelLayout.LogLinesFloor(RoomBelowHeader(screenHeight)));
    }
}
