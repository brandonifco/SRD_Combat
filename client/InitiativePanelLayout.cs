namespace SRDCombat.Viewer;

/// <summary>
/// How many initiative rows the panel shows, and where the combat log starts, for a
/// given window height, combatant count and active-turn index — the placement seam
/// #305 asks for, in the <see cref="ShopLayout.Fit"/> style (#704/#710): a static,
/// testable function of the numbers <c>FightScreen.DrawTurnOrder</c> and
/// <c>FightScreen.DrawLog</c> already have, returning both screens' regions in one
/// call rather than two independent pieces of arithmetic that can drift apart. #727
/// (the every-screen layout invariant) generalises this seam's shape to every other
/// screen's placement code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Brandon's decision (2026-09-10, second and final round on #305): page the panel
/// at 1920×1080 too.</b> The first round of this fix left <c>PanelCapacity</c> at 29
/// for 1080p — above every combatant count this project fields (a party of four plus a
/// warband of up to ten, 14 total) — so the floor never bound at the only resolution
/// the client ships at, and the fix changed nothing a player could see. Brandon's call:
/// keep the combat log at a stated floor even in the biggest fights by paging
/// initiative rows there too, with the active row always visible and hiding a few rows
/// in the busiest fights accepted as the cost.
/// </para>
/// <para>
/// <b>The chosen floors, and what they cost at 14 combatants</b> (measured by
/// <c>InitiativePanelLayoutTests</c>, not estimated): <see cref="MinLogLinesAt1080p"/>
/// (45) makes <see cref="PanelCapacity"/> exactly <b>8</b> at 1080p — a 14-combatant
/// fight shows 8 rows, hides 6, and the log holds <b>45 lines</b> (up from 38 under the
/// old, unpaged arithmetic — see the full count-by-count table in this class's own
/// tests and the PR body). <see cref="MinLogLinesAt720p"/> (20, unchanged from the
/// first round) makes capacity <b>11</b> at 720p — a 14-combatant fight there shows 11
/// rows, hides 3, log holds 20 lines. Two different floors rather than one shared
/// number because a single floor tuned for 1080p's 968px of room is unreachable at
/// 720p's 608px without collapsing the panel to a single row for every fight (measured:
/// a 45-line floor at 720p computes a capacity of 0 even hiding everyone but the active
/// combatant caps out at 31 lines) — the task's own "or a per-height floor" allowance.
/// </para>
/// <para>
/// <b>Hiding a row must never cost the log a line</b> (qc's second-round review: the
/// first round's 720p/11 case hid a row and gained nothing, because a separate "N more
/// below" line cost its own 16px of newly-reserved space against the 19px a hidden row
/// frees — a one-time cost that could match or exceed the first row's own gain,
/// depending on where the two independent roundings happened to land). The fix folds
/// the "below" notice into the existing "COMBAT LOG" label itself
/// (<c>FightScreen.DrawLog</c>: <c>"COMBAT LOG — N below"</c>) exactly the way the
/// first round already folded the "above" notice into the existing "INITIATIVE"
/// header — neither notice ever costs new vertical space, so <see cref="Fit"/>'s
/// <c>logTop</c> is <i>always</i> <c>visibleCount * RowHeight + GapAfterPanel</c>, paged
/// or not. That is not merely convenient: it is what makes every hidden row a strict,
/// provable log-line gain, since hiding one more row always frees exactly <see
/// cref="RowHeight"/> (19px), which exceeds <see cref="LogLineHeight"/> (17px) — so
/// <c>floor((logRoom + 19) / 17) &gt; floor(logRoom / 17)</c> for every possible
/// <c>logRoom</c>, unconditionally. No per-count exception is needed, and none is
/// pinned as one; <c>HidingARowNeverCostsTheLogALine</c> checks all fourteen pinned
/// counts at both heights precisely because the guarantee is structural, not
/// coincidental.
/// </para>
/// <para>
/// <b>The panel pages rather than compacting</b> ("should shrink" versus "should page"
/// being #305's own open design question, independent of the capacity/floor numbers
/// above). Shrinking the row height or font as the party grows has no floor — a
/// warband large enough always finds a size too small to read, which would leave
/// #727's own invariant (every control stays on screen and legible) special-casing
/// this one panel. The merchant's stall already answered the identical shape — an
/// unbounded list competing with fixed chrome for the same finite height — by paging
/// rather than shrinking its offers (#704), and the initiative list is that same shape
/// again.
/// </para>
/// <para>
/// <b>The window follows the active combatant, not the top of the list</b> (the
/// <c>activeIndex</c> parameter of <see cref="Fit"/>): initiative order is fixed for
/// the whole fight, so a window pinned to index 0 would lose whoever is acting the
/// moment the active turn advanced past <see cref="PanelCapacity"/> rows into the
/// round. <c>PanelFirstIndex</c> starts at <c>activeIndex</c> and slides back only far
/// enough to keep the window inside the list, so every reachable window still contains
/// the active row.
/// </para>
/// <para>
/// <b>The active row always shows, even where the log's own floor cannot</b>: a room
/// small enough that <see cref="PanelCapacity"/> computes 0 would otherwise show no
/// panel rows at all — including the one combatant this panel exists to always track —
/// while still (nonsensically) claiming a windowed view. <see cref="Fit"/> therefore
/// never shows fewer than one row once the list overflows (<c>Math.Max(1, capacity)</c>):
/// the log's own floor is the one that yields at that extreme, not the active row's
/// visibility.
/// </para>
/// <para>
/// <b>Hidden combatants are reported on whichever side they are actually hidden</b>: a
/// window that starts at <c>activeIndex</c> can leave combatants hidden <i>above</i> it
/// (everyone earlier in turn order this round), <i>below</i> it (everyone later), or
/// both at once — <see cref="Regions.HiddenAboveCount"/> and
/// <see cref="Regions.HiddenBelowCount"/> are reported separately rather than as one
/// combined count, because a single "+N more" line drawn below the rows would say
/// "more" even when every hidden combatant is actually earlier in the order, already
/// acted, and not merely undrawn.
/// </para>
/// <para>
/// <b>None of these hidden rows are reachable by any input.</b> A click anywhere on
/// the panel's own column is chrome, not a square
/// (<c>PlayMode.OverOverlay</c>: <c>pixel.X >= PanelLeft - 16</c>), so it backs out of
/// anything armed rather than acting on a hidden row; the mouse wheel during a fight is
/// the camera's zoom, not a panel scroll (wheel scrolling exists only for the
/// merchant's stall, <c>HandleShopWheelInput</c>). So unlike <see cref="ShopLayout"/>'s
/// own offer list — which the wheel and Page Up/Down do scroll — this panel borrows
/// only <c>Fit</c>'s windowing shape from that pattern, not its scrolling half: a
/// hidden row is genuinely off-screen for the whole turn it stays hidden, recoverable
/// only by watching the active window slide as turns pass.
/// </para>
/// </remarks>
internal static class InitiativePanelLayout
{
    /// <summary>An initiative row's own height — unchanged from <c>DrawTurnOrder</c>'s existing <c>y += 19</c>.</summary>
    internal const float RowHeight = 19f;

    /// <summary>
    /// The gap between the panel's rows and the combat log's first line — holds the
    /// "COMBAT LOG" label, which draws <see cref="LogLabelBaselineOffset"/> above the
    /// first line. Matches the literal <c>26</c> <c>DrawLog</c> used to add directly
    /// onto <c>tokenCount * 19</c> before #305. This is the <i>only</i> gap
    /// <see cref="Fit"/> ever adds between the rows and the log — see the type-level
    /// remarks on why paging never costs a second, separate reserve.
    /// </summary>
    internal const float GapAfterPanel = 26f;

    /// <summary>
    /// How far above the log's first content line the "COMBAT LOG" label's own baseline
    /// sits (<c>DrawLog</c>'s <c>top - LogLabelBaselineOffset</c>) — named so a test
    /// reads the same value the drawing call does rather than a copied literal (Codex
    /// review round, #710's own precedent).
    /// </summary>
    internal const float LogLabelBaselineOffset = 12f;

    /// <summary>A log line's own height — unchanged from <c>DrawLog</c>'s existing <c>y += 17</c>.</summary>
    internal const float LogLineHeight = 17f;

    /// <summary>The gap <c>DrawLog</c> leaves below its last line and the window's own bottom edge.</summary>
    internal const float LogFooterMargin = 20f;

    /// <summary>
    /// The log's guaranteed floor at 1920×1080, in lines — chosen (PR #746, round 2) so
    /// that <see cref="PanelCapacity"/> computes 8 there: a 14-combatant fight, the
    /// worst case this project fields, shows 8 rows, hides 6, and holds the log at 45
    /// lines (up from 38 under the pre-#305 arithmetic). See the type-level remarks for
    /// why 1080p and 720p carry different floors.
    /// </summary>
    internal const int MinLogLinesAt1080p = 45;

    /// <summary>
    /// The log's guaranteed floor at 1280×720, in lines — unchanged from #305's first
    /// round. Kept lower than <see cref="MinLogLinesAt1080p"/> because 720p has far
    /// less room to begin with (608px below the panel's header against 1080p's 968px):
    /// a 45-line floor there computes a capacity of 0, collapsing every fight to a
    /// single visible row for a ceiling of only 31 lines even then — worse for every
    /// fight, not better. <see cref="PanelCapacity"/> at 720p with this floor is 11.
    /// </summary>
    internal const int MinLogLinesAt720p = 20;

    /// <summary>
    /// The room, in pixels, above which <see cref="MinLogLinesAt1080p"/> applies rather
    /// than <see cref="MinLogLinesAt720p"/> — 1080p's own <c>roomBelowHeader</c> is 968,
    /// 720p's is 608, so 800 sits cleanly between the two named resolutions this seam
    /// is tuned for. This project tests and ships at exactly those two heights (see
    /// every other layout seam's own <c>ScreenHeights</c> theory data); a continuous
    /// formula scaling the floor with arbitrary room was not built because there is no
    /// third resolution to tune it against.
    /// </summary>
    internal const float FloorThresholdRoom = 800f;

    /// <summary>
    /// The log's guaranteed floor, in lines, for a given <paramref name="roomBelowHeader"/> —
    /// <see cref="MinLogLinesAt1080p"/> above <see cref="FloorThresholdRoom"/>,
    /// <see cref="MinLogLinesAt720p"/> at or below it.
    /// </summary>
    internal static int LogLinesFloor(float roomBelowHeader) =>
        roomBelowHeader > FloorThresholdRoom ? MinLogLinesAt1080p : MinLogLinesAt720p;

    /// <summary>
    /// The panel's visible rows and the log's own top and line budget, for one frame —
    /// the regions <see cref="Fit"/> hands back together so neither
    /// <c>DrawTurnOrder</c> nor <c>DrawLog</c> computes the boundary between them a
    /// second, independent way.
    /// </summary>
    /// <param name="PanelFirstIndex">The first combatant index the panel draws.</param>
    /// <param name="PanelVisibleCount">How many rows, from <see cref="PanelFirstIndex"/>, the panel draws. Never 0 when <c>combatantCount</c> is at least 1.</param>
    /// <param name="HiddenAboveCount">
    /// Combatants earlier in turn order than <see cref="PanelFirstIndex"/>, left off the
    /// visible window — 0 when the window starts at index 0. Reported in the
    /// "INITIATIVE" header, not a reserved line, since that space exists regardless of
    /// this count.
    /// </param>
    /// <param name="HiddenBelowCount">
    /// Combatants later in turn order than the visible window's own end, left off it —
    /// 0 when the window reaches the list's end. Reported in the "COMBAT LOG" label
    /// itself, not a reserved line — see the type-level remarks on why paging must
    /// never cost a separate reserve.
    /// </param>
    /// <param name="LogTop">
    /// Where the combat log's own content starts, measured from the same origin
    /// <c>roomBelowHeader</c> in <see cref="Fit"/> was measured from (the panel's first
    /// row — <c>UiTop + 16</c> in <c>FightScreen</c>). Always exactly
    /// <c>PanelVisibleCount * RowHeight + GapAfterPanel</c>, whether or not anything is
    /// hidden.
    /// </param>
    /// <param name="LogLines">
    /// How many log lines fit below <see cref="LogTop"/> — never less than
    /// <see cref="LogLinesFloor"/> for any window large enough for at least one panel
    /// row through <see cref="PanelCapacity"/> to hold that promise; at a window smaller
    /// than that, the active row's own visibility takes priority instead (see the
    /// type-level remarks).
    /// </param>
    internal readonly record struct Regions(
        int PanelFirstIndex,
        int PanelVisibleCount,
        int HiddenAboveCount,
        int HiddenBelowCount,
        float LogTop,
        int LogLines);

    /// <summary>
    /// The most panel rows <see cref="Fit"/> will page down to for the given room,
    /// leaving <see cref="LogLinesFloor"/> lines plus every fixed margin clear beneath
    /// them — not a floor on how few rows ever show (see <see cref="Fit"/>'s own
    /// <c>Math.Max(1, …)</c> for that).
    /// </summary>
    internal static int PanelCapacity(float roomBelowHeader)
    {
        var floor = LogLinesFloor(roomBelowHeader);
        var reserved = GapAfterPanel + LogFooterMargin + (floor * LogLineHeight);
        var available = roomBelowHeader - reserved;

        return Math.Max(0, (int)(available / RowHeight));
    }

    /// <summary>
    /// The log's own line count for a panel showing exactly <paramref
    /// name="visibleCount"/> rows — the pure function <see cref="Fit"/>'s own
    /// <c>logLines</c> line is, exposed so a test can compute the "one more row shown"
    /// counterfactual <see cref="Fit"/>'s hidden-row guarantee rests on without
    /// duplicating the arithmetic as a second, independent copy.
    /// </summary>
    internal static int LogLinesForVisibleCount(float roomBelowHeader, int visibleCount)
    {
        var logTop = (visibleCount * RowHeight) + GapAfterPanel;
        var logRoom = roomBelowHeader - logTop - LogFooterMargin;

        return Math.Max(0, (int)(logRoom / LogLineHeight));
    }

    /// <summary>
    /// Splits <paramref name="roomBelowHeader"/> between the initiative panel and the
    /// combat log.
    /// </summary>
    /// <param name="roomBelowHeader">
    /// The window's own height, less everything above the panel's first row (<c>UiTop
    /// + 16</c> in <c>FightScreen</c>) — the same "room, not absolute position"
    /// convention <see cref="ShopLayout.Fit"/> already uses.
    /// </param>
    /// <param name="combatantCount">How many rows the initiative list has to show.</param>
    /// <param name="activeIndex">
    /// The active combatant's index into that same list — clamped into range, so an
    /// out-of-range value (nobody active between rounds) behaves as index 0.
    /// </param>
    internal static Regions Fit(float roomBelowHeader, int combatantCount, int activeIndex)
    {
        var capacity = PanelCapacity(roomBelowHeader);

        int firstIndex;
        int visibleCount;
        int hiddenAbove;
        int hiddenBelow;

        if (combatantCount <= capacity)
        {
            firstIndex = 0;
            visibleCount = combatantCount;
            hiddenAbove = 0;
            hiddenBelow = 0;
        }
        else
        {
            // However small the window, the active combatant's own row always shows —
            // showing zero rows (the one this panel exists to track included) is worse
            // than a log that falls short of LogLinesFloor at a window this degenerate.
            visibleCount = Math.Max(1, capacity);

            var clampedActive = Math.Clamp(activeIndex, 0, combatantCount - 1);
            firstIndex = Math.Clamp(clampedActive, 0, combatantCount - visibleCount);
            hiddenAbove = firstIndex;
            hiddenBelow = combatantCount - (firstIndex + visibleCount);
        }

        // No separate reserve for either hidden direction: "above" is named in the
        // existing "INITIATIVE" header and "below" in the existing "COMBAT LOG" label
        // (FightScreen.DrawTurnOrder / DrawLog), so LogTop is always exactly the rows
        // plus the one fixed gap — see the type-level remarks on why this is what makes
        // every hidden row a guaranteed log-line gain rather than merely a usual one.
        var logTop = (visibleCount * RowHeight) + GapAfterPanel;
        var logLines = LogLinesForVisibleCount(roomBelowHeader, visibleCount);

        return new Regions(firstIndex, visibleCount, hiddenAbove, hiddenBelow, logTop, logLines);
    }
}
