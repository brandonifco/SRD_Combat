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
/// <b>The bug this closes</b>: <c>DrawLog</c> used to start its own <c>top</c> at
/// <c>UiTop + 16 + (tokenCount * 19) + 26</c> — a warband's row count fed straight into
/// the log's starting position with nothing capping it, so the log's own room
/// (<c>(ScreenHeight - top - 20) / 17</c>) shrank one line for every combatant the
/// fight added, worst exactly when a fight was busiest (the issue's own title). Nothing
/// stopped a big enough warband from reducing the log to a handful of lines, which is
/// what #161 already named as a real failure mode: a narrow log is a log more likely to
/// need every character it is given, right when the panel above it is taking the most.
/// </para>
/// <para>
/// <b>The panel pages rather than compacting</b> ("should shrink" versus "should page"
/// being #305's own open design question). Shrinking the row height or font as the party
/// grows has no floor — a warband large enough always finds a size too small to read,
/// which would leave #727's own invariant (every control stays on screen and legible)
/// special-casing this one panel. The merchant's stall already answered the identical
/// shape — an unbounded list competing with fixed chrome for the same finite height —
/// by paging rather than shrinking its offers (#704), and the initiative list is that
/// same shape again: a row is either fully legible or it is the next page's problem,
/// never a smaller version of itself.
/// </para>
/// <para>
/// <b>The window follows the active combatant, not the top of the list</b> (the
/// <c>activeIndex</c> parameter of <see cref="Fit"/>): initiative order is fixed for
/// the whole fight, so a window pinned to index 0 would lose whoever is acting the
/// moment the active turn advanced past <see cref="PanelCapacity"/> rows into the round
/// — the one combatant this panel exists to always show. <c>PanelFirstIndex</c> starts
/// at <c>activeIndex</c> (showing who is up now, then as many upcoming turns as fit)
/// and slides back only far enough to keep the window inside the list, so every
/// reachable window still contains the active row.
/// </para>
/// <para>
/// <b><see cref="MinLogLines"/> is a floor, not the log's usual size.</b> At 1920×1080
/// it never binds for any fight this project fields (a warband of ten plus the party of
/// four, #305's own named worst case) — the panel simply never pages there. At
/// 1280×720 it starts paging the panel at the same combatant count the log's own room
/// was already down to under the old, uncapped arithmetic, so the floor is not a new
/// number so much as the smallest fight's headroom made permanent — a bigger warband
/// can no longer erode it further.
/// </para>
/// </remarks>
internal static class InitiativePanelLayout
{
    /// <summary>An initiative row's own height — unchanged from <c>DrawTurnOrder</c>'s existing <c>y += 19</c>.</summary>
    internal const float RowHeight = 19f;

    /// <summary>
    /// Room for the "+N more" line below the visible panel rows, drawn only when the
    /// panel is actually paged (<see cref="Fit"/> reserves it exactly when <c>
    /// PanelHiddenCount</c> would be positive) — unlike <see
    /// cref="ShopLayout.MoreLineReserve"/>, which the stall reserves unconditionally.
    /// A fight under <see cref="PanelCapacity"/> never pages, so it pays nothing for a
    /// line it never draws; <see cref="PanelCapacity"/> itself still budgets for this
    /// unconditionally, so the floor <see cref="MinLogLines"/> promises holds regardless
    /// of which branch a given frame takes.
    /// </summary>
    internal const float MoreLineReserve = 16f;

    /// <summary>
    /// The gap between the panel's rows (or the reserved "+N more" line below them) and
    /// the combat log's first line — holds the "COMBAT LOG" label, which draws 12px
    /// above the first line. Matches the literal <c>26</c> <c>DrawLog</c> used to add
    /// directly onto <c>tokenCount * 19</c> before this change.
    /// </summary>
    internal const float GapAfterPanel = 26f;

    /// <summary>A log line's own height — unchanged from <c>DrawLog</c>'s existing <c>y += 17</c>.</summary>
    internal const float LogLineHeight = 17f;

    /// <summary>The gap <c>DrawLog</c> leaves below its last line and the window's own bottom edge.</summary>
    internal const float LogFooterMargin = 20f;

    /// <summary>
    /// The log's guaranteed floor, in lines, at every combatant count and both named
    /// resolutions (1920×1080, 1280×720) — the number <see cref="PanelCapacity"/> is
    /// solved backwards from.
    /// </summary>
    internal const int MinLogLines = 20;

    /// <summary>
    /// The panel's visible rows and the log's own top and line budget, for one frame —
    /// the two regions <see cref="Fit"/> hands back together so neither
    /// <c>DrawTurnOrder</c> nor <c>DrawLog</c> computes the boundary between them a
    /// second, independent way.
    /// </summary>
    /// <param name="PanelFirstIndex">The first combatant index the panel draws.</param>
    /// <param name="PanelVisibleCount">How many rows, from <see cref="PanelFirstIndex"/>, the panel draws.</param>
    /// <param name="PanelHiddenCount">Combatants left off the visible window — 0 when everyone fits.</param>
    /// <param name="LogTop">
    /// Where the combat log's own content starts, measured from the same origin
    /// <c>roomBelowHeader</c> in <see cref="Fit"/> was measured from (the panel's first
    /// row — <c>UiTop + 16</c> in <c>FightScreen</c>).
    /// </param>
    /// <param name="LogLines">
    /// How many log lines fit below <see cref="LogTop"/> — never less than
    /// <see cref="MinLogLines"/>, by construction of <see cref="PanelCapacity"/>.
    /// </param>
    internal readonly record struct Regions(
        int PanelFirstIndex,
        int PanelVisibleCount,
        int PanelHiddenCount,
        float LogTop,
        int LogLines);

    /// <summary>
    /// The most panel rows <see cref="Fit"/> will ever show for the given room, leaving
    /// <see cref="MinLogLines"/> lines plus every fixed margin clear beneath them.
    /// </summary>
    internal static int PanelCapacity(float roomBelowHeader)
    {
        var reserved = MoreLineReserve + GapAfterPanel + LogFooterMargin + (MinLogLines * LogLineHeight);
        var available = roomBelowHeader - reserved;

        return Math.Max(0, (int)(available / RowHeight));
    }

    /// <summary>
    /// Splits <paramref name="roomBelowHeader"/> between the initiative panel and the
    /// combat log.
    /// </summary>
    /// <remarks>
    /// <b>No combatant is ever hidden unless the list overflows <see
    /// cref="PanelCapacity"/></b>: below that count every row shows, exactly as before
    /// this change, and the log's own room still varies a little with the party's
    /// size (a two-combatant duel gets more log than a ten-combatant warband) — what
    /// changes is that the variation now stops at a floor instead of continuing forever
    /// as the list grows past what once fit.
    /// </remarks>
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
        int hiddenCount;

        if (combatantCount <= capacity)
        {
            firstIndex = 0;
            visibleCount = combatantCount;
            hiddenCount = 0;
        }
        else
        {
            var clampedActive = Math.Clamp(activeIndex, 0, combatantCount - 1);
            firstIndex = Math.Clamp(clampedActive, 0, combatantCount - capacity);
            visibleCount = capacity;
            hiddenCount = combatantCount - capacity;
        }

        // The "+N more" line's own room is reserved only when it is actually going to
        // draw (unlike ShopLayout.MoreLineReserve, which the stall reserves
        // unconditionally): a warband under capacity never pages, so it costs that
        // warband nothing — every fight this project fields today stays pixel-for-pixel
        // where it always was, and only a fight that actually pages pays the 16px this
        // line needs. PanelCapacity itself still budgets for it unconditionally, so the
        // floor in MinLogLines holds either way.
        var moreLineSpace = hiddenCount > 0 ? MoreLineReserve : 0f;
        var logTop = (visibleCount * RowHeight) + moreLineSpace + GapAfterPanel;
        var logRoom = roomBelowHeader - logTop - LogFooterMargin;
        var logLines = Math.Max(0, (int)(logRoom / LogLineHeight));

        return new Regions(firstIndex, visibleCount, hiddenCount, logTop, logLines);
    }
}
