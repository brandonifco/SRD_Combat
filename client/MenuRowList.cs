using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// The rows of whichever <see cref="PlayFocus.RowMenu"/> is open, each stamped with the
/// exact layer instance that filled it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The stale-list misattribution this closes (#505, qc review round).</b> Draw fills
/// this list; input reads it; and those two can disagree for exactly one input.
/// <c>PlayMode.ToggleMenu</c> swaps <c>_focus.Top</c> to a fresh menu immediately, but the
/// rows themselves stay whatever the <em>previous</em> menu last drew until the next
/// <c>_Draw</c> runs. Open the Attack menu, press the Cast hotkey, then press Enter before
/// the next frame: <c>_focus.Top</c> is already the new Spell menu, but this list still
/// held Attack rows — so an in-range index would have taken an attack while the player was
/// looking at (and the input model said they were on) the spell list.
/// </para>
/// <para>
/// Before #505 this could not happen even for one frame: the spell, attack and slot menus
/// each had their own typed row field, so a spell row could not physically hold an
/// attack's closure — the type system was the guard. Collapsing the three into one
/// untyped <c>Action</c> list threw that away without replacing it.
/// <see cref="CountFor"/> and <see cref="TryTake"/> are the replacement: every row is
/// stamped with the <see cref="PlayFocus.RowMenu"/> instance that was on top when it was
/// added, and both reads refuse unless the caller's <em>current</em> top layer is that same
/// instance, by reference. That turns "these happen to agree because of draw timing" into
/// an asserted invariant a stray input cannot slip past, rather than a coincidence one
/// could.
/// </para>
/// <para>
/// Not generic over a <c>TFocus</c> the way <see cref="Ui.FocusStack{TFocus}"/> is: this
/// type's whole reason to exist is one specific screen's bug shape, and <c>PlayMode</c> is
/// presently the only row-menu screen in this codebase. Genericising ahead of a second
/// caller would be the kind of guess #500's own remarks warn against.
/// </para>
/// </remarks>
internal sealed class MenuRowList
{
    private readonly List<(Rect2 Rect, Action Take)> _rows = [];
    private PlayFocus.RowMenu? _owner;

    /// <summary>The raw row count, ownership unchecked.</summary>
    /// <remarks>
    /// For a caller that already knows a fresh draw just ran this frame — the probe waits
    /// one frame after every click that opens or changes a menu before reading this, the
    /// same wait <c>HitTest</c>'s callers have always needed. Anything reading this
    /// <em>during</em> input handling wants <see cref="CountFor"/> instead, which is exactly
    /// the distinction this slice exists to draw.
    /// </remarks>
    internal int Count => _rows.Count;

    /// <summary>The rectangle of row <paramref name="index"/>. Ownership unchecked — see <see cref="Count"/>.</summary>
    internal Rect2 this[int index] => _rows[index].Rect;

    /// <summary>Empties the list and forgets who filled it.</summary>
    internal void Clear()
    {
        _rows.Clear();
        _owner = null;
    }

    /// <summary>
    /// Adds one row, stamped with <paramref name="owner"/> — the menu being drawn this
    /// call. A single <c>Draw*Menu</c> traversal must pass the same owner for every row it
    /// adds; every call site passes <c>_focus.Top</c> itself, once, cast to
    /// <see cref="PlayFocus.RowMenu"/> at the top of the method.
    /// </summary>
    internal void Add(PlayFocus.RowMenu owner, Rect2 rect, Action take)
    {
        _owner = owner;
        _rows.Add((rect, take));
    }

    /// <summary>
    /// How many rows are on offer for <paramref name="currentTop"/> — zero unless it is
    /// reference-equal to whoever last filled this list. A menu that has replaced the one
    /// drawn last frame reports no rows of its own until the next frame actually draws
    /// them, rather than borrowing the old menu's count.
    /// </summary>
    internal int CountFor(PlayFocus.RowMenu? currentTop) =>
        ReferenceEquals(_owner, currentTop) ? _rows.Count : 0;

    /// <summary>
    /// The index of the first row whose rectangle contains <paramref name="pixel"/>, or
    /// null.
    /// </summary>
    /// <remarks>
    /// Blind to ownership on purpose, the same way <c>PlayMode.HitTest</c> tests every rect
    /// unconditionally regardless of what is actually showing (#503) — <see cref="TryTake"/>
    /// is the one place ownership is asserted, for both the click path and the keyboard
    /// path, rather than this method silently filtering what it reports.
    /// </remarks>
    internal int? RowAt(Vector2 pixel)
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            if (_rows[index].Rect.HasPoint(pixel))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// Takes row <paramref name="index"/> — but only when <paramref name="currentTop"/> is
    /// the exact instance that filled this list. Returns whether a row was taken, so a
    /// stale or out-of-range index is a silent no-op rather than running the wrong menu's
    /// action.
    /// </summary>
    internal bool TryTake(int index, PlayFocus.RowMenu? currentTop)
    {
        if (!ReferenceEquals(_owner, currentTop) || index < 0 || index >= _rows.Count)
        {
            return false;
        }

        _rows[index].Take();
        return true;
    }
}
