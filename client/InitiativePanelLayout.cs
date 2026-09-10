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
/// the log's starting position with nothing capping it. Past <see
/// cref="PanelCapacity"/> the panel now pages instead of growing further, so the log's
/// own room stops shrinking there rather than continuing indefinitely.
/// </para>
/// <para>
/// <b>Where this actually changes anything, and where it does not</b> (qc's review of
/// PR #746): 1920×1080 is the only resolution this client ever runs at
/// (<c>project.godot</c>'s <c>window/size/mode=3</c> opens fullscreen, and there is no
/// <c>window/stretch</c> entry to make a smaller logical viewport reachable — see the
/// <c>Fit</c> remarks below on why a live capture cannot currently show the 1280×720
/// case). <see cref="PanelCapacity"/> there is 29, so for every combatant count this
/// project fields today (a party of four plus a warband of up to ten, 14 total) the
/// panel never pages and <see cref="Fit"/> returns the *same* log room this fight
/// already had on `main` — the floor in <see cref="MinLogLines"/> does not bind at the
/// shipping resolution for any fight this project currently generates. At 1280×720
/// (reachable only by resizing a windowed build, or by a future capability that runs
/// the client at that size) capacity is 10, so paging starts at 11 combatants — the
/// same count the log's own room was already down to 20 lines under the old, uncapped
/// arithmetic, so paging there buys at most one additional line at the threshold and
/// nothing per additional combatant after that until the floor's own headroom is used
/// up. **Whether that trade — and whether 1080p's own inertness — is the right call is
/// with Brandon**; the constants below (<see cref="MinLogLines"/>,
/// <see cref="PanelCapacity"/>'s formula) are deliberately left as measured rather than
/// retuned pending that answer.
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
/// the active row — see the next paragraph for the one further guarantee this needs at
/// a degenerate window size.
/// </para>
/// <para>
/// <b>The active row always shows, even where the log's own floor cannot</b> (PR #746
/// review): a room small enough that <see cref="PanelCapacity"/> computes 0 would
/// otherwise show no panel rows at all — including the one combatant this panel exists
/// to always track — while still (nonsensically) claiming a windowed view. <see
/// cref="Fit"/> therefore never shows fewer than one row once the list overflows
/// (<c>Math.Max(1, capacity)</c>): the log's own <see cref="MinLogLines"/> promise is
/// the one that yields at that extreme, not the active row's visibility.
/// </para>
/// <para>
/// <b>Hidden combatants are reported on whichever side they are actually hidden</b>
/// (PR #746 review): a window that starts at <c>activeIndex</c> can leave combatants
/// hidden *above* it (everyone earlier in turn order this round), *below* it
/// (everyone later), or both at once — <see cref="Regions.HiddenAboveCount"/> and
/// <see cref="Regions.HiddenBelowCount"/> are reported separately rather than as one
/// combined count, because a single "+N more" line drawn below the rows would say
/// "more" even when every hidden combatant is actually earlier in the order, already
/// acted, and not merely undrawn. <c>FightScreen.DrawTurnOrder</c> folds the "above"
/// count into the existing "INITIATIVE" header (space that already exists regardless
/// of any count, so it costs the log nothing new) and reserves a line below the rows
/// only when <see cref="Regions.HiddenBelowCount"/> is actually positive — <see
/// cref="PanelCapacity"/>'s own reserved budget therefore still only ever needs to
/// account for one such line, not two.
/// </para>
/// <para>
/// <b>None of these hidden rows are reachable by any input.</b> A click anywhere on
/// the panel's own column is chrome, not a square
/// (<c>FightScreen.PlayMode.OverOverlay</c>: <c>pixel.X >= PanelLeft - 16</c>), so it
/// backs out of anything armed rather than acting on a hidden row; the mouse wheel
/// during a fight is the camera's zoom, not a panel scroll (wheel scrolling exists only
/// for the merchant's stall, <c>HandleShopWheelInput</c>). So unlike
/// <see cref="ShopLayout"/>'s own offer list — which the wheel and Page Up/Down do
/// scroll — this panel borrows only <c>Fit</c>'s windowing shape from that pattern, not
/// its scrolling half: a hidden row is genuinely off-screen for the whole turn it stays
/// hidden, recoverable only by watching the active window slide as turns pass.
/// </para>
/// <para>
/// <b><see cref="MinLogLines"/> is a floor the seam is built to guarantee, not
/// evidence that any fight today needs it</b> — see the resolution note above for
/// which fights actually reach it.
/// </para>
/// </remarks>
internal static class InitiativePanelLayout
{
    /// <summary>An initiative row's own height — unchanged from <c>DrawTurnOrder</c>'s existing <c>y += 19</c>.</summary>
    internal const float RowHeight = 19f;

    /// <summary>
    /// Room for the "N more below" line under the visible panel rows, reserved only
    /// when <see cref="Regions.HiddenBelowCount"/> would be positive — unlike <see
    /// cref="ShopLayout.MoreLineReserve"/>, which the stall reserves unconditionally.
    /// A fight under <see cref="PanelCapacity"/> never pages, so it pays nothing for a
    /// line it never draws. Combatants hidden *above* the window cost nothing here at
    /// all — they are named in the existing "INITIATIVE" header instead, which needs no
    /// new reserve — so this is still the only line <see cref="PanelCapacity"/> budgets
    /// for.
    /// </summary>
    internal const float MoreLineReserve = 16f;

    /// <summary>
    /// The y-offset below the panel's visible rows where the "N more below" line's own
    /// baseline sits (<c>FightScreen.DrawTurnOrder</c>'s <c>y + MoreLineBaselineOffset</c>) —
    /// named so a test reads the same value the drawing call does rather than a copied
    /// literal (Codex review round, #710's own precedent).
    /// </summary>
    internal const float MoreLineBaselineOffset = 12f;

    /// <summary>
    /// A conservative estimate of the "N more below" line's own drawn height — it draws
    /// at <c>fontSize: 11</c>, and this is that same size, used only to prove the line's
    /// drawn rect does not run into the log's own "COMBAT LOG" label (see
    /// <c>InitiativePanelLayoutTests.MoreLineNeverCollidesWithTheLogLabel</c>) rather
    /// than to lay anything out.
    /// </summary>
    internal const float MoreLineTextHeight = 11f;

    /// <summary>
    /// The gap between the panel's rows (or the reserved "N more below" line beneath
    /// them) and the combat log's first line — holds the "COMBAT LOG" label, which
    /// draws <see cref="LogLabelBaselineOffset"/> above the first line. Matches the
    /// literal <c>26</c> <c>DrawLog</c> used to add directly onto <c>tokenCount * 19</c>
    /// before this change.
    /// </summary>
    internal const float GapAfterPanel = 26f;

    /// <summary>
    /// How far above the log's first content line the "COMBAT LOG" label's own baseline
    /// sits (<c>DrawLog</c>'s <c>top - LogLabelBaselineOffset</c>) — named for the same
    /// reason <see cref="MoreLineBaselineOffset"/> is.
    /// </summary>
    internal const float LogLabelBaselineOffset = 12f;

    /// <summary>A log line's own height — unchanged from <c>DrawLog</c>'s existing <c>y += 17</c>.</summary>
    internal const float LogLineHeight = 17f;

    /// <summary>The gap <c>DrawLog</c> leaves below its last line and the window's own bottom edge.</summary>
    internal const float LogFooterMargin = 20f;

    /// <summary>
    /// The log's guaranteed floor, in lines, at every combatant count and both named
    /// resolutions (1920×1080, 1280×720) — the number <see cref="PanelCapacity"/> is
    /// solved backwards from. See the type-level remarks for which of those two
    /// resolutions any fight today actually reaches.
    /// </summary>
    internal const int MinLogLines = 20;

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
    /// 0 when the window reaches the list's end. Reported in the reserved "N more
    /// below" line, drawn only when this is positive.
    /// </param>
    /// <param name="LogTop">
    /// Where the combat log's own content starts, measured from the same origin
    /// <c>roomBelowHeader</c> in <see cref="Fit"/> was measured from (the panel's first
    /// row — <c>UiTop + 16</c> in <c>FightScreen</c>).
    /// </param>
    /// <param name="LogLines">
    /// How many log lines fit below <see cref="LogTop"/> — never less than
    /// <see cref="MinLogLines"/> for any window large enough for at least one panel row
    /// through <see cref="PanelCapacity"/> to hold that promise; at a window smaller
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
    /// leaving <see cref="MinLogLines"/> lines plus every fixed margin clear beneath
    /// them — not a floor on how few rows ever show (see <see cref="Fit"/>'s own
    /// <c>Math.Max(1, …)</c> for that).
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
            // than a log that falls short of MinLogLines at a window this degenerate.
            visibleCount = Math.Max(1, capacity);

            var clampedActive = Math.Clamp(activeIndex, 0, combatantCount - 1);
            firstIndex = Math.Clamp(clampedActive, 0, combatantCount - visibleCount);
            hiddenAbove = firstIndex;
            hiddenBelow = combatantCount - (firstIndex + visibleCount);
        }

        // The "N more below" line's own room is reserved only when it is actually
        // going to draw. Combatants hidden above cost nothing here — see the
        // type-level remarks on why that count is reported in the header instead.
        var moreLineSpace = hiddenBelow > 0 ? MoreLineReserve : 0f;
        var logTop = (visibleCount * RowHeight) + moreLineSpace + GapAfterPanel;
        var logRoom = roomBelowHeader - logTop - LogFooterMargin;
        var logLines = Math.Max(0, (int)(logRoom / LogLineHeight));

        return new Regions(firstIndex, visibleCount, hiddenAbove, hiddenBelow, logTop, logLines);
    }
}
