namespace SRDCombat.Viewer;

/// <summary>
/// How many of the merchant's offers fit between the stall's header and its own Back
/// button, for a given window height and offer list — the arithmetic <c>DrawShop</c>
/// draws from rather than laying out a second time (#704).
/// </summary>
/// <remarks>
/// <para>
/// <b>A static, testable function in the <see cref="FightScreen.BarTop"/>/
/// <see cref="FightScreen.GroundLine"/> style</b> (<c>HitPointBarPlacementTests</c> is the
/// pattern): every offer's own text and price stay in <c>DrawShop</c>, which knows how to
/// paint a <c>ShopOffer</c>; this class knows nothing about a <c>ShopOffer</c> at all —
/// only how many effect lines each one prints, which is the one number its own height
/// depends on. That split is what makes it callable from a plain xUnit test with no
/// <c>Godot</c> object in sight.
/// </para>
/// <para>
/// <b>The stall had no floor at all before this</b> (#704): <c>DrawShop</c> laid every
/// offer into one column and let <c>y</c> run past <c>ScreenHeight</c> with nothing to
/// stop it, so the last few offers and the Back button itself went dark the moment the
/// stall carried enough of them — 25 offers already did it at 1920×1080. <see
/// cref="Fit"/> is what stops that: it never reports more rows than fit in the room it is
/// given, so a caller that only ever draws what it returns cannot push anything past the
/// bottom edge, regardless of how many offers the run has generated or how tall each
/// one's effect list happens to be.
/// </para>
/// </remarks>
internal static class ShopLayout
{
    /// <summary>
    /// The stall's fixed header, above the first offer row: the purse line and its own
    /// gap. <c>DrawShop</c>'s <c>y</c> starts at <c>UiTop + 8</c> and advances by 26 to
    /// clear that one line — this is that arithmetic, named once so a test can add it up
    /// the same way <c>DrawShop</c> does rather than copying the two literals separately.
    /// </summary>
    internal const float HeaderHeight = 34f;

    /// <summary>
    /// Where the purse line's own baseline sits, relative to the panel's top
    /// (<c>FightScreen.UiTop</c>). <c>DrawShop</c> reads this directly rather than the
    /// literal <c>8f</c> it used to — a test asserting the purse stays on screen is only
    /// evidence once it is checking the number the drawing call actually uses (Codex
    /// review round, #710).
    /// </summary>
    internal const float PurseLineTop = 8f;

    /// <summary>The height of the "nothing here would improve anybody" line, when there are no offers to page through at all.</summary>
    internal const float EmptyMessageHeight = 22f;

    /// <summary>The gap drawn after every offer row, on top of its own height.</summary>
    internal const float RowGap = 4f;

    /// <summary>The Back button's own height.</summary>
    internal const float BackButtonHeight = 32f;

    /// <summary>The gap <c>DrawShop</c> leaves between the last drawn content and the Back button.</summary>
    internal const float BackButtonGap = 12f;

    /// <summary>The small gap <c>DrawShop</c> leaves before the purchase notice line.</summary>
    internal const float NoticeGap = 6f;

    /// <summary>The purchase notice line's own height.</summary>
    internal const float NoticeLineHeight = 18f;

    /// <summary>
    /// Room reserved for the purchase notice line, whether or not one happens to be
    /// showing this frame — reserved unconditionally so scrolling never depends on
    /// whether the last click bought something or was refused.
    /// </summary>
    internal const float NoticeReserve = NoticeGap + NoticeLineHeight;

    /// <summary>
    /// Room reserved for the "N more…" line below the visible offers, whether or not the
    /// window is currently scrolled to the end — reserved unconditionally for the same
    /// reason <see cref="NoticeReserve"/> is: the offers drawn above it must never depend
    /// on whether that line happens to be needed.
    /// </summary>
    internal const float MoreLineReserve = 18f;

    /// <summary>
    /// Everything <see cref="Fit"/> must leave clear below the offers it shows: the "more"
    /// line, the notice line, the gap above the button, and the button itself.
    /// </summary>
    internal const float FooterReserve = MoreLineReserve + NoticeReserve + BackButtonGap + BackButtonHeight;

    /// <summary>An offer row's own height, before <see cref="RowGap"/> — 19px plus 15 per effect line.</summary>
    internal static float RowHeight(int effectLineCount) => 19f + (effectLineCount * 15f);

    /// <summary>
    /// One page of the offer list: which offers are drawn, whether any are left below the
    /// window, and how much vertical room they took — the number <c>DrawShop</c> advances
    /// its own running <c>y</c> by, so the Back button lands exactly where the visible
    /// offers end rather than where the whole list would have.
    /// </summary>
    /// <param name="FirstIndex">The first offer index this window starts from.</param>
    /// <param name="VisibleCount">How many offers, starting at <see cref="FirstIndex"/>, are drawn.</param>
    /// <param name="HasMore">Whether any offer past the window is hidden.</param>
    /// <param name="ContentHeight">The total height the visible rows occupy, gaps included.</param>
    internal readonly record struct Window(int FirstIndex, int VisibleCount, bool HasMore, float ContentHeight);

    /// <summary>
    /// Fits as many offers, starting at <paramref name="firstIndex"/>, as leave
    /// <see cref="FooterReserve"/> clear in <paramref name="roomBelowHeader"/> — the
    /// screen height remaining once the stall's own header (the purse line and the
    /// reserved "more above" line) has been drawn.
    /// </summary>
    /// <remarks>
    /// <b>No offer is ever shown if it alone would not fit</b> (unlike a softer "always
    /// show at least one" reading): a row that does not fit is left for the "N more…" line
    /// to name instead. That is what keeps the Back button's position bounded regardless
    /// of how tall a single offer's effect list is — a looser rule would still let one
    /// outsized offer push it off the bottom, the exact failure #704 is closing.
    /// </remarks>
    internal static Window Fit(float roomBelowHeader, IReadOnlyList<int> effectLineCounts, int firstIndex)
    {
        var available = Math.Max(roomBelowHeader - FooterReserve, 0f);
        var used = 0f;
        var count = 0;

        for (var i = firstIndex; i < effectLineCounts.Count; i++)
        {
            var height = RowHeight(effectLineCounts[i]) + RowGap;

            if (used + height > available)
            {
                break;
            }

            used += height;
            count++;
        }

        return new Window(firstIndex, count, firstIndex + count < effectLineCounts.Count, used);
    }

    /// <summary>
    /// Keeps a requested scroll offset inside the offer list — never negative, never past
    /// the last offer (an empty list clamps to zero, the only value there is room for).
    /// </summary>
    internal static int ClampOffset(int requestedOffset, int offerCount) =>
        Math.Clamp(requestedOffset, 0, Math.Max(0, offerCount - 1));

    /// <summary>
    /// The scroll wheel's and Page Up/Down's one shared arithmetic: moves
    /// <paramref name="storedOffset"/> by <paramref name="rows"/>, clamped to
    /// <paramref name="offerCount"/>.
    /// </summary>
    /// <remarks>
    /// <b>Normalises the stored offset before applying the delta</b> (Codex review
    /// round, #710) — a purchase can shrink the offer list out from under a scrolled-down
    /// stall, leaving <c>PlayFocus.Shop.Offset</c> pointing past the new end even though
    /// <c>DrawShop</c>'s own <see cref="ClampOffset"/> call already displays a clamped
    /// window. The naive <c>ClampOffset(storedOffset + rows, offerCount)</c> adds the
    /// delta to that stale, unclamped value first: a stored 22 against 21 remaining
    /// offers (already displaying from 20) plus Wheel Up's -1 computes
    /// <c>ClampOffset(21, 21) == 20</c> — the same offset as before, so the wheel looks
    /// dead. Clamping <paramref name="storedOffset"/> to what is actually on screen
    /// *first*, then applying the delta, moves relative to the displayed window instead
    /// of the stale stored one.
    /// </remarks>
    internal static int Scroll(int storedOffset, int rows, int offerCount) =>
        ClampOffset(ClampOffset(storedOffset, offerCount) + rows, offerCount);
}
