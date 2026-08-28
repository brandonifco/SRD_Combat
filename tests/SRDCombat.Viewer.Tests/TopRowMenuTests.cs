using SRDCombat.Game;
using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="PlayFocus.TopOf"/>: the one computation <c>PlayMode</c>'s
/// <c>DrawSpellMenu</c>, <c>DrawAttackMenu</c> and <c>DrawSlotMenu</c> now share instead of
/// each asking <c>_focus.Top</c> independently (S5, #504's third acceptance criterion).
/// </summary>
/// <remarks>
/// <c>PlayMode</c> cannot be constructed here — it derives from <c>Node2D</c> (#190's
/// finding) — so this drives the extracted, Godot-free function directly rather than the
/// screen. That function is what <c>PlayMode.TopRowMenu</c> forwards to, so a test here is
/// a test of what actually draws.
/// </remarks>
public class TopRowMenuTests
{
    private static FocusStack<PlayFocus> Fresh() => new(new PlayFocus.Board());

    /// <summary>
    /// <c>BottomUp</c> for a three-deep stack reads root first, most recently pushed last —
    /// the order <see cref="PlayFocus.TopOf"/> walks to decide what is on screen.
    /// </summary>
    [Fact]
    public void BottomUpOrdersAThreeDeepStackFromTheRootUp()
    {
        // The same SpellDefinition instance in both the pushed layer and the expected
        // one: SpellDefinition carries array-typed fields, whose record equality is by
        // reference, so two separately-built "any spell" values would never compare
        // equal — the point being tested here is stack order, not spell identity.
        var spell = FightTestData.AnySpell();
        var focus = Fresh();
        focus.Push(new PlayFocus.SpellMenu());
        focus.Push(new PlayFocus.SlotMenu(spell));

        Assert.Equal(
            [new PlayFocus.Board(), new PlayFocus.SpellMenu(), (PlayFocus)new PlayFocus.SlotMenu(spell)],
            focus.BottomUp);
    }

    /// <summary>
    /// A layer pushed later is the one that draws — not the one pushed first, even though
    /// both are row menus and both are still on the stack.
    /// </summary>
    /// <remarks>
    /// This is <see cref="ChooseSpellLeavesTheSpellMenuOnTheStackUnderTheSlotMenu"/>'s real
    /// shape from <c>PlayMode.ChooseSpell</c>: the spell menu is pushed, then the slot menu
    /// is pushed <em>over</em> it rather than replacing it, so Esc can hand the spell list
    /// back (#509). Reading <c>TopOf</c> off the wrong end of the list — first pushed rather
    /// than last — would make this pass with the spell menu instead, which is the exact
    /// failure a reordered walk produces.
    /// </remarks>
    [Fact]
    public void ALayerPushedLaterIsTheOneThatDraws()
    {
        var spell = FightTestData.AnySpell();
        var focus = Fresh();
        focus.Push(new PlayFocus.SpellMenu());
        focus.Push(new PlayFocus.SlotMenu(spell));

        Assert.Equal(new PlayFocus.SlotMenu(spell), PlayFocus.TopOf(focus));
        Assert.IsType<PlayFocus.SlotMenu>(PlayFocus.TopOf(focus));
    }

    [Fact]
    public void ChooseSpellLeavesTheSpellMenuOnTheStackUnderTheSlotMenu()
    {
        var focus = Fresh();
        focus.Push(new PlayFocus.SpellMenu());
        focus.Push(new PlayFocus.SlotMenu(FightTestData.AnySpell()));

        Assert.True(focus.Holds<PlayFocus.SpellMenu>());
        Assert.Equal(3, focus.Depth);
    }

    /// <summary>
    /// A non-menu layer above a menu hides it — the case that makes an armed action blank
    /// the menu that armed it, rather than leaving it showing underneath.
    /// </summary>
    [Fact]
    public void TargetingAboveAMenuHidesTheMenu()
    {
        var focus = Fresh();
        focus.Push(new PlayFocus.SpellMenu());
        focus.Push(new PlayFocus.Targeting(TargetKind.Spell, Spell: FightTestData.AnySpell()));

        Assert.Null(PlayFocus.TopOf(focus));
    }

    [Fact]
    public void ABareBoardHasNoRowMenuToDraw()
    {
        Assert.Null(PlayFocus.TopOf(Fresh()));
    }

    [Fact]
    public void AnAttackMenuAloneIsWhatDraws()
    {
        var focus = Fresh();
        focus.Push(new PlayFocus.AttackMenu());

        Assert.Equal(new PlayFocus.AttackMenu(), PlayFocus.TopOf(focus));
    }
}
