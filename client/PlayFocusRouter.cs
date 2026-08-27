using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer;

/// <summary>What the screen should do about one input.</summary>
internal enum RouteAction
{
    /// <summary>The router has no rule for this; the node carries on as before.</summary>
    Unhandled,

    /// <summary>Swallow it. Something is up that takes input and does nothing with this one.</summary>
    Ignore,

    /// <summary>Leave the game.</summary>
    QuitGame,

    /// <summary>Take the quit confirmation down, unharmed.</summary>
    DismissQuitConfirm,

    /// <summary>Raise the quit confirmation.</summary>
    AskToQuit,

    /// <summary>Acknowledge the outcome card and move the run on.</summary>
    CommitOutcome,

    /// <summary>Back out to the board, closing every layer over it.</summary>
    DropToBoard,

    /// <summary>
    /// Close the top layer only, leaving whatever is under it open.
    /// </summary>
    /// <remarks>
    /// No focus answers <see cref="EscapeMeaning.CloseSelf"/> yet — every menu in this
    /// slice drops all the way to the board, which is today's behaviour and must stay it.
    /// The route exists because the meaning does, and mapping <c>CloseSelf</c> onto
    /// <see cref="DropToBoard"/> to avoid an unused member would be a lie the compiler
    /// would never catch.
    /// </remarks>
    CloseTopLayer,

    /// <summary>Walk the ring of things the armed action could be used on.</summary>
    CycleTarget,

    /// <summary>Arm the attack from a cold turn, naming no attack.</summary>
    ArmAttack,

    /// <summary>Move the highlighted row of the open menu by <c>StepY</c>.</summary>
    MoveMenuIndex,

    /// <summary>Take the highlighted row of the open menu.</summary>
    TakeHighlightedRow,

    /// <summary>Move the board cursor by <c>StepX</c>/<c>StepY</c>.</summary>
    MoveCursor,

    /// <summary>Act on the cursor's square, the same path a click takes.</summary>
    ActivateSquare,

    /// <summary>Run whatever action <c>Character</c> is the hotkey for, if any.</summary>
    RunHotkey,
}

/// <summary>One decision about one input.</summary>
/// <param name="Action">What to do.</param>
/// <param name="StepX">Horizontal step, for the moves.</param>
/// <param name="StepY">Vertical step, for the moves.</param>
/// <param name="Character">The typed character, for <see cref="RouteAction.RunHotkey"/>.</param>
internal readonly record struct Route(
    RouteAction Action,
    int StepX = 0,
    int StepY = 0,
    char Character = '\0');

/// <summary>
/// Everything outside the focus stack that the routing decision still depends on.
/// </summary>
/// <remarks>
/// <para>
/// <b>No modal flags left.</b> S2 took the shop and the outcome card out of here and S3
/// took the quit confirmation; all three are focus layers, and the router asks the stack.
/// What remains is not about attention at all — it is what the engine currently allows and
/// where the cursor is, which are facts about the fight.
/// </para>
/// </remarks>
/// <param name="Fighting">Whether the screen is in a fight rather than between them.</param>
/// <param name="ActInProgress">Whether an act is still playing out on screen.</param>
/// <param name="MenuRowCount">How many rows the open menu has, or zero when none is open.</param>
/// <param name="CanArmAttack">Whether the commanded character is offered the Attack action.</param>
/// <param name="HasCommanded">Whether a character is under the player's command right now.</param>
/// <param name="HasCursor">Whether the board cursor is placed.</param>
internal readonly record struct RouteContext(
    bool Fighting,
    bool ActInProgress,
    int MenuRowCount,
    bool CanArmAttack,
    bool HasCommanded,
    bool HasCursor);

/// <summary>
/// Decides what one input means, given what has the player's attention.
/// </summary>
/// <remarks>
/// <para>
/// <b>The priority order lives here and nowhere else</b> (#500, acceptance criterion 7).
/// <c>PlayMode._UnhandledInput</c>'s keyboard half becomes translate, route, execute: it
/// turns a Godot event into a <see cref="ClientInput"/>, calls this, and performs the
/// <see cref="Route"/>. It makes no decision of its own about what beats what.
/// </para>
/// <para>
/// <b>The order below is today's, preserved deliberately and not improved.</b> Every
/// branch here corresponds to one in the method it replaces, and the migration is
/// worthless if it quietly re-ranks them. Two are worth naming because they look like
/// mistakes and are not:
/// </para>
/// <list type="bullet">
/// <item>
/// Tab always consumes the input, even when neither of its branches fires — today's
/// <c>if (key.Keycode == Key.Tab) { …; return; }</c> returns unconditionally.
/// </item>
/// <item>
/// A <em>left or right</em> arrow while a menu is open falls through to the board
/// cursor. Only the vertical pair belongs to the menu, because a row list has no
/// horizontal move — so the board keeps the other two rather than the menu swallowing
/// all four.
/// </item>
/// </list>
/// </remarks>
internal static class PlayFocusRouter
{
    /// <summary>Routes one input against the current focus.</summary>
    internal static Route Route(FocusStack<PlayFocus> focus, ClientInput input, RouteContext context)
    {
        ArgumentNullException.ThrowIfNull(focus);

        // The quit card owns every input while it is up: Esc again really quits, anything
        // else pressed or clicked takes it down. Reported from play on 2026-08-18 after
        // two accidental exits — Esc is also the key that backs out of an armed action, so
        // one press past the last thing to cancel used to be the whole game gone mid-fight.
        // The one layer named here rather than answered by the table (#502): "any key that
        // is not Esc, and any click, takes this down" is a rule the five members cannot
        // express, and inventing a sixth member for a single layer would be worse than
        // saying it once, here, where the order already lives. Esc still goes through
        // EscapeRoute, so the table stays the authority on what Esc means.
        //
        // It sits above everything — including the ActInProgress gate below — because
        // quitting must not wait on an animation.
        if (focus.Top is PlayFocus.QuitConfirm)
        {
            if (input is { Kind: ClientInputKind.KeyPressed, Key: ClientKey.Escape })
            {
                return new Route(EscapeRoute(focus.Top.Escape));
            }

            return input.Kind is ClientInputKind.KeyPressed or ClientInputKind.MousePressed
                ? new Route(RouteAction.DismissQuitConfirm)
                : new Route(RouteAction.Ignore);
        }

        if (input is { Kind: ClientInputKind.KeyPressed, Key: ClientKey.Escape })
        {
            // Esc backs out of whatever is armed before it quits anything — the merchant's
            // stall included. The whole cascade is one table lookup now (#501): the shop
            // and the card answer CloseSelf and Commit for themselves, and at depth 1 the
            // top *is* the root, whose AskToQuit is "nothing is open, so Esc quits".
            return new Route(EscapeRoute(focus.Top.Escape));
        }

        // Any key moves the outcome card on: it asks nothing of the player but
        // acknowledgement, so hunting for the right key would be its own small annoyance.
        if (input.IsKey && focus.Holds<PlayFocus.Outcome>())
        {
            return new Route(RouteAction.CommitOutcome);
        }

        if (!input.IsKey || !context.Fighting || focus.Top.SuppressesBoard)
        {
            return new Route(RouteAction.Unhandled);
        }

        // While an act is playing out, the keyboard commands nothing — the engine resolves
        // instantly, so without this gate a key pressed mid-swing started the next action
        // before the first had visibly happened (asked for from play, 2026-08-21). Esc
        // stays live above: quitting must not wait on an animation.
        if (context.ActInProgress)
        {
            return new Route(RouteAction.Ignore);
        }

        // Tab walks the ring of things the armed action could be used on — and with
        // nothing armed it arms the attack first (asked for from play, 2026-08-19), so one
        // key reaches "aim at somebody" from a cold turn. Gated the way every keypress is:
        // only while the row actually offers Attacks, so Tab can never reach an action the
        // row hides.
        if (input.Key == ClientKey.Tab)
        {
            if (focus.Holds<PlayFocus.Targeting>())
            {
                return new Route(RouteAction.CycleTarget);
            }

            return context.MenuRowCount == 0 && context.CanArmAttack
                ? new Route(RouteAction.ArmAttack)
                : new Route(RouteAction.Ignore);
        }

        var step = input.ArrowStep;

        // An open menu takes the vertical arrows first: while a spell list is up, Up and
        // Down belong to it rather than to the board behind it.
        if (context.MenuRowCount > 0 && focus.Top.TakesRowKeys)
        {
            if (step is { X: 0 } scroll)
            {
                return new Route(RouteAction.MoveMenuIndex, 0, scroll.Y);
            }

            if (input.Key == ClientKey.Enter)
            {
                return new Route(RouteAction.TakeHighlightedRow);
            }
        }

        if (step is { } move && context.HasCommanded)
        {
            return new Route(RouteAction.MoveCursor, move.X, move.Y);
        }

        if (input.Key == ClientKey.Enter && context.HasCursor)
        {
            return new Route(RouteAction.ActivateSquare);
        }

        // A key runs exactly what its button would, and only while that button is shown —
        // so a keypress can never reach an action the row is hiding. Suppressed while an
        // action is armed: the player is mid-choice, and a letter typed then is reaching
        // past it.
        if (!focus.Top.SuppressesHotkeys && input.Character != '\0')
        {
            return new Route(RouteAction.RunHotkey, Character: input.Character);
        }

        return new Route(RouteAction.Unhandled);
    }

    /// <summary>
    /// The action one <see cref="EscapeMeaning"/> calls for.
    /// </summary>
    /// <remarks>
    /// Exhaustive by throwing rather than by a silent default: a meaning added without a
    /// route is a modal whose Esc key does nothing, which is precisely the class of
    /// silence this refactor exists to end.
    /// </remarks>
    internal static RouteAction EscapeRoute(EscapeMeaning meaning) => meaning switch
    {
        EscapeMeaning.AskToQuit => RouteAction.AskToQuit,
        EscapeMeaning.DropToBoard => RouteAction.DropToBoard,
        EscapeMeaning.CloseSelf => RouteAction.CloseTopLayer,
        EscapeMeaning.Commit => RouteAction.CommitOutcome,
        EscapeMeaning.LeaveTheGame => RouteAction.QuitGame,
        _ => throw new ArgumentOutOfRangeException(
            nameof(meaning), meaning, "No route for this Escape meaning."),
    };
}
