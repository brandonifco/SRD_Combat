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
/// <see cref="PlayFocusRouter.RouteClick"/> takes a <see cref="ClickHit"/> rather than a
/// pixel: hit-testing (which rect the pixel fell in) is layout and stays in
/// <c>PlayMode.HitTest</c>; only the decision moved here, for the same untestable-inside-
/// Godot reason <see cref="PlayFocusRouterTests"/>'s remarks record for the keyboard half.
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
    private static RouteContext Ready(bool hasCommanded = true, bool actInProgress = false) =>
        new(Fighting: true, ActInProgress: actInProgress, MenuRowCount: 0, CanArmAttack: true,
            HasCommanded: hasCommanded, HasCursor: true);

    private static Route Route(FocusStack<PlayFocus> focus, ClickHit hit, RouteContext context) =>
        PlayFocusRouter.RouteClick(focus, hit, context);

    // ---- step 1: the interlude screen -------------------------------------------------

    [Fact]
    public void Step1ShopBackButtonClosesTheStall()
    {
        var hit = new ClickHit(HitKind.ShopBack, 0, null, true);

        Assert.Equal(RouteAction.CloseTopLayer, Route(With(new PlayFocus.Shop()), hit, Ready()).Action);
    }

    [Fact]
    public void Step1ShopRowBuysTheOfferAtItsIndex()
    {
        var hit = new ClickHit(HitKind.ShopRow, 2, null, true);
        var route = Route(With(new PlayFocus.Shop()), hit, Ready());

        Assert.Equal(RouteAction.PurchaseShopRow, route.Action);
        Assert.Equal(2, route.Index);
    }

    [Fact]
    public void Step1ShopOpenButtonOpensTheStall()
    {
        var hit = new ClickHit(HitKind.ShopOpen, 0, null, false);

        Assert.Equal(RouteAction.OpenShop, Route(Board(), hit, Ready()).Action);
    }

    [Fact]
    public void Step1ContinueButtonStartsTheNextFight()
    {
        var hit = new ClickHit(HitKind.Continue, 0, null, false);

        Assert.Equal(RouteAction.ContinueFight, Route(Board(), hit, Ready()).Action);
    }

    [Fact]
    public void Step1NothingHitDuringTheInterludeFallsThroughToTheCommandedGate()
    {
        var hit = new ClickHit(HitKind.Nothing, 0, null, false);
        var context = Ready(hasCommanded: false);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, context).Action);
    }

    /// <summary>
    /// The interlude kinds are checked before anything else, exactly like
    /// <c>HandleClick</c>'s own phase branch: there is no commanded combatant between
    /// fights, and the click still resolves.
    /// </summary>
    [Fact]
    public void Step1OutranksTheNoCommandedCombatantGate()
    {
        var hit = new ClickHit(HitKind.Continue, 0, null, false);
        var context = Ready(hasCommanded: false);

        Assert.Equal(RouteAction.ContinueFight, Route(Board(), hit, context).Action);
    }

    // ---- step 2: nobody to command -----------------------------------------------------

    [Fact]
    public void Step2NoCommandedCombatantIgnoresTheClick()
    {
        var hit = new ClickHit(HitKind.Square, 0, new GridPosition(1, 1), false);
        var context = Ready(hasCommanded: false);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, context).Action);
    }

    /// <summary>Nobody to command outranks even an armed action.</summary>
    [Fact]
    public void Step2OutranksAnArmedAction()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var hit = new ClickHit(HitKind.Square, 0, new GridPosition(1, 1), false);
        var context = Ready(hasCommanded: false);

        Assert.Equal(RouteAction.Ignore, Route(focus, hit, context).Action);
    }

    // ---- step 3: an act is playing out --------------------------------------------------

    [Fact]
    public void Step3AnActPlayingOutIgnoresTheClick()
    {
        var hit = new ClickHit(HitKind.Square, 0, new GridPosition(1, 1), false);
        var context = Ready(actInProgress: true);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, context).Action);
    }

    /// <summary>An act playing out outranks even an armed action.</summary>
    [Fact]
    public void Step3OutranksAnArmedAction()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var hit = new ClickHit(HitKind.Square, 0, new GridPosition(1, 1), false);
        var context = Ready(actInProgress: true);

        Assert.Equal(RouteAction.Ignore, Route(focus, hit, context).Action);
    }

    // ---- step 4: an armed action ---------------------------------------------------------

    [Fact]
    public void Step4AnArmedActionActivatesTheSquareUnderThePixel()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var square = new GridPosition(3, 4);
        var hit = new ClickHit(HitKind.Square, 0, square, false);

        var route = Route(focus, hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Equal(square, route.Square);
    }

    /// <summary>
    /// Acceptance criterion 4: a click on the chrome while an action is armed cancels it —
    /// <c>ActivateSquareAt</c> with a null square, which <c>ActivateSquare</c> treats as
    /// "nowhere" and never spends. <c>OverOverlay</c> wins even though <c>HitKind</c> here
    /// says <c>Button</c> — an armed action reads the raw pixel facts, not the
    /// classification the rest of the pipeline uses.
    /// </summary>
    [Fact]
    public void Step4AClickOnTheChromeWhileArmedCancelsWithoutSpendingAnything()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var hit = new ClickHit(HitKind.Button, 5, null, true);

        var route = Route(focus, hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Null(route.Square);
    }

    /// <summary>
    /// Defensive: <c>PlayMode.HitTest</c> never actually produces <c>MenuRow</c> while
    /// <c>Targeting</c> is on top (targeting replaces the menu at the top of the stack), but
    /// the router's own ordering must not lean on that invariant — this focus's branch
    /// reads <c>Square</c>/<c>OverOverlay</c> straight through and never looks at
    /// <c>Kind</c> at all.
    /// </summary>
    [Fact]
    public void Step4OutranksMenuRowsRegardlessOfWhatKindWasClassified()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        var square = new GridPosition(7, 7);
        var hit = new ClickHit(HitKind.MenuRow, 0, square, false);

        var route = Route(focus, hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Equal(square, route.Square);
    }

    // ---- step 5: the open menu's rows -----------------------------------------------------

    [Fact]
    public void Step5AMenuRowTakesThatRow()
    {
        var hit = new ClickHit(HitKind.MenuRow, 3, null, true);

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
        var hit = new ClickHit(HitKind.Button, 1, null, true);

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
        var hit = new ClickHit(HitKind.Square, 0, square, false);

        var route = Route(With(new PlayFocus.AttackMenu()), hit, Ready());

        Assert.Equal(RouteAction.DropToBoard, route.Action);
        Assert.NotEqual(RouteAction.ActivateSquareAt, route.Action);
    }

    /// <summary>Step 7 swallows a chrome click with a menu open too, not just a grid one.</summary>
    [Fact]
    public void Step7OutranksTheOverlayIgnoreWhenAMenuIsOpen()
    {
        var hit = new ClickHit(HitKind.Overlay, 0, null, true);

        Assert.Equal(RouteAction.DropToBoard, Route(With(new PlayFocus.SpellMenu()), hit, Ready()).Action);
    }

    // ---- step 8: the fixed chrome, nothing left to close -----------------------------------

    [Fact]
    public void Step8AClickOnTheChromeWithNothingOpenIsIgnored()
    {
        var hit = new ClickHit(HitKind.Overlay, 0, null, true);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, Ready()).Action);
    }

    /// <summary>
    /// <c>ClickHit.Square</c> is computed alongside <c>Overlay</c>'s classification (S4's
    /// <c>HitTest</c> fills both), but a chrome click must still be ignored rather than
    /// acting on whatever square happens to sit behind the panel.
    /// </summary>
    [Fact]
    public void Step8OutranksActivatingTheSquareEvenWhenOneWasComputed()
    {
        var hit = new ClickHit(HitKind.Overlay, 0, new GridPosition(0, 0), true);

        Assert.Equal(RouteAction.Ignore, Route(Board(), hit, Ready()).Action);
    }

    // ---- step 9: the square itself ----------------------------------------------------------

    [Fact]
    public void Step9AClickOnTheBoardActivatesItsSquare()
    {
        var square = new GridPosition(2, 2);
        var hit = new ClickHit(HitKind.Square, 0, square, false);

        var route = Route(Board(), hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Equal(square, route.Square);
    }

    [Fact]
    public void Step9AClickOffTheBoardActivatesNoSquare()
    {
        var hit = new ClickHit(HitKind.Square, 0, null, false);

        var route = Route(Board(), hit, Ready());

        Assert.Equal(RouteAction.ActivateSquareAt, route.Action);
        Assert.Null(route.Square);
    }
}
