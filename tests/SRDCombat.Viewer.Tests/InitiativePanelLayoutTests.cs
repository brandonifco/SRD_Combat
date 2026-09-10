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
/// <b>Round 3 (qc's second review of PR #746): capacity is a constant, not a
/// room-dependent climb.</b> Round 2 picked the log's floor by a hard threshold on
/// room (45 lines above 800px, 20 at or below), then derived capacity from that
/// per-room floor. qc found the composition badly non-monotone — capacity 21 at room
/// 800, 1 at room 801, not recovering to 21 until room 1210 — and traced it to
/// something deeper than a threshold picked wrong: <i>any</i> formula that lets
/// capacity climb smoothly from a small value toward a larger one, as room grows,
/// cannot avoid a real dip in the log's own displayed line count at the climb itself.
/// </para>
/// <para>
/// <b>The proof, worked out here since it is what justifies making capacity a
/// constant</b>: showing one more row always frees exactly <see
/// cref="InitiativePanelLayout.RowHeight"/> (19px) of panel space, and a log line
/// costs <see cref="InitiativePanelLayout.LogLineHeight"/> (17px) — different amounts.
/// Let <c>lines(room, C) = floor((room - 19C - 46) / 17)</c> be the log's line count
/// showing <c>C</c> rows (<see cref="InitiativePanelLayout.LogLinesForVisibleCount"/>
/// is this, plus the fixed offsets). Fix a target row count increase from <c>C</c> to
/// <c>C+1</c> at whatever room <c>R</c> the climb happens. Immediately before, at
/// <c>R-1</c> and still showing <c>C</c> rows, the log had <c>lines(R-1, C)</c> —
/// which has been climbing linearly in room the entire time capacity sat at <c>C</c>,
/// with no ceiling. Immediately after, at <c>R</c> and now showing <c>C+1</c> rows, it
/// has <c>lines(R, C+1) = floor((R - 19(C+1) - 46)/17) = floor((R - 19C - 65)/17)</c> —
/// exactly <c>18</c> less in the numerator than <c>lines(R-1, C) = floor((R - 19C -
/// 47)/17)</c> would have been at the very same room. Since <c>18 &gt; 17</c>, that
/// floored quotient drops by at least 1 (worked as an exact case split: writing
/// <c>R - 19C - 47 = 17q + r</c> with <c>0 &lt;= r &lt; 17</c>, the drop is exactly 2
/// when <c>r = 0</c> and exactly 1 otherwise — never 0). Delaying the climb does not
/// help: waiting longer only lets <c>lines(R-1, C)</c> climb higher still, widening the
/// gap <c>lines(R, C+1)</c> has to close, not narrowing it. So the dip is not a bug in
/// any one threshold choice — it is forced by <c>RowHeight &gt; LogLineHeight</c>
/// itself, for every possible climb, at every possible room.
/// <see cref="CapacityAndLogLinesAreMonotoneInRoom"/> is the sweep that would have
/// caught this had it existed for round 2.
/// </para>
/// <para>
/// <b>The fix: capacity never climbs inside any room this project's window can reach,
/// or that the sweep checks.</b> <see cref="InitiativePanelLayout.PanelCapacity"/> is
/// the constant <see cref="InitiativePanelLayout.MinRows"/> (8 — what 1080p already
/// showed at 14 combatants under round 2) for every room at or above <see
/// cref="InitiativePanelLayout.ShrinkBelowRoom"/> (300) — comfortably below both this
/// sweep's own start (400) and the smallest room any real window can produce (428, the
/// enforced 960×540 minimum). Below that threshold it is a hard 0 (which <see
/// cref="InitiativePanelLayout.Fit"/>'s own <c>Math.Max(1, …)</c> turns into "just the
/// active row," never something in between) — not a graduated shrink, because a
/// graduated shrink is exactly the climb the proof above rules out, and nothing
/// reachable, or tested, ever needs one.
/// </para>
/// <para>
/// <b>1080p is unchanged: 8 rows, 45 lines at 14 combatants</b> — <see
/// cref="InitiativePanelLayout.MinRows"/> was chosen to equal round 2's own 1080p
/// capacity exactly. <b>1280×720 changes from round 2's 11 rows to 8</b>: with one
/// constant capacity shared by both resolutions, 720p's shorter room simply produces
/// fewer log lines at that same row count (24, not round 2's 20) — round 2's 11 was
/// never a deliberate choice, only round 1's separate per-height floor constant
/// working out to that number incidentally. <see cref="ExactTableForBothResolutions"/>
/// is the full, freshly measured 1–14 table at both heights.
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

    /// <summary>A room comfortably above <see cref="InitiativePanelLayout.ShrinkBelowRoom"/>, for tests that need a deterministic capacity of <see cref="InitiativePanelLayout.MinRows"/> without tying themselves to either named resolution.</summary>
    private const float RoomWellAboveShrinkThreshold = 1000f;

    private static float RoomBelowHeader(float screenHeight) => screenHeight - HeaderBottom;

    private static IEnumerable<int> AllCounts()
    {
        for (var count = 1; count <= 14; count++)
        {
            yield return count;
        }
    }

    // ---- The exact table ---------------------------------------------------------------

    /// <summary>
    /// Every one of the 28 (count, height) pairs this PR pins, computed once here and
    /// quoted verbatim in the PR body — "quote only numbers the tests computed" applies
    /// to this table as much as to any knockout result. Capacity is 8 at both
    /// resolutions now (round 3); both plateau at their own room's own line count the
    /// moment paging starts and never move again as the count keeps growing.
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
    [InlineData(720f, 9, 8, 1, 24)]
    [InlineData(720f, 10, 8, 2, 24)]
    [InlineData(720f, 11, 8, 3, 24)]
    [InlineData(720f, 12, 8, 4, 24)]
    [InlineData(720f, 13, 8, 5, 24)]
    [InlineData(720f, 14, 8, 6, 24)]
    public void ExactTableForBothResolutions(
        float screenHeight, int count, int expectedShown, int expectedHidden, int expectedLines)
    {
        var regions = InitiativePanelLayout.Fit(RoomBelowHeader(screenHeight), count, activeIndex: 0);

        Assert.Equal(expectedShown, regions.PanelVisibleCount);
        Assert.Equal(expectedHidden, regions.HiddenBelowCount);
        Assert.Equal(expectedLines, regions.LogLines);
    }

    /// <summary>
    /// The two intermediate rooms qc's review cited by name as where round 2's formula
    /// broke worst (800/801, the cliff's edge, and 848/912, deep inside the violated
    /// 43–45 band) — pinned directly at 14 combatants, not only at the two named
    /// resolutions.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh):</b> reintroducing round 2's per-room threshold
    /// (<c>roomBelowHeader &gt; 800 ? capacity-from-a-45-line-floor :
    /// capacity-from-a-20-line-floor</c>) fails both rows and
    /// <see cref="CapacityAndLogLinesAreMonotoneInRoom"/> — it computes 1 shown / 13
    /// hidden / 46 lines at room 848 (not 8 / 6 / 38: the reintroduced 45-line floor at
    /// this room leaves capacity 1, clamped up from a raw 1, not 8) and 5 shown / 9
    /// hidden / 45 lines at room 912 (not 8 / 6 / 42). The monotonicity sweep itself
    /// fails first, and earlier, at the exact shape qc's review named: lines drop from
    /// 21 to 20 going from room 423 to 424.
    /// </remarks>
    [Theory]
    [InlineData(848f, 8, 6, 38)]
    [InlineData(912f, 8, 6, 42)]
    public void ExactValuesAtTheCitedIntermediateRooms(
        float room, int expectedShown, int expectedHidden, int expectedLines)
    {
        var regions = InitiativePanelLayout.Fit(room, combatantCount: 14, activeIndex: 0);

        Assert.Equal(expectedShown, regions.PanelVisibleCount);
        Assert.Equal(expectedHidden, regions.HiddenBelowCount);
        Assert.Equal(expectedLines, regions.LogLines);
    }

    // ---- The monotonicity sweep (qc's round-3 finding) ---------------------------------

    /// <summary>
    /// The literal fix: for every room from 400 to 1100, at 14 combatants (this
    /// project's own worst case), neither the panel's own row count nor the log's line
    /// count ever decreases as the window grows. 400 is comfortably below every room
    /// this project's window can produce (960×540's own enforced minimum is 428) and
    /// 1100 comfortably above 1080p's own 968 — the sweep spans every room a resize
    /// could plausibly reach and then some.
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> reintroducing round 2's per-room-threshold capacity (see <see
    /// cref="ExactValuesAtTheCitedIntermediateRooms"/>'s own knockout) fails this
    /// immediately — capacity drops from 21 at room 800 to 1 at room 801, the exact
    /// cliff qc's review named.
    /// </remarks>
    [Fact]
    public void CapacityAndLogLinesAreMonotoneInRoom()
    {
        var previousCapacity = InitiativePanelLayout.PanelCapacity(400f);
        var previousLines = InitiativePanelLayout.Fit(400f, combatantCount: 14, activeIndex: 0).LogLines;

        for (var room = 401; room <= 1100; room++)
        {
            var capacity = InitiativePanelLayout.PanelCapacity(room);
            var lines = InitiativePanelLayout.Fit(room, combatantCount: 14, activeIndex: 0).LogLines;

            Assert.True(
                capacity >= previousCapacity,
                $"PanelCapacity dropped from {previousCapacity} to {capacity} going from room {room - 1} to {room}");
            Assert.True(
                lines >= previousLines,
                $"log lines dropped from {previousLines} to {lines} going from room {room - 1} to {room}");

            previousCapacity = capacity;
            previousLines = lines;
        }
    }

    /// <summary>
    /// A window taller than any this project names does not grow the panel past <see
    /// cref="InitiativePanelLayout.MinRows"/> — every extra pixel goes straight to the
    /// log instead, which is what keeps <see cref="CapacityAndLogLinesAreMonotoneInRoom"/>'s
    /// guarantee unconditional rather than bounded to the swept range.
    /// </summary>
    [Fact]
    public void LogLinesGrowsWithoutBoundOnceCapacityIsFixed()
    {
        var atNamedMax = InitiativePanelLayout.Fit(RoomBelowHeader(1080f), combatantCount: 14, activeIndex: 0);
        var atADoubledWindow = InitiativePanelLayout.Fit(2000f, combatantCount: 14, activeIndex: 0);

        Assert.Equal(InitiativePanelLayout.MinRows, atNamedMax.PanelVisibleCount);
        Assert.Equal(InitiativePanelLayout.MinRows, atADoubledWindow.PanelVisibleCount);
        Assert.True(
            atADoubledWindow.LogLines > atNamedMax.LogLines,
            $"a 2000px room got {atADoubledWindow.LogLines} log lines, no more than 1080p's own {atNamedMax.LogLines}");
    }

    // ---- The acceptance criteria, verbatim -------------------------------------------

    /// <summary>
    /// "The log's usable space no longer shrinks as the initiative list grows" — the
    /// log never drops below its own room's line count at 8 rows shown, at every named
    /// count and both named resolutions, active turn anywhere in the order.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh):</b> reverting <see
    /// cref="InitiativePanelLayout.PanelCapacity"/> to return <c>int.MaxValue</c> (the
    /// pre-#305 behaviour — every combatant always shown, the log always pushed down to
    /// make room) fails this at both resolutions: 1280×720 first fails at **9
    /// combatants** (`log got 23 lines... below the floor of 24`); 1920×1080 first
    /// fails at **9 combatants** too (`log got 44 lines... below the floor of 45`).
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void LogNeverDropsBelowTheStatedMinimum(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);
        var floor = InitiativePanelLayout.Fit(room, InitiativePanelLayout.MinRows, activeIndex: 0).LogLines;

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
    /// The opposite polarity: below <see cref="InitiativePanelLayout.MinRows"/>,
    /// nothing pages at all — every combatant shows, and the log's own top carries no
    /// reserve for a "below" notice it never draws.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreenHeights))]
    public void PanelShowsEveryCombatantWhenTheListFitsAboveTheFloor(float screenHeight)
    {
        var room = RoomBelowHeader(screenHeight);
        var capacity = InitiativePanelLayout.PanelCapacity(room);

        Assert.Equal(InitiativePanelLayout.MinRows, capacity);

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
            Assert.Equal(
                (count * InitiativePanelLayout.RowHeight) + InitiativePanelLayout.GapAfterPanel,
                regions.LogTop);
        }
    }

    /// <summary>
    /// The other named acceptance criterion: the log's own drawn content never runs
    /// past the window's bottom edge, at every pinned count and both resolutions.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh, unchanged from earlier rounds):</b> changing <see
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
    /// Paging must never hide a row without buying the log at least one more line.
    /// Compares the fight's own actual (hidden, logLines) against the counterfactual of
    /// showing exactly one more row (<see cref="InitiativePanelLayout.LogLinesForVisibleCount"/>)
    /// — a per-combatant-count comparison at a fixed room, unrelated to (and unaffected
    /// by) round 3's own capacity-vs-room fix above; this guarantee has held since
    /// round 2 folded both hidden directions into text that already draws.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh):</b> reintroducing round 1's separate "more below"
    /// reserve (adding 16px to <c>logTop</c> whenever <c>hiddenBelow &gt; 0</c>) fails
    /// this at **9 combatants, 1920×1080** (actual 44 lines, counterfactual 44 — a tie)
    /// and at **9 combatants, 1280×720** (actual 23, counterfactual 23).
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
    /// or not — because neither hidden direction reserves a line of its own.
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
    /// The "COMBAT LOG" label's own baseline never sits above the last panel row's own
    /// bottom edge.
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
    /// resolutions — capacity is now 8 at both (round 3), so both fail identically
    /// under the knockout below, unlike round 2 where only 1080p was newly exercised.
    /// </summary>
    /// <remarks>
    /// <b>Knockout (measured fresh):</b> pinning <c>firstIndex</c> at 0 regardless of
    /// <c>activeIndex</c> fails this at *both* resolutions for `activeIndex` **8
    /// through 13** — capacity is 8 at both now.
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
    /// floor is what yields at a room this degenerate, not the active row's visibility.
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
        // capacity 8 (RoomWellAboveShrinkThreshold); 14 combatants, active at the
        // list's own tail slides the window to [6, 14), hiding 0-5 above and nothing
        // below.
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 14, activeIndex: 13);

        Assert.Equal(6, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
    }

    [Fact]
    public void HiddenBelowCountNamesCombatantsLaterInTurnOrder()
    {
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 14, activeIndex: 0);

        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(6, regions.HiddenBelowCount);
    }

    /// <summary>
    /// A window that starts mid-list hides combatants on both sides at once — a single
    /// combined "+N more" count could never say this correctly no matter which side it
    /// was drawn on.
    /// </summary>
    /// <remarks>
    /// <b>Knockout:</b> reverting to a single combined count (reporting the total as
    /// <c>HiddenBelowCount</c> and always 0 for <c>HiddenAboveCount</c>) fails this —
    /// the total here is 6 (3 above, 3 below), which a combined-below reading would
    /// report as 0 above, 6 below instead of 3 above, 3 below.
    /// </remarks>
    [Fact]
    public void BothDirectionsCanBeHiddenAtOnce()
    {
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 14, activeIndex: 3);

        Assert.Equal(3, regions.HiddenAboveCount);
        Assert.Equal(3, regions.HiddenBelowCount);
    }

    /// <summary>
    /// Combatants hidden above cost the log nothing, and neither do combatants hidden
    /// below — both are named in text that already draws regardless of any count
    /// ("INITIATIVE" and "COMBAT LOG" respectively), so <c>LogTop</c> depends only on
    /// <c>PanelVisibleCount</c>, never on which direction (or whether) anything is
    /// hidden.
    /// </summary>
    [Fact]
    public void HiddenAboveAloneReservesNoExtraLogSpace()
    {
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 14, activeIndex: 13);

        Assert.Equal(6, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
        Assert.Equal(
            (InitiativePanelLayout.MinRows * InitiativePanelLayout.RowHeight) + InitiativePanelLayout.GapAfterPanel,
            regions.LogTop);
    }

    // ---- InitiativePanelLayout.Fit on its own ------------------------------------------

    [Fact]
    public void FitShowsEveryCombatantWhenTheyAllFit()
    {
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 4, activeIndex: 1);

        Assert.Equal(0, regions.PanelFirstIndex);
        Assert.Equal(4, regions.PanelVisibleCount);
        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
    }

    [Fact]
    public void FitPagesWhenMoreCombatantsThanCapacityFit()
    {
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 14, activeIndex: 0);

        Assert.Equal(InitiativePanelLayout.MinRows, regions.PanelVisibleCount);
        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(6, regions.HiddenBelowCount);
    }

    [Fact]
    public void FitWindowStartsAtTheActiveIndexWhenThereIsRoomToSlide()
    {
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 14, activeIndex: 3);

        Assert.Equal(3, regions.PanelFirstIndex);
        Assert.Equal(InitiativePanelLayout.MinRows, regions.PanelVisibleCount);
        Assert.Equal(3, regions.HiddenAboveCount);
        Assert.Equal(3, regions.HiddenBelowCount);
    }

    [Fact]
    public void FitWindowClampsToTheListsOwnEndNearTheTail()
    {
        // Active is the very last combatant (index 13 of 14); a window starting there
        // would run past the list, so it clamps back to [6, 14).
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 14, activeIndex: 13);

        Assert.Equal(6, regions.PanelFirstIndex);
        Assert.Equal(InitiativePanelLayout.MinRows, regions.PanelVisibleCount);
        Assert.InRange(13, regions.PanelFirstIndex, regions.PanelFirstIndex + regions.PanelVisibleCount - 1);
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
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 14, activeIndex: -1);

        Assert.Equal(0, regions.PanelFirstIndex);
        Assert.Equal(InitiativePanelLayout.MinRows, regions.PanelVisibleCount);
    }

    [Fact]
    public void FitHandlesAnEmptyCombatantList()
    {
        var regions = InitiativePanelLayout.Fit(RoomWellAboveShrinkThreshold, combatantCount: 0, activeIndex: 0);

        Assert.Equal(0, regions.PanelVisibleCount);
        Assert.Equal(0, regions.HiddenAboveCount);
        Assert.Equal(0, regions.HiddenBelowCount);
    }

    // ---- InitiativePanelLayout.PanelCapacity -------------------------------------------

    /// <summary>
    /// The exact capacity at both named resolutions — 8 at both now (round 3; was 8 and
    /// 11 respectively under round 2's separate per-height floors).
    /// </summary>
    [Theory]
    [InlineData(1080f, 8)]
    [InlineData(720f, 8)]
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

    /// <summary>
    /// The step itself: 0 immediately below <see
    /// cref="InitiativePanelLayout.ShrinkBelowRoom"/>, <see
    /// cref="InitiativePanelLayout.MinRows"/> immediately at or above it — a hard step,
    /// not a graduated ramp, because a ramp is exactly the climb this round's own fix
    /// rules out (see the class-level remarks' proof).
    /// </summary>
    [Fact]
    public void PanelCapacity_StepsCleanlyAtTheShrinkThreshold()
    {
        var threshold = InitiativePanelLayout.ShrinkBelowRoom;

        Assert.Equal(0, InitiativePanelLayout.PanelCapacity(threshold - 1f));
        Assert.Equal(InitiativePanelLayout.MinRows, InitiativePanelLayout.PanelCapacity(threshold));
    }
}
