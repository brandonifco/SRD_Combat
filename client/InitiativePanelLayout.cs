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
/// <b>Round 3 (qc's second review of PR #746): capacity is a constant, not a
/// room-dependent climb — because the climb itself cannot be made monotone.</b> Round
/// 2 picked the floor by a hard threshold on room (45 lines above 800px, 20 at or
/// below it) and derived capacity from that per-room floor; qc found the composition
/// was badly non-monotone (capacity 21 at room 800, 1 at room 801, not climbing back
/// to 21 until room 1210) and, worse, that the underlying construction — <i>any</i>
/// formula that lets capacity climb smoothly from a small value up toward a larger one
/// as room grows — cannot avoid a real dip in the log's own line count at the climb
/// itself. The proof: showing one more row always frees exactly <see
/// cref="RowHeight"/> (19px) of panel space, and a log line costs <see
/// cref="LogLineHeight"/> (17px) — 19 and 17 are close but not equal, so incrementing
/// the number of rows shown always costs the log <i>more room per row</i> (19px) than
/// one more log line needs (17px), meaning the log's own line count, computed fresh at
/// the new (larger) row count, is provably 1–2 lines below what it was a moment before
/// at the old (smaller) row count — which had been accumulating slack over every
/// intervening pixel of room growth. Delaying the climb does not help (the "before"
/// value only grows larger while it waits, widening the gap the "after" value has to
/// close); <see cref="InitiativePanelLayoutTests"/>' own remarks work the algebra.
/// </para>
/// <para>
/// <b>The fix: capacity never climbs inside any room this project's window can reach,
/// or that the monotonicity sweep checks.</b> <see cref="PanelCapacity"/> is the
/// constant <see cref="MinRows"/> (8 — what 1080p already showed at 14 combatants
/// under round 2) for every <c>roomBelowHeader</c> at or above <see
/// cref="ShrinkBelowRoom"/> (300) — comfortably below both the monotonicity sweep's
/// own start (400) and the smallest room any real window can produce (428, the
/// enforced 960×540 minimum, #704's own floor). Below that, the same degenerate
/// shrink this class has always had (down to <c>Math.Max(1, …)</c> in <see
/// cref="Fit"/>) still applies, targeting a floor of <see
/// cref="DegenerateShrinkFloor"/> (20) — the one place this class does not promise
/// monotonicity, because nothing reachable, and nothing tested, ever asks it to.
/// </para>
/// <para>
/// <b>1080p is unchanged: 8 rows, 45 lines at 14 combatants</b> — the same numbers
/// round 2 pinned, since <see cref="MinRows"/> was chosen to equal round 2's own
/// 1080p capacity exactly. <b>1280×720 changes from round 2's 11 rows to 8</b>: with a
/// single constant capacity shared by every reachable room, 720p's own 968px-shorter
/// room simply produces fewer log lines at the same row count (24, not round 2's 20 —
/// more, not fewer, since 720p no longer pages as early: capacity 11 there was never a
/// deliberate choice, only round 1's separate per-height floor constant working out to
/// that number incidentally). See <see cref="InitiativePanelLayoutTests.ExactTableForBothResolutions"/>
/// for the full, freshly measured 1–14 table at both heights.
/// </para>
/// <para>
/// <b>Capacity no longer grows past <see cref="MinRows"/> for arbitrarily large
/// windows either</b> — a deliberate consequence of the same fix, not a separate
/// choice: growing capacity for a taller window has the identical climb-cannot-be-
/// monotone problem as shrinking it for a shorter one. A window taller than 1080p
/// simply hands every extra pixel straight to the log instead of to more panel rows,
/// which the log's own uncapped `LogLines` already does — <see
/// cref="InitiativePanelLayoutTests.LogLinesGrowsWithoutBoundOnceCapacityIsFixed"/>
/// checks that a taller window keeps paying that pixel into the log, not into rows
/// nobody asked to see.
/// </para>
/// <para>
/// <b>The panel pages rather than compacting</b> ("should shrink" versus "should page"
/// being #305's own open design question, independent of the capacity constant
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
/// moment the active turn advanced past <see cref="MinRows"/> rows into the round.
/// <c>PanelFirstIndex</c> starts at <c>activeIndex</c> and slides back only far enough
/// to keep the window inside the list, so every reachable window still contains the
/// active row.
/// </para>
/// <para>
/// <b>The active row always shows, even where the log's own floor cannot</b>: a room
/// small enough that <see cref="PanelCapacity"/> computes 0 (below <see
/// cref="ShrinkBelowRoom"/>, unreachable by any real window) would otherwise show no
/// panel rows at all — including the one combatant this panel exists to always track
/// — while still (nonsensically) claiming a windowed view. <see cref="Fit"/> therefore
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
    /// The panel's row count for every reachable room (round 3, PR #746's second
    /// review) — what 1080p already showed at 14 combatants under round 2, kept
    /// unchanged rather than re-derived. See the type-level remarks for why this is now
    /// a constant rather than a per-room formula: the climb between a small capacity and
    /// this one cannot be made monotone, so it is pushed below <see
    /// cref="ShrinkBelowRoom"/> instead of attempted.
    /// </summary>
    internal const int MinRows = 8;

    /// <summary>
    /// The room, in px, below which <see cref="PanelCapacity"/> stops being the
    /// constant <see cref="MinRows"/> and shrinks instead (down to <c>Math.Max(1, …)</c>
    /// in <see cref="Fit"/>). Chosen comfortably below both the monotonicity sweep's own
    /// start (400, <c>InitiativePanelLayoutTests.CapacityAndLogLinesAreMonotoneInRoom</c>)
    /// and the smallest room any real window can produce (428 — the enforced 960×540
    /// minimum, <c>FightScreen._Ready</c>'s own floor, less the panel's header offset):
    /// no room this project can reach, and none the sweep checks, ever falls below this
    /// threshold, so the climb's own unavoidable non-monotonicity (see the type-level
    /// remarks) never needs to be measured, let alone promised.
    /// </summary>
    internal const float ShrinkBelowRoom = 300f;

    /// <summary>
    /// The degenerate shrink branch's own target line count, below <see
    /// cref="ShrinkBelowRoom"/> — unrelated to either named resolution now that both
    /// 1080p and 720p share <see cref="MinRows"/> (round 3). Kept at the same value
    /// round 1 picked for 720p, since nothing has ever asked it to be anything else.
    /// </summary>
    internal const int DegenerateShrinkFloor = 20;

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
    /// How many log lines fit below <see cref="LogTop"/> — uncapped: a room larger than
    /// any this project names keeps handing every extra pixel straight to the log,
    /// since <see cref="PanelCapacity"/> never grows past <see cref="MinRows"/> to
    /// claim any of it (see the type-level remarks).
    /// </param>
    internal readonly record struct Regions(
        int PanelFirstIndex,
        int PanelVisibleCount,
        int HiddenAboveCount,
        int HiddenBelowCount,
        float LogTop,
        int LogLines);

    /// <summary>
    /// The panel's row count for the given room — the constant <see cref="MinRows"/>
    /// at or above <see cref="ShrinkBelowRoom"/>, shrinking below it (see the
    /// type-level remarks for why this class does not attempt, or promise, a smooth
    /// climb between the two).
    /// </summary>
    internal static int PanelCapacity(float roomBelowHeader)
    {
        if (roomBelowHeader >= ShrinkBelowRoom)
        {
            return MinRows;
        }

        var reserved = GapAfterPanel + LogFooterMargin + (DegenerateShrinkFloor * LogLineHeight);
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
            // than a log that falls short of its own floor at a window this degenerate.
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
