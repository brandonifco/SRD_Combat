using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The highlighted row now lives on the menu layer itself, not in a field
/// <c>PlayMode</c> had to remember to zero every time it opened a menu (#505).
/// </summary>
/// <remarks>
/// <para>
/// Before this slice, <c>PlayMode</c> held one <c>_menuIndex</c> field shared by all
/// three row menus, reset by three separate assignments scattered through
/// <c>Invoke</c> and <c>ChooseSpell</c> — a silent-reset class: a fourth call site
/// opening a menu, or an edit that removed one of the three, would leave the highlight
/// wherever the previous menu left it. Moving <see cref="PlayFocus.RowMenu.MenuIndex"/>
/// onto the layer means a freshly constructed menu starts at row zero because that is
/// the property's default, not because anything remembers to set it.
/// </para>
/// <para>
/// <b>Two knockouts, performed and reverted (#416).</b> First, the three
/// <c>_menuIndex = 0</c> assignments this slice deletes from <c>PlayMode.Invoke</c> and
/// <c>PlayMode.ChooseSpell</c> stayed deleted while every test here passed — the reset no
/// longer depends on them, so removing them changed nothing. Second, and the sharper
/// proof: <c>RowMenu.MenuIndex</c> was temporarily backed by a <c>static</c> field shared
/// by every instance, reproducing the pre-refactor shape at the type level. That made
/// <see cref="ANewlyPushedMenuStartsWithTheHighlightAtRowZero"/> (all three menus) and
/// <see cref="ARePushedMenuStartsBackAtRowZeroEvenAfterTheHighlightMoved"/> fail with
/// <c>Assert.Equal() Failure: Expected: 0, Actual: 2</c> — proof these tests are pinned to
/// construction giving a fresh zero, not merely to the field never having been touched in
/// this particular run.
/// </para>
/// <para>
/// <b>A third knockout covers criterion 4:</b> removing <c>Math.Clamp</c> from
/// <see cref="PlayFocus.RowMenu.MoveHighlight"/> (leaving a bare <c>MenuIndex += step</c>)
/// failed <see cref="MovingUpFromTheTopRowHoldsAtRowZero"/> (<c>Actual: -1</c>),
/// <see cref="MovingFarPastTheTopRowClampsAtRowZeroRatherThanGoingNegative"/>
/// (<c>Actual: -98</c>) and <see cref="MovingPastTheLastRowClampsAtTheLastRow"/>
/// (<c>Actual: 100</c>).
/// </para>
/// </remarks>
public class RowMenuHighlightTests
{
    // Travels by name rather than by focus, same workaround PlayFocusTests uses: PlayFocus
    // is internal, and a public theory method may not name it in its signature.
    private static readonly Dictionary<string, Func<PlayFocus.RowMenu>> Fresh = new(StringComparer.Ordinal)
    {
        ["AttackMenu"] = () => new PlayFocus.AttackMenu(),
        ["SpellMenu"] = () => new PlayFocus.SpellMenu(),
        ["SlotMenu"] = () => new PlayFocus.SlotMenu(FightTestData.AnySpell()),
    };

    public static TheoryData<string> RowMenuNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var name in Fresh.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RowMenuNames))]
    public void ANewlyPushedMenuStartsWithTheHighlightAtRowZero(string name)
    {
        var focus = new FocusStack<PlayFocus>(new PlayFocus.Board());

        focus.Push(Fresh[name]());

        Assert.Equal(0, ((PlayFocus.RowMenu)focus.Top).MenuIndex);
    }

    /// <summary>
    /// The silent-reset class this slice closes, reproduced directly: move the
    /// highlight off zero, close the menu, open a fresh one of the same kind, and
    /// confirm it is not carrying the old menu's position forward.
    /// </summary>
    [Fact]
    public void ARePushedMenuStartsBackAtRowZeroEvenAfterTheHighlightMoved()
    {
        var focus = new FocusStack<PlayFocus>(new PlayFocus.Board());
        focus.Push(new PlayFocus.AttackMenu());

        ((PlayFocus.RowMenu)focus.Top).MoveHighlight(step: 2, rowCount: 5);
        Assert.Equal(2, ((PlayFocus.RowMenu)focus.Top).MenuIndex);

        focus.Pop();
        focus.Push(new PlayFocus.AttackMenu());

        Assert.Equal(0, ((PlayFocus.RowMenu)focus.Top).MenuIndex);
    }

    /// <summary>
    /// Moving past the first row holds at zero, matching today's
    /// <c>Math.Clamp(_menuIndex + scroll.Y, 0, rows - 1)</c>.
    /// </summary>
    [Fact]
    public void MovingUpFromTheTopRowHoldsAtRowZero()
    {
        var menu = new PlayFocus.AttackMenu();

        menu.MoveHighlight(step: -1, rowCount: 4);

        Assert.Equal(0, menu.MenuIndex);
    }

    /// <summary>
    /// A large upward jump from the middle of the list still stops at zero rather than
    /// going negative.
    /// </summary>
    [Fact]
    public void MovingFarPastTheTopRowClampsAtRowZeroRatherThanGoingNegative()
    {
        var menu = new PlayFocus.AttackMenu();
        menu.MoveHighlight(step: 2, rowCount: 5);

        menu.MoveHighlight(step: -100, rowCount: 5);

        Assert.Equal(0, menu.MenuIndex);
    }

    /// <summary>
    /// Moving past the last row holds at the last row rather than running off the end
    /// of the list the click and Enter paths both index into.
    /// </summary>
    [Fact]
    public void MovingPastTheLastRowClampsAtTheLastRow()
    {
        var menu = new PlayFocus.AttackMenu();

        menu.MoveHighlight(step: 100, rowCount: 3);

        Assert.Equal(2, menu.MenuIndex);
    }
}
