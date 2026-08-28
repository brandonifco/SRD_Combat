using SRDCombat.Game;
using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// What each input means, given what has the player's attention.
/// </summary>
/// <remarks>
/// <para>
/// One test per branch of the priority order (#500, acceptance criterion 4). The order
/// used to live inline in <c>PlayMode._UnhandledInput</c>, where it was correct only
/// because it had been written in the right sequence — nothing named it, nothing held it,
/// and nothing would have failed had a later edit re-ranked it. These tests are what
/// holds it now.
/// </para>
/// <para>
/// The router takes a <c>ClientInput</c> rather than a <c>Godot.InputEvent</c> for the
/// reason <c>ClientInput</c>'s remarks record: constructing an <c>InputEventKey</c>
/// outside a running engine does not throw, it terminates the test host and fails every
/// other test in the assembly with a misleading message.
/// </para>
/// </remarks>
public class PlayFocusRouterTests
{
    /// <summary>Mid-fight, nothing in the way — the context most branches are read against.</summary>
    /// <remarks>
    /// The shop and the outcome card left this record in S2 (#501) and the quit
    /// confirmation in S3 (#502): all three are focus layers, so a test that wants one
    /// pushes it rather than setting a flag here. Nothing about attention is left in it.
    /// </remarks>
    private static RouteContext Fighting(
        bool actInProgress = false,
        int menuRowCount = 0,
        bool canArmAttack = true,
        bool hasCommanded = true,
        bool hasCursor = true) =>
        new(true, actInProgress, menuRowCount, canArmAttack, hasCommanded, hasCursor);

    private static FocusStack<PlayFocus> Board() => new(new PlayFocus.Board());

    private static FocusStack<PlayFocus> With(PlayFocus layer)
    {
        var stack = Board();
        stack.Push(layer);
        return stack;
    }

    private static RouteAction Route(FocusStack<PlayFocus> focus, ClientInput input, RouteContext context) =>
        PlayFocusRouter.Route(focus, input, context).Action;

    // ---- the quit card preempts everything ------------------------------------------

    [Fact]
    public void EscapeWhileTheQuitCardIsUpQuitsTheGame()
    {
        Assert.Equal(
            RouteAction.QuitGame,
            Route(With(new PlayFocus.QuitConfirm()), ClientInput.Pressed(ClientKey.Escape), Fighting()));
    }

    [Fact]
    public void AnyOtherKeyWhileTheQuitCardIsUpTakesItDownUnharmed()
    {
        Assert.Equal(
            RouteAction.DismissQuitConfirm,
            Route(With(new PlayFocus.QuitConfirm()), ClientInput.Typed('d'), Fighting()));
    }

    [Fact]
    public void AClickWhileTheQuitCardIsUpTakesItDownToo()
    {
        Assert.Equal(
            RouteAction.DismissQuitConfirm,
            Route(With(new PlayFocus.QuitConfirm()), ClientInput.Clicked(10, 10), Fighting()));
    }

    /// <summary>
    /// The card swallows what it does not act on, rather than letting it past. Otherwise a
    /// drag would pan the camera from behind the confirmation, which is what the old
    /// unconditional <c>return</c> prevented.
    /// </summary>
    [Fact]
    public void EverythingElseWhileTheQuitCardIsUpIsSwallowed()
    {
        var motion = new ClientInput(ClientInputKind.MouseMoved, ClientKey.Other, '\0', 4, 4);
        var card = With(new PlayFocus.QuitConfirm());

        // Motion does not dismiss it — only a key or a button going down does. The old
        // block returned without dismissing on anything else, and this pins that reading.
        Assert.Equal(RouteAction.Ignore, Route(card, motion, Fighting()));
        Assert.Equal(
            RouteAction.Ignore,
            Route(card, new ClientInput(ClientInputKind.Other, ClientKey.Other, '\0', 0, 0), Fighting()));
    }

    /// <summary>
    /// The card outranks an armed action: it is checked before the focus stack is asked
    /// anything. The bug it was added for was Esc past the last thing to cancel quitting
    /// the game mid-fight (reported from play, 2026-08-18).
    /// </summary>
    [Fact]
    public void TheQuitCardOutranksAnArmedAction()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));
        focus.Push(new PlayFocus.QuitConfirm());

        Assert.Equal(RouteAction.QuitGame, Route(focus, ClientInput.Pressed(ClientKey.Escape), Fighting()));
    }

    /// <summary>
    /// The card preempts the act gate: quitting must not wait on an animation. It sits
    /// above <c>ActInProgress</c>, not below it.
    /// </summary>
    [Fact]
    public void TheQuitCardAnswersEvenWhileAnActIsPlayingOut()
    {
        var card = With(new PlayFocus.QuitConfirm());

        Assert.Equal(
            RouteAction.QuitGame,
            Route(card, ClientInput.Pressed(ClientKey.Escape), Fighting(actInProgress: true)));

        Assert.Equal(
            RouteAction.DismissQuitConfirm,
            Route(card, ClientInput.Typed('d'), Fighting(actInProgress: true)));
    }

    // ---- Esc, by destination ---------------------------------------------------------

    [Fact]
    public void EscapeOnTheBareBoardAsksToQuit()
    {
        Assert.Equal(
            RouteAction.AskToQuit,
            Route(Board(), ClientInput.Pressed(ClientKey.Escape), Fighting()));
    }

    [Fact]
    public void EscapeClosesTheShopRatherThanAskingToQuit()
    {
        Assert.Equal(
            RouteAction.CloseTopLayer,
            Route(With(new PlayFocus.Shop()), ClientInput.Pressed(ClientKey.Escape), Fighting()));
    }

    /// <summary>
    /// <b>Esc on the outcome card advances the run — it does not dismiss it.</b>
    /// <c>CommitOutcome</c> runs <c>CompleteAndReport</c>: experience, loot, autosave. A
    /// structure that assumed "Esc backs out" would silently turn an acknowledgement into
    /// a cancel and lose the fight's rewards. This is the sharpest row of the table.
    /// </summary>
    [Fact]
    public void EscapeCommitsTheOutcomeCardRatherThanDismissingIt()
    {
        var route = Route(With(new PlayFocus.Outcome()), ClientInput.Pressed(ClientKey.Escape), Fighting());

        Assert.Equal(RouteAction.CommitOutcome, route);
        Assert.NotEqual(RouteAction.CloseTopLayer, route);
        Assert.NotEqual(RouteAction.DropToBoard, route);
    }

    /// <summary>
    /// Esc walks back out one layer at a time, and the board is the floor.
    /// </summary>
    /// <remarks>
    /// <b>This is a deliberate behaviour change (#509), not a refactor artefact.</b> Until
    /// S1 the three menu flags and four pending fields were cleared as a set, so Esc from
    /// anywhere landed on the board — there was nothing to step back *to*. Brandon,
    /// 2026-08-27: "ESC should drop one level until it's at the base game, then it behaves
    /// as it does now."
    /// </remarks>
    [Fact]
    public void EscapeWalksBackOutOneLayerAtATime()
    {
        var focus = With(new PlayFocus.SpellMenu());
        focus.Push(new PlayFocus.SlotMenu(FightTestData.AnySpell()));
        focus.Push(new PlayFocus.Targeting(TargetKind.Spell));

        // Targeting -> slot list -> spell list -> board, one Esc each.
        Assert.Equal(RouteAction.CloseTopLayer, Route(focus, ClientInput.Pressed(ClientKey.Escape), Fighting()));
        focus.Pop();

        Assert.Equal(RouteAction.CloseTopLayer, Route(focus, ClientInput.Pressed(ClientKey.Escape), Fighting(menuRowCount: 3)));
        focus.Pop();

        Assert.Equal(RouteAction.CloseTopLayer, Route(focus, ClientInput.Pressed(ClientKey.Escape), Fighting(menuRowCount: 5)));
        focus.Pop();

        // The floor: at the board it behaves as it always did.
        Assert.Equal(RouteAction.AskToQuit, Route(focus, ClientInput.Pressed(ClientKey.Escape), Fighting()));
        Assert.Equal(1, focus.Depth);
    }

    /// <summary>
    /// Targeting armed straight off the board — a single-attack character, or Tab from a
    /// cold turn — has no menu under it, so one Esc reaches the board.
    /// </summary>
    [Fact]
    public void EscapeFromTargetingArmedOffTheBoardReachesTheBoardInOneStep()
    {
        var focus = With(new PlayFocus.Targeting(TargetKind.Attack));

        Assert.Equal(RouteAction.CloseTopLayer, Route(focus, ClientInput.Pressed(ClientKey.Escape), Fighting()));

        focus.Pop();

        Assert.IsType<PlayFocus.Board>(focus.Top);
    }

    /// <summary>
    /// The other half of "the notice leaves with the layer": Esc on a stall carrying a
    /// purchase notice routes to the pop, and the pop is what discards the notice. The
    /// old code had to clear two things and could clear one.
    /// </summary>
    [Fact]
    public void EscapeOnAStallCarryingANoticePopsItNoticeAndAll()
    {
        var focus = With(new PlayFocus.Shop("Bought: a Potion of Healing."));

        Assert.Equal(
            RouteAction.CloseTopLayer,
            Route(focus, ClientInput.Pressed(ClientKey.Escape), Fighting()));

        focus.Pop();

        Assert.Null(focus.Topmost<PlayFocus.Shop>());
    }

    // ---- the outcome card's any-key --------------------------------------------------

    [Fact]
    public void AnyKeyCommitsTheOutcomeCard()
    {
        Assert.Equal(
            RouteAction.CommitOutcome,
            Route(With(new PlayFocus.Outcome()), ClientInput.Typed('x'), Fighting()));
    }

    /// <summary>
    /// A left-click on the outcome card is handled by <c>PlayMode._UnhandledInput</c>
    /// itself, before <c>HandleClick</c>'s cascade even runs — a deliberate boundary
    /// (#503, S4 review), not a gap the click pipeline forgot. It sits above the camera
    /// handling and is not one of the click pipeline's nine steps, so bringing it in here
    /// would need its own scoped slice with a left-button-and-ordering characterization
    /// test, not a drive-by move riding another slice's commit.
    /// </summary>
    [Fact]
    public void AClickDoesNotCommitTheOutcomeCardThroughTheRouter()
    {
        Assert.Equal(
            RouteAction.Unhandled,
            Route(With(new PlayFocus.Outcome()), ClientInput.Clicked(5, 5), Fighting()));
    }

    // ---- the gates -------------------------------------------------------------------

    [Fact]
    public void TheKeyboardCommandsNothingWhileAnActIsPlayingOut()
    {
        Assert.Equal(
            RouteAction.Ignore,
            Route(Board(), ClientInput.Typed('d'), Fighting(actInProgress: true)));
    }

    /// <summary>Esc stays live through an act: quitting must not wait on an animation.</summary>
    [Fact]
    public void EscapeStillWorksWhileAnActIsPlayingOut()
    {
        Assert.Equal(
            RouteAction.AskToQuit,
            Route(Board(), ClientInput.Pressed(ClientKey.Escape), Fighting(actInProgress: true)));
    }

    /// <summary>
    /// The stall takes the whole keyboard: a board hotkey typed over it reaches nothing.
    /// This is <c>Shop.SuppressesBoard</c>, the one focus that answers it — and the guard
    /// it preserves is unreachable in practice, since the stall only opens from the
    /// interlude. Kept because deleting an unreachable guard mid-refactor is the change no
    /// capture can show.
    /// </summary>
    [Fact]
    public void TheKeyboardIsNotTheBoardsWhileTheShopIsOpen()
    {
        Assert.Equal(
            RouteAction.Unhandled,
            Route(With(new PlayFocus.Shop()), ClientInput.Typed('d'), Fighting()));
    }

    [Fact]
    public void TheKeyboardIsNotTheBoardsBetweenFights()
    {
        var between = new RouteContext(false, false, 0, true, true, true);

        Assert.Equal(RouteAction.Unhandled, Route(Board(), ClientInput.Typed('d'), between));
    }

    // ---- Tab -------------------------------------------------------------------------

    [Fact]
    public void TabFromAColdTurnArmsTheAttack()
    {
        Assert.Equal(RouteAction.ArmAttack, Route(Board(), ClientInput.Pressed(ClientKey.Tab), Fighting()));
    }

    [Fact]
    public void TabWhileTargetingWalksTheRing()
    {
        Assert.Equal(
            RouteAction.CycleTarget,
            Route(With(new PlayFocus.Targeting(TargetKind.Attack)), ClientInput.Pressed(ClientKey.Tab), Fighting()));
    }

    [Fact]
    public void TabNeverReachesAnAttackTheRowIsHiding()
    {
        Assert.Equal(
            RouteAction.Ignore,
            Route(Board(), ClientInput.Pressed(ClientKey.Tab), Fighting(canArmAttack: false)));
    }

    /// <summary>
    /// Tab consumes the input even when neither branch fires — today's
    /// <c>if (key.Keycode == Key.Tab) { …; return; }</c> returns unconditionally. It looks
    /// like an oversight and is behaviour.
    /// </summary>
    [Fact]
    public void TabWithAMenuOpenIsSwallowedRatherThanFallingThrough()
    {
        Assert.Equal(
            RouteAction.Ignore,
            Route(With(new PlayFocus.AttackMenu()), ClientInput.Pressed(ClientKey.Tab), Fighting(menuRowCount: 4)));
    }

    // ---- rows versus board -----------------------------------------------------------

    [Fact]
    public void ArrowsBelongToAnOpenMenu()
    {
        var route = PlayFocusRouter.Route(
            With(new PlayFocus.SpellMenu()), ClientInput.Pressed(ClientKey.Down), Fighting(menuRowCount: 5));

        Assert.Equal(RouteAction.MoveMenuIndex, route.Action);
        Assert.Equal(1, route.StepY);
    }

    /// <summary>
    /// Only the vertical pair. A row list has no horizontal move, so Left and Right fall
    /// through to the board cursor rather than being swallowed with the other two.
    /// </summary>
    [Fact]
    public void SidewaysArrowsFallThroughAMenuToTheBoard()
    {
        var route = PlayFocusRouter.Route(
            With(new PlayFocus.SpellMenu()), ClientInput.Pressed(ClientKey.Right), Fighting(menuRowCount: 5));

        Assert.Equal(RouteAction.MoveCursor, route.Action);
        Assert.Equal(1, route.StepX);
    }

    [Fact]
    public void EnterTakesTheHighlightedRow()
    {
        Assert.Equal(
            RouteAction.TakeHighlightedRow,
            Route(With(new PlayFocus.AttackMenu()), ClientInput.Pressed(ClientKey.Enter), Fighting(menuRowCount: 2)));
    }

    [Fact]
    public void ArrowsWalkTheBoardCursorWhenNoMenuIsOpen()
    {
        var route = PlayFocusRouter.Route(Board(), ClientInput.Pressed(ClientKey.Up), Fighting());

        Assert.Equal(RouteAction.MoveCursor, route.Action);
        Assert.Equal(-1, route.StepY);
    }

    [Fact]
    public void ArrowsDoNothingWithNobodyUnderCommand()
    {
        Assert.Equal(
            RouteAction.Unhandled,
            Route(Board(), ClientInput.Pressed(ClientKey.Left), Fighting(hasCommanded: false)));
    }

    [Fact]
    public void EnterOnTheBoardActsOnTheCursorsSquare()
    {
        Assert.Equal(
            RouteAction.ActivateSquare,
            Route(Board(), ClientInput.Pressed(ClientKey.Enter), Fighting()));
    }

    [Fact]
    public void EnterWithNoCursorPlacedDoesNothing()
    {
        Assert.Equal(
            RouteAction.Unhandled,
            Route(Board(), ClientInput.Pressed(ClientKey.Enter), Fighting(hasCursor: false)));
    }

    // ---- hotkeys ---------------------------------------------------------------------

    [Fact]
    public void ALetterRunsItsActionOnTheBoard()
    {
        var route = PlayFocusRouter.Route(Board(), ClientInput.Typed('d'), Fighting());

        Assert.Equal(RouteAction.RunHotkey, route.Action);
        Assert.Equal('d', route.Character);
    }

    /// <summary>
    /// A letter typed while an action is armed is reaching past a choice the player is in
    /// the middle of. This is the one focus that answers <c>SuppressesHotkeys</c>.
    /// </summary>
    [Fact]
    public void ALetterIsSuppressedWhileTargetingIsArmed()
    {
        Assert.Equal(
            RouteAction.Unhandled,
            Route(With(new PlayFocus.Targeting(TargetKind.Potion)), ClientInput.Typed('d'), Fighting()));
    }

    /// <summary>
    /// A menu does not suppress them — the old <c>if (_pending == Pending.Nothing)</c> was
    /// about the armed state alone, and Cast pressed over the attack menu has always
    /// swapped the menus rather than being ignored.
    /// </summary>
    [Fact]
    public void AMenuDoesNotSuppressHotkeys()
    {
        Assert.Equal(
            RouteAction.RunHotkey,
            Route(With(new PlayFocus.AttackMenu()), ClientInput.Typed('c'), Fighting(menuRowCount: 3)));
    }
}
