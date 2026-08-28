using SRDCombat.Core.Combat;
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

    /// <summary>
    /// Back out to the board, closing every layer over it.
    /// </summary>
    /// <remarks>
    /// No <see cref="EscapeMeaning"/> maps here any more — #509 moved every menu and
    /// <see cref="PlayFocus.Targeting"/> onto <see cref="EscapeMeaning.CloseSelf"/>, so Esc
    /// never reaches this route today. It survives for the click pipeline (#503, S4 step
    /// 7): a click on the grid while a menu is open closes the menu <b>and swallows the
    /// click</b> rather than acting through it, which is exactly this action's effect —
    /// <c>ClearPending</c>, i.e. <c>PopToRoot</c> — reused rather than duplicated.
    /// </remarks>
    DropToBoard,

    /// <summary>
    /// Close the top layer only, leaving whatever is under it open.
    /// </summary>
    /// <remarks>
    /// The shop, every row menu and <see cref="PlayFocus.Targeting"/> answer
    /// <see cref="EscapeMeaning.CloseSelf"/> with this route since #509. The click pipeline
    /// (#503) reuses it too: a click on the shop's back button is exactly "close the top
    /// layer", so it routes here rather than growing a synonym.
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

    // ---- click-only, from RouteClick (#503, S4) --------------------------------------

    /// <summary>
    /// Buy the shop offer at <c>Index</c>. The engine's answer either way replaces the
    /// stall's notice — a purchase re-lists it with the purse lighter, a refusal shows its
    /// code like every other rule.
    /// </summary>
    PurchaseShopRow,

    /// <summary>Open the merchant's stall.</summary>
    OpenShop,

    /// <summary>Start the next fight.</summary>
    ContinueFight,

    /// <summary>
    /// Take the open menu's row at <c>Index</c> — the click's own row, not the keyboard's
    /// highlighted one, so a click needs no highlight to have landed first.
    /// </summary>
    TakeMenuRowAt,

    /// <summary>
    /// Run the button at <c>Index</c>, whatever it does — including toggling the very menu
    /// open beneath it. Checked after the open menu's rows and before the close-menu
    /// fallback: a button click while a menu is open runs the button rather than closing
    /// the menu first.
    /// </summary>
    RunButtonRow,

    /// <summary>
    /// Act on <c>Square</c>, or on nothing when it is null — the same path Enter takes from
    /// the board cursor, addressed by pixel instead. Null stands for "anywhere else": off
    /// the board, or the chrome, and either backs out of an armed action without spending
    /// it.
    /// </summary>
    ActivateSquareAt,
}

/// <summary>One decision about one input.</summary>
/// <param name="Action">What to do.</param>
/// <param name="StepX">Horizontal step, for the moves.</param>
/// <param name="StepY">Vertical step, for the moves.</param>
/// <param name="Character">The typed character, for <see cref="RouteAction.RunHotkey"/>.</param>
/// <param name="Index">
/// Which row or button, for <see cref="RouteAction.PurchaseShopRow"/>,
/// <see cref="RouteAction.TakeMenuRowAt"/> and <see cref="RouteAction.RunButtonRow"/>.
/// </param>
/// <param name="Square">
/// The square to act on, for <see cref="RouteAction.ActivateSquareAt"/>. Null means
/// "nowhere" — off the board, or the chrome under an armed action.
/// </param>
internal readonly record struct Route(
    RouteAction Action,
    int StepX = 0,
    int StepY = 0,
    char Character = '\0',
    int Index = 0,
    GridPosition? Square = null);

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
/// <param name="Interlude">
/// Whether the phase is the interlude between fights — not simply <c>!Fighting</c>, since a
/// finished run (<c>Phase.RunOver</c>) is neither. The click pipeline's guard on
/// <see cref="ClickHit"/>'s shop-related facts (#503, qc review round 1): <c>PlayMode</c>'s
/// hit-testing reports those facts unconditionally now, so the router — not the node — is
/// what refuses to honour a stray one outside the screen it belongs to.
/// </param>
/// <param name="ShopAvailable">
/// Whether this interlude offers a stall at all (a Long Rest's own fact, unrelated to the
/// focus stack). Gates <see cref="ClickHit.ShopOpen"/> the same way.
/// </param>
internal readonly record struct RouteContext(
    bool Fighting,
    bool ActInProgress,
    int MenuRowCount,
    bool CanArmAttack,
    bool HasCommanded,
    bool HasCursor,
    bool Interlude = false,
    bool ShopAvailable = false);

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
    /// Routes one click against the current focus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The priority order lives here now too</b> (#503, S4). It used to be a second,
    /// independent copy inside <c>PlayMode.HandleClick</c>, in a different vocabulary from
    /// <see cref="Route(FocusStack{PlayFocus},ClientInput,RouteContext)"/>'s own — nothing
    /// made the two agree, and they agreed only because people read the file carefully.
    /// </para>
    /// <para>
    /// <b>This method owns every guard, not just the order.</b> (qc review round 1.)
    /// <c>PlayMode.HitTest</c> reports <see cref="ClickHit"/>'s fields unconditionally —
    /// every rect tested against the pixel regardless of phase, of what is open, of what is
    /// available — so a fact reaching here is only ever honoured because <i>this method</i>
    /// checked <see cref="RouteContext.Interlude"/>, <see cref="RouteContext.ShopAvailable"/>
    /// or the focus stack first, never because the node declined to compute it. A hit-test
    /// that pre-filters is a hit-test that has already made the decision it was supposed to
    /// hand over.
    /// </para>
    /// <para>
    /// <b>The nine steps below are numbered to match the issue and the design doc, and the
    /// order is preserved exactly — including two steps that look like bugs and are not:</b>
    /// </para>
    /// <list type="number">
    /// <item>the interlude screen, gated on <see cref="RouteContext.Interlude"/> explicitly
    /// rather than trusted from which <see cref="ClickHit"/> fields happen to be set —
    /// within it, the back button and the stall's rows only while the shop
    /// (<c>focus.Top is PlayFocus.Shop</c>) is open, else the open-stall button (also gated
    /// on <see cref="RouteContext.ShopAvailable"/>) and the continue button;</item>
    /// <item>nobody to command;</item>
    /// <item>an act still playing out;</item>
    /// <item>an armed action, which resolves against <see cref="ClickHit.Square"/> and
    /// <see cref="ClickHit.OverOverlay"/> directly rather than waiting on any other field —
    /// a click on the chrome is "anywhere else", and cancelling must never cost anything;</item>
    /// <item>the open menu's rows (<see cref="ClickHit.MenuRow"/>);</item>
    /// <item><b>the button row (<see cref="ClickHit.Button"/>) — after the menu's rows and
    /// before the close-menu fallback.</b> Clicking a button while a menu is open runs the
    /// button, which may itself toggle that very menu, rather than closing the menu first.
    /// Both fields are populated independently by <c>HitTest</c>, so this order is a real
    /// choice this method makes rather than one <c>HitTest</c> already made by testing menu
    /// rows first;</item>
    /// <item><b>a menu still open, swallowing the click.</b> Neither a row nor a button
    /// caught it, so the menu closes rather than being acted through — the square
    /// underneath is not touched, even though one might be there;</item>
    /// <item>the fixed chrome, with nothing left open to close;</item>
    /// <item>the square itself.</item>
    /// </list>
    /// </remarks>
    internal static Route RouteClick(FocusStack<PlayFocus> focus, ClickHit hit, RouteContext context)
    {
        ArgumentNullException.ThrowIfNull(focus);

        // Step 1 — the interlude screen. Gated on context.Interlude explicitly: hit's
        // shop-related fields are computed unconditionally by HitTest, so this check, not
        // whichever fields happen to be set, is what confines this branch to the screen it
        // belongs to.
        if (context.Interlude)
        {
            if (focus.Top is PlayFocus.Shop)
            {
                if (hit.ShopBack)
                {
                    return new Route(RouteAction.CloseTopLayer);
                }

                if (hit.ShopRow is { } shopRow)
                {
                    return new Route(RouteAction.PurchaseShopRow, Index: shopRow);
                }

                return new Route(RouteAction.Ignore);
            }

            if (context.ShopAvailable && hit.ShopOpen)
            {
                return new Route(RouteAction.OpenShop);
            }

            if (hit.Continue)
            {
                return new Route(RouteAction.ContinueFight);
            }

            return new Route(RouteAction.Ignore);
        }

        // Step 2 — nobody to command: no encounter, or it is not the party's turn.
        if (!context.HasCommanded)
        {
            return new Route(RouteAction.Ignore);
        }

        // Step 3 — the mouse waits with the keyboard while an act plays out.
        if (context.ActInProgress)
        {
            return new Route(RouteAction.Ignore);
        }

        // Step 4 — an armed action resolves before anything else the pixel might have hit.
        // A click on the overlay is "anywhere else": it backs out without spending, so this
        // reads OverOverlay and Square straight through rather than any other field.
        if (focus.Top is PlayFocus.Targeting)
        {
            return new Route(RouteAction.ActivateSquareAt, Square: hit.OverOverlay ? null : hit.Square);
        }

        // Step 5 — the open menu's rows. Independent of step 6's fact: HitTest sets both
        // MenuRow and Button whenever their rects match, so this method is the one place
        // that picks a winner when they could both be set.
        //
        // The <see cref="PlayFocus.RowMenu"/> test is the gate, and it belongs here rather
        // than in HitTest: a row may only be taken while a row menu is actually on top.
        // HitTest reports the rectangle it found and says nothing about whether taking it
        // is allowed — that separation is the whole point of the slice, and without this
        // check the router would honour a stale MenuRow from board focus.
        if (focus.Top is PlayFocus.RowMenu && hit.MenuRow is { } menuRow)
        {
            return new Route(RouteAction.TakeMenuRowAt, Index: menuRow);
        }

        // Step 6 — the button row. Deliberately after the rows and before step 7's
        // close-menu fallback: see this method's remarks.
        if (hit.Button is { } button)
        {
            return new Route(RouteAction.RunButtonRow, Index: button);
        }

        // Step 7 — a menu is still open and neither of the above caught the click. It
        // closes rather than being acted through, and the click is swallowed.
        if (focus.Top is PlayFocus.RowMenu)
        {
            return new Route(RouteAction.DropToBoard);
        }

        // Step 8 — the fixed chrome, with nothing open to close.
        if (hit.OverOverlay)
        {
            return new Route(RouteAction.Ignore);
        }

        // Step 9 — the square itself.
        return new Route(RouteAction.ActivateSquareAt, Square: hit.Square);
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
