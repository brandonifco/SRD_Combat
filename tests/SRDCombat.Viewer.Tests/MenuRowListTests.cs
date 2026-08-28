using Godot;
using SRDCombat.Viewer;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The stale-list misattribution qc's review of #505 found: draw fills <c>MenuRowList</c>,
/// input reads it, and those two can disagree for exactly one input.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window, reproduced directly.</b> <c>PlayMode.ToggleMenu</c> swaps
/// <c>_focus.Top</c> to a fresh menu immediately, but the rows themselves stay whatever the
/// <em>previous</em> menu last drew until the next <c>_Draw</c> runs and repopulates them.
/// Before #505's three typed lists (<c>_spellRows</c>/<c>_attackRows</c>/<c>_slotRows</c>)
/// collapsed into this one untyped-<c>Action</c> list, a spell row could not physically
/// hold an attack's closure — the type system was the guard. These tests are the
/// replacement guard, exercised directly rather than through <c>PlayMode</c> (a
/// <c>Node2D</c>, unconstructable in a test host — see <c>PlayFocusTopTests</c>'s remarks
/// for the precedent).
/// </para>
/// <para>
/// <b>Knockout, performed and reverted:</b> removing the <c>ReferenceEquals</c> check from
/// <c>TryTake</c> (trusting the index alone, the pre-fix shape) made
/// <see cref="ARowTakenAfterTheOwningMenuChangesWithoutARedrawIsRefused"/> fail — the stale
/// row's action ran, flipping <c>taken</c> to <c>"attack"</c> where the test asserts it
/// stays empty. Removing the same check from <c>CountFor</c> made
/// <see cref="TheRowCountForANewlyFocusedMenuIsZeroUntilItIsActuallyDrawn"/> fail with
/// <c>Expected: 0, Actual: 1</c>.
/// </para>
/// </remarks>
public class MenuRowListTests
{
    [Fact]
    public void ARowTakenAfterTheOwningMenuChangesWithoutARedrawIsRefused()
    {
        var rows = new MenuRowList();
        var attackMenu = new PlayFocus.AttackMenu();
        var spellMenu = new PlayFocus.SpellMenu();

        // The Attack menu is drawn: its row closes over ChooseAttack's shape.
        var taken = string.Empty;
        rows.Add(attackMenu, new Rect2(0, 0, 10, 10), () => taken = "attack");

        // ToggleMenu's whole job: the focus swaps to a fresh Spell menu immediately.
        // Nothing has redrawn yet, so `rows` still belongs to attackMenu — this is the
        // window. Taking row 0 while the caller believes the Spell menu is on top must not
        // run the Attack menu's action.
        Assert.False(rows.TryTake(0, spellMenu));
        Assert.Equal(string.Empty, taken);

        // The row is not lost — it is still there for whoever actually owns it.
        Assert.True(rows.TryTake(0, attackMenu));
        Assert.Equal("attack", taken);
    }

    [Fact]
    public void TheRowCountForANewlyFocusedMenuIsZeroUntilItIsActuallyDrawn()
    {
        var rows = new MenuRowList();
        var attackMenu = new PlayFocus.AttackMenu();
        var spellMenu = new PlayFocus.SpellMenu();

        rows.Add(attackMenu, new Rect2(0, 0, 10, 10), () => { });

        Assert.Equal(1, rows.CountFor(attackMenu));
        Assert.Equal(0, rows.CountFor(spellMenu));
    }

    [Fact]
    public void ClearForgetsBothTheRowsAndWhoFilledThem()
    {
        var rows = new MenuRowList();
        var attackMenu = new PlayFocus.AttackMenu();

        rows.Add(attackMenu, new Rect2(0, 0, 10, 10), () => { });
        rows.Clear();

        Assert.Equal(0, rows.CountFor(attackMenu));
        Assert.False(rows.TryTake(0, attackMenu));
    }

    [Fact]
    public void TheOwningMenuCanStillTakeEveryRowItFilled()
    {
        var rows = new MenuRowList();
        var menu = new PlayFocus.SpellMenu();
        var takenIndex = -1;

        rows.Add(menu, new Rect2(0, 0, 10, 20), () => takenIndex = 0);
        rows.Add(menu, new Rect2(0, 20, 10, 20), () => takenIndex = 1);

        Assert.Equal(2, rows.CountFor(menu));
        Assert.True(rows.TryTake(1, menu));
        Assert.Equal(1, takenIndex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void TryTakeRefusesAnOutOfRangeIndexEvenForTheOwner(int index)
    {
        var rows = new MenuRowList();
        var menu = new PlayFocus.AttackMenu();
        rows.Add(menu, new Rect2(0, 0, 10, 10), () => { });

        Assert.False(rows.TryTake(index, menu));
    }

    /// <summary>
    /// <see cref="MenuRowList.RowAt"/> reports a hit regardless of ownership — the same
    /// "test every rect unconditionally" shape <c>PlayMode.HitTest</c> uses for buttons and
    /// shop rows (#503). <see cref="MenuRowList.TryTake"/> is the one place ownership is
    /// asserted, not this lookup.
    /// </summary>
    [Fact]
    public void RowAtFindsAPixelHitRegardlessOfWhoOwnsTheList()
    {
        var rows = new MenuRowList();
        var attackMenu = new PlayFocus.AttackMenu();
        rows.Add(attackMenu, new Rect2(0, 0, 10, 10), () => { });

        // No SpellMenu was ever passed to RowAt — it takes no owner parameter at all.
        Assert.Equal(0, rows.RowAt(new Vector2(5, 5)));
        Assert.Null(rows.RowAt(new Vector2(50, 50)));
    }
}
