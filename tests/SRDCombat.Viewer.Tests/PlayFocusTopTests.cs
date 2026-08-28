using SRDCombat.Game;
using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// What <c>focus.Top</c> is for a stack shaped the way <c>PlayMode._Draw</c>'s single
/// traversal reads it (S5, #504 round 3): every card-bearing layer — the three row menus
/// and the outcome card — comes off one <c>foreach (var layer in _focus.BottomUp)</c>, and
/// a row-menu case fires only when <c>ReferenceEquals(layer, _focus.Top)</c>. These tests
/// pin the domain facts that guard depends on.
/// </summary>
/// <remarks>
/// <para>
/// Two earlier shapes were tried and rejected here. Round 1 introduced
/// <c>PlayFocus.TopOf</c>, a function that iterated <see cref="FocusStack{TFocus}.BottomUp"/>
/// to find "the last row menu, reset by anything else" — qc proved that computation is
/// exactly <c>focus.Top as PlayFocus.RowMenu</c> for every reachable stack, a loop that
/// cannot disagree with a plain cast. Round 2 made every guard read <c>_focus.Top</c>
/// directly but left the four card draws as four names written by hand in <c>_Draw</c> —
/// consistent, but still the third copy of the modal order the slice exists to kill. Round 3
/// is the one <c>foreach</c> above: <c>_Draw</c> no longer names which cards to draw, it
/// walks the stack and lets each layer's case answer for itself.
/// </para>
/// <para>
/// <c>PlayMode</c> cannot be constructed here — it derives from <c>Node2D</c> (#190's
/// finding) — so this drives <see cref="FocusStack{TFocus}"/> and <see cref="PlayFocus"/>
/// directly, the same types the traversal reads. The traversal itself is knockout-verified
/// against the probe instead (the PR body carries the capture diff): a version of the
/// SlotMenu case that compared against <c>_focus.BottomUp[0]</c> (the bottom) rather than
/// <c>_focus.Top</c> made <c>play-9-slot-menu.png</c> stop showing the slot list.
/// </para>
/// </remarks>
public class PlayFocusTopTests
{
    private static FocusStack<PlayFocus> Fresh() => new(new PlayFocus.Board());

    /// <summary>
    /// <c>BottomUp</c> for a three-deep stack reads root first, most recently pushed last.
    /// </summary>
    /// <remarks>
    /// Criterion 3 of #504 asks for this specifically; <c>FocusStackTests</c> already pins
    /// the same shape against a screen-agnostic double, so this proves nothing new beyond
    /// exercising the real <c>PlayFocus</c> types the draw guards use.
    /// </remarks>
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
    /// A layer pushed later is the one <c>Top</c> names — not the one pushed first, even
    /// though both are row menus and both are still on the stack.
    /// </summary>
    /// <remarks>
    /// This is <c>PlayMode.ChooseSpell</c>'s real shape: the spell menu is pushed, then the
    /// slot menu is pushed <em>over</em> it rather than replacing it, so Esc can hand the
    /// spell list back (#509). A guard that read the wrong end of the stack — first pushed
    /// rather than last — would make this pass with the spell menu instead.
    /// </remarks>
    [Fact]
    public void ALayerPushedLaterIsTheOneTopNames()
    {
        var spell = FightTestData.AnySpell();
        var focus = Fresh();
        focus.Push(new PlayFocus.SpellMenu());
        focus.Push(new PlayFocus.SlotMenu(spell));

        Assert.Equal(new PlayFocus.SlotMenu(spell), focus.Top);
        Assert.IsType<PlayFocus.SlotMenu>(focus.Top);
    }

    /// <summary>
    /// A non-menu layer above a menu means <c>Top</c> is not a row menu at all — the case
    /// that makes an armed action blank the menu that armed it, rather than leaving it
    /// showing underneath.
    /// </summary>
    [Fact]
    public void TargetingAboveAMenuMeansTopIsNotARowMenu()
    {
        var focus = Fresh();
        focus.Push(new PlayFocus.SpellMenu());
        focus.Push(new PlayFocus.Targeting(TargetKind.Spell, Spell: FightTestData.AnySpell()));

        Assert.Null(focus.Top as PlayFocus.RowMenu);
    }

    [Fact]
    public void ABareBoardHasNoRowMenuOnTop()
    {
        Assert.Null(Fresh().Top as PlayFocus.RowMenu);
    }

    [Fact]
    public void AnAttackMenuAloneIsTop()
    {
        var focus = Fresh();
        focus.Push(new PlayFocus.AttackMenu());

        Assert.Equal(new PlayFocus.AttackMenu(), focus.Top);
    }

    /// <summary>
    /// <c>Outcome</c> pushed over an open menu, without the menu being popped first,
    /// becomes <c>Top</c> — the exact shape <c>PlayMode.HandleFightEnd</c> produces (it
    /// pushes <c>Outcome</c> without calling <c>ClearPending</c> first) and the fact
    /// <c>DrawOutcomeCard</c>'s guard now relies on in place of
    /// <c>Holds&lt;Outcome&gt;</c>.
    /// </summary>
    /// <remarks>
    /// This is why the traversal's <c>Outcome</c> case carries no
    /// <c>ReferenceEquals(layer, _focus.Top)</c> guard the way the row-menu cases do: nothing
    /// is ever pushed above <c>Outcome</c> (qc's #504 review checked every <c>Push</c> site),
    /// so the traversal reaching this layer at all is already the whole answer — and it is
    /// also why <c>Outcome</c> and the row-menu trio could never share a
    /// <c>commanded is { } character</c> guard: <c>CommandedCombatant()</c> requires
    /// <c>_encounter is { IsComplete: false }</c>, so <c>commanded</c> is null in every
    /// single frame the outcome card exists.
    /// </remarks>
    [Fact]
    public void OutcomePushedOverAnOpenMenuBecomesTop()
    {
        var focus = Fresh();
        focus.Push(new PlayFocus.SpellMenu());
        focus.Push(new PlayFocus.Outcome());

        Assert.Equal(new PlayFocus.Outcome(), focus.Top);
        Assert.True(focus.Holds<PlayFocus.SpellMenu>());
        Assert.Null(focus.Top as PlayFocus.RowMenu);
    }
}
