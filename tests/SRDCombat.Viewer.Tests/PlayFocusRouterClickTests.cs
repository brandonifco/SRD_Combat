using SRDCombat.Core.Combat;
using SRDCombat.Game;
using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// What each click means, given what has the player's attention (#503, S4).
/// </summary>
/// <remarks>
/// <para>
/// One test per step of the nine-step priority order <c>HandleClick</c> used to encode by
/// hand — a second, independent copy of the keyboard's own order, in a different
/// vocabulary. Nothing made the two agree; they agreed only because people read the file
/// carefully. These tests are what holds the click order now that it lives in
/// <see cref="PlayFocusRouter.RouteClick"/> beside it.
/// </para>
/// <para>
/// <b><see cref="ClickHit"/> carries independent facts, not one preselected classification</b>
/// (qc review round 1). The first version of this file used a single <c>HitKind</c> that
/// <c>PlayMode.HitTest</c> had already picked, which meant a menu row silently outranked a
/// button before <see cref="PlayFocusRouter.RouteClick"/> ever ran — reordering the router's
/// own step 5 and step 6 changed nothing, because <c>HitTest</c>'s loop order had already
/// thrown the losing fact away. <see cref="Hit"/> below builds a <see cref="ClickHit"/> with
/// every field it is given set at once, precisely so a test can hand the router two facts
/// that could both be true and watch it choose.
/// </para>
/// <para>
/// The same round moved the interlude/shop/shop-availability guards from
/// <c>PlayMode.HitTest</c> into the router: <c>HitTest</c> now tests every rect it knows
/// about unconditionally, and <see cref="PlayFocusRouter.RouteClick"/> is the only place
/// that decides whether a fact it receives is even in play. The tests under "the guards,
/// pinned on the router side" exist because the first version's
/// <c>Step1OutranksTheNoCommandedCombatantGate</c> passed a <i>fighting</i> context and
/// still expected a shop action — which proved nothing about the old
/// <c>Phase.Interlude</c> check, since nothing on the router side was reading it yet.
/// </para>
/// </remarks>
public class PlayFocusRouterClickTests
{
    private static FocusStack<PlayFocus> Board() => new(new PlayFocus.Board());

    private static FocusStack<PlayFocus> With(PlayFocus layer)
    {
        var stack = Board();
        stack.Push(layer);
        return stack;
    }

    /// <summary>A fight in progress, nobody's animation running, someone under command.</summary>
    private static RouteContext Ready(
        bool hasCommanded = true, bool actInProgress = false, bool interlude = false, bool shopAvailable = false) =>
        new(Fighting: !interlude, ActInProgress: actInProgress, MenuRowCount: 0, CanArmAttack: true,
            HasCommanded: hasCommanded, HasCursor: true, Interlude: interlude, ShopAvailable: shopAvailable);

    /// <summary>
    /// Builds a <see cref="ClickHit"/> with exactly the facts named — every other field
    /// false/null, as <c>PlayMode.HitTest</c> would report for a pixel that missed
    /// everything else. Named so a test that hands the router two facts at once (a menu row
    /// <i>and</i> a button, say) reads as a deliberate scenario rather than a typo.
    /// </summary>
    private static ClickHit Hit(
        bool shopBack = false,
        int? shopRow = null,
        bool shopOpen = false,
        bool continueHit = false,
        int? menuRow = null,
        int? button = null,
        GridPosition? square = null,
        bool overOverlay = false) =>
        new(shopBack, shopRow, shopOpen, continueHit, menuRow, button, square, overOverlay);

    private static Route Route(FocusStack<PlayFocus> focus, ClickHit hit, RouteContext context) =>
        PlayFocusRouter.RouteClick(focus, hit, context);

    // ---- step 1: the interlude screen -------------------------------------------------

    [Fact]
    public void Step1ShopBackButtonClosesTheStall()
    {
        var hit = Hit(shopBack: true);
        var context = Ready(interlude: true);

        Assert.Equal(RouteAction.CloseTopLayer, Route(With(new PlayFocus.Shop()), hit, context).Action);
    }

    [Fact]
    public void Step1ShopRowBuysTheOfferAtItsIndex()
    {
        var hit = Hit(shopRow: 2);
        var context = Ready(interlude: true);

        var route = Route(With(new PlayFocus.Shop()), hit, context);

        Assert.Equal(RouteAction.PurchaseShopRow, route.Action);
        Assert.Equal(2, route.Index);
    }

    [Fact]
    public void Step1ShopOpenButtonOpensTheStall()
    {
        var hit = Hit(shopOpen: true);
        var context = Ready(interlude: true, shopAvailable: true);

        Assert.Equal(RouteAction.OpenShop, Route(Board(), hit, context).Action);
    }

    [Fact]
    public void Step1ContinueButtonStartsTheNextFight()
    {
        var hit = Hit(continueHit: true);
        var context = Ready(interlude: true);

        Assert.Equal(RouteAction.ContinueFight, Route(Board(), hit, context).Action);
    }

    [Fact]
    public void Step1NothingHitDuringTheInterludeIsIgnored()
    {
        var context = Ready(interlude: true, hasCommanded: false);

        Assert.Equal(RouteAction.Ignore, Route(Board(), Hit(), context).Action);
    }

    /// <summary>
    /// The interlude kinds are checked before anything else, exactly like
    /// <c>HandleClick</c>'s own phase branch: there is no commanded combatant between
    /// fights, and the click still resolves.
    /// </summary>
    [Fact]
    public void Step1OutranksTheNoCommandedCombatantGate()
    {
        var hit = Hit(continueHit: true);
        var context = Ready(interlude: true, hasCommanded: false);

        Assert.Equal(RouteAction.ContinueFight, Route(Board(), hit, context).Action);
    }

    // ---- the guards, pinned on the router side (qc review round 1) --------------------

    /// <summary>
    /// <c>PlayMode.HitTest</c> reports <see cref="ClickHit.Continue"/> unconditionally now
    /// — the phase check that used to keep it interlude-only lives here instead. A stray
    /// true fact outside the screen it belongs to must not be honoured.
    /// </summary>
    [Fact]
    public void Step1RequiresContextInterludeRatherThanTrustingTheHitFacts()
    {
        var hit = Hit(continueHit: true);
        var context = Ready(interlude: false, hasCommanded: false);

        var route = Route(Board(), hit, context);

        Assert.NotEqual(RouteAction.ContinueFight, route.Action);
        Assert.Equal(RouteAction.Ignore, route.Action);
    }

    /// <summary>
    /// The back button and the stall's rows count only while the shop is actually the top
    /// of the stack — <c>focus.Top is PlayFocus.Shop</c>, read by the router, not a flag
    /// <c>HitTest</c> pre-filtered on.
    /// </summary>
    [Fact]
    public void Step1ShopBackAndRowsCountOnlyWhileTheShopIsOnTopOfTheStack()
    {
        var hit = Hit(shopBack: true);
        var context = Ready(interlude: true);

        var route = Route(Board(), hit, context);

        Assert.NotEqual(RouteAction.CloseTopLayer, route.Action);
        Assert.Equal(RouteAction.Ignore, route.Action);
    }

    /// <summary>
    /// The open-stall button needs <see cref="RouteContext.ShopAvailable"/> as well as the
    /// pixel — the old <c>_shopAvailable &amp;&amp;</c> term, now on the router's side of
    /// the seam.
    /// </summary>
    [Fact]
    public void Step1ShopOpenButtonNeedsShopAvailableToo()
    {
        var hit = Hit(shopOpen: true);
        var context = Ready(interlude: true, shopAvailable: false);

        var route = Route(Board(), hit, context);

        Assert.NotEqual(RouteAction.OpenShop, route.Action);
        Assert.Equal(RouteAction.Ignore, route.Action);
    }

    // ---- step 2: nobody to command -----------------------------------------------------

    [Fact]
    public void Step2NoCommandedCombatantIgnoresTheClick()
    {
        var hit = Hit(square: new GridPosition(1, 1));
        var context = Ready(hasCommanded: false);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, context).Action);
    }

    /// <summary>Nobody to command outranks even an armed action.</summary>
    [Fact]
    public void Step2OutranksAnArmedAction()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var hit = Hit(square: new GridPosition(1, 1));
        var context = Ready(hasCommanded: false);

        Assert.Equal(RouteAction.Ignore, Route(focus, hit, context).Action);
    }

    // ---- step 3: an act is playing out --------------------------------------------------

    [Fact]
    public void Step3AnActPlayingOutIgnoresTheClick()
    {
        var hit = Hit(square: new GridPosition(1, 1));
        var context = Ready(actInProgress: true);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, context).Action);
    }

    /// <summary>An act playing out outranks even an armed action.</summary>
    [Fact]
    public void Step3OutranksAnArmedAction()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var hit = Hit(square: new GridPosition(1, 1));
        var context = Ready(actInProgress: true);

        Assert.Equal(RouteAction.Ignore, Route(focus, hit, context).Action);
    }

    // ---- step 4: an armed action ---------------------------------------------------------

    [Fact]
    public void Step4AnArmedActionActivatesTheSquareUnderThePixel()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var square = new GridPosition(3, 4);
        var hit = Hit(square: square);

        var route = Route(focus, hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Equal(square, route.Square);
    }

    /// <summary>
    /// Acceptance criterion 4: a click on the chrome while an action is armed cancels it —
    /// <c>ActivateSquareAt</c> with a null square, which <c>ActivateSquare</c> treats as
    /// "nowhere" and never spends. <c>OverOverlay</c> wins even though the pixel also hit a
    /// button — an armed action reads the raw pixel facts, not whatever the rest of the
    /// pipeline would have made of them.
    /// </summary>
    [Fact]
    public void Step4AClickOnTheChromeWhileArmedCancelsWithoutSpendingAnything()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var hit = Hit(button: 5, overOverlay: true);

        var route = Route(focus, hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Null(route.Square);
    }

    /// <summary>
    /// Defensive: <c>PlayMode.HitTest</c> never actually sets <c>MenuRow</c> while
    /// <c>Targeting</c> is on top (targeting replaces the menu at the top of the stack, so
    /// none of <c>HitTest</c>'s three row-list branches match), but the router's own
    /// ordering must not lean on that invariant — this focus's branch reads
    /// <c>Square</c>/<c>OverOverlay</c> straight through and never looks at
    /// <c>MenuRow</c>/<c>Button</c> at all.
    /// </summary>
    [Fact]
    public void Step4OutranksMenuRowsAndButtonsRegardlessOfWhatHitTestReported()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var square = new GridPosition(7, 7);
        var hit = Hit(menuRow: 0, button: 0, square: square);

        var route = Route(focus, hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Equal(square, route.Square);
    }

    // ---- step 5 vs step 6: the fact the first version of this file could not pin --------

    /// <summary>
    /// <b>The router, not <c>HitTest</c>'s loop order, decides menu-row-versus-button.</b>
    /// Both facts are set at once — exactly what a real click can produce, since an open
    /// menu's rows and the button strip beneath it are both visually live at the same time
    /// and their rects can overlap. Step 5 wins. Knockout: swap this method's step 5 and
    /// step 6 checks and this test goes red; nothing else does, because it is the only test
    /// that hands the router both facts simultaneously.
    /// </summary>
    [Fact]
    public void Step5WinsOverStep6WhenBothAMenuRowAndAButtonMatchThePixel()
    {
        var hit = Hit(menuRow: 2, button: 5);

        var route = Route(With(new PlayFocus.AttackMenu()), hit, Ready());

        Assert.Equal(RouteAction.TakeMenuRowAt, route.Action);
        Assert.Equal(2, route.Index);
    }

    [Fact]
    public void Step5AMenuRowTakesThatRow()
    {
        var hit = Hit(menuRow: 3);

        var route = Route(With(new PlayFocus.AttackMenu()), hit, Ready());

        Assert.Equal(RouteAction.TakeMenuRowAt, route.Action);
        Assert.Equal(3, route.Index);
    }

    // ---- step 6: the button row ------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion 3, part one, and the first of the two steps the issue calls out
    /// by name: the button row is checked <b>after</b> the menu's rows and <b>before</b> the
    /// close-menu fallback (step 7), so a button click while a menu is open runs the button
    /// — which may itself toggle that very menu — rather than closing the menu first.
    /// </summary>
    [Fact]
    public void Step6AButtonRunsEvenWhileAMenuIsOpen()
    {
        var hit = Hit(button: 1);

        var route = Route(With(new PlayFocus.AttackMenu()), hit, Ready());

        Assert.Equal(RouteAction.RunButtonRow, route.Action);
        Assert.Equal(1, route.Index);
    }

    // ---- step 7: close the open menu, swallowing the click ---------------------------------

    /// <summary>
    /// Acceptance criterion 3, part two, and the second named step: a click that matches
    /// neither a row (step 5) nor a button (step 6), with a menu open, closes the menu
    /// <b>and does not act on the square</b> — the click is swallowed, not redirected.
    /// </summary>
    [Fact]
    public void Step7AGridClickWithAMenuOpenClosesTheMenuAndLeavesTheSquareUntouched()
    {
        var square = new GridPosition(6, 6);
        var hit = Hit(square: square);

        var route = Route(With(new PlayFocus.AttackMenu()), hit, Ready());

        Assert.Equal(RouteAction.DropToBoard, route.Action);
        Assert.NotEqual(RouteAction.ActivateSquareAt, route.Action);
    }

    /// <summary>Step 7 swallows a chrome click with a menu open too, not just a grid one.</summary>
    [Fact]
    public void Step7OutranksTheOverlayIgnoreWhenAMenuIsOpen()
    {
        var hit = Hit(overOverlay: true);

        Assert.Equal(RouteAction.DropToBoard, Route(With(new PlayFocus.SpellMenu()), hit, Ready()).Action);
    }

    // ---- step 8: the fixed chrome, nothing left to close -----------------------------------

    [Fact]
    public void Step8AClickOnTheChromeWithNothingOpenIsIgnored()
    {
        var hit = Hit(overOverlay: true);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, Ready()).Action);
    }

    /// <summary>
    /// <c>ClickHit.Square</c> is computed alongside <c>OverOverlay</c> (S4's <c>HitTest</c>
    /// fills both), but a chrome click must still be ignored rather than acting on whatever
    /// square happens to sit behind the panel.
    /// </summary>
    [Fact]
    public void Step8OutranksActivatingTheSquareEvenWhenOneWasComputed()
    {
        var hit = Hit(square: new GridPosition(0, 0), overOverlay: true);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, Ready()).Action);
    }

    // ---- step 9: the square itself ----------------------------------------------------------

    [Fact]
    public void Step9AClickOnTheBoardActivatesItsSquare()
    {
        var square = new GridPosition(2, 2);
        var hit = Hit(square: square);

        var route = Route(Board(), hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Equal(square, route.Square);
    }

    [Fact]
    public void Step9AClickOffTheBoardActivatesNoSquare()
    {
        var route = Route(Board(), Hit(), Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Null(route.Square);
    }
}
