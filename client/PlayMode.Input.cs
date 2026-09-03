using Godot;
using SRDCombat.Core.Combat;
using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer;

/// <summary>The input surface: translating a Godot event, routing it through the focus stack, and the buttons, targeting and menu-row helpers that carry it out.</summary>
public partial class PlayMode : FightScreen
{
    /// <summary>
    /// Two rows of buttons: the actions anybody can take, then what this character
    /// brought — features, spells, potions. Everything with a target is a click on the
    /// grid; Cast and Give Potion arm the next click instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only what can be used is shown.</b> <see cref="TurnOptions"/> decides, so the
    /// console and this client offer the same set and neither works it out for itself:
    /// Dodge and Dash leave the row when the Action does, Stand Up appears only while
    /// Prone, Action Surge only once there is no Action left to surge past. The status
    /// line above still reads out what is left to spend, so a row that has shrunk still
    /// explains itself.
    /// </para>
    /// <para>
    /// <b>Every action answers to a key, and the key never moves.</b> D is Dodge
    /// whenever Dodge is offered; the assignment is a property of the action rather
    /// than of its place in the row, so nothing is relearned when the row changes or
    /// the character does.
    /// </para>
    /// </remarks>
    private void BuildButtons(Combatant active)
    {
        _buttons.Clear();
        _buttonHints.Clear();
        _buttonsFor = active.Id;

        if (_encounter is not { } encounter)
        {
            return;
        }

        // One row, in TurnOptions' own order. It was two — anybody's actions above,
        // this character's below — until the fullscreen layout: the board is as tall as
        // the screen allows, so the rows under it are down to a strict budget, and a
        // fullscreen width holds every button a turn can offer side by side with room
        // to spare.
        var x = (float)UiLeft;

        foreach (var action in TurnOptions.For(encounter, active))
        {
            x = AddButton(x, ButtonRowTop, action);
        }
    }

    /// <summary>
    /// The button rects are cached at build time and anchored to the window's bottom
    /// edge, so a resize leaves them where the old edge was — off-screen when the
    /// window grew shorter. Re-seat them against the new edge.
    /// </summary>
    protected override void OnResized()
    {
        if (CommandedCombatant() is { } commanded)
        {
            BuildButtons(commanded);
        }

        base.OnResized();
    }

    /// <summary>Runs an action, by button or by key. The engine still rules on it.</summary>
    private ActionRefusal? Invoke(TurnAction action)
    {
        var encounter = _encounter!;

        switch (action)
        {
            case TurnAction.Dodge: return encounter.Dodge();
            case TurnAction.Dash: return encounter.Dash();
            case TurnAction.Disengage: return encounter.Disengage();
            case TurnAction.StandUp: return encounter.StandUp();
            case TurnAction.Escape: return encounter.Escape();
            case TurnAction.EndTurn: encounter.EndTurn(); return null;
            case TurnAction.Rage: return encounter.Rage();
            case TurnAction.RecklessAttack: return encounter.RecklessAttack();
            case TurnAction.SecondWind: return encounter.SecondWind();
            case TurnAction.ActionSurge: return encounter.ActionSurge();
            case TurnAction.SteadyAim: return encounter.SteadyAim();
            case TurnAction.CunningDash: return encounter.CunningAction(CunningActionKind.Dash);
            case TurnAction.CunningDisengage: return encounter.CunningAction(CunningActionKind.Disengage);
            case TurnAction.CunningStrikeTrip: return encounter.CunningStrike(CunningStrikeEffect.Trip);

            case TurnAction.Attacks:
                // With one attack there is nothing to choose, so it arms targeting
                // straight away; the menu is for characters carrying a choice.
                if (CommandedCombatant() is { } swinging && swinging.Stats.Attacks.Count == 1)
                {
                    ArmTargeting(TargetKind.Attack, attack: swinging.Stats.Attacks[0]);
                    return null;
                }

                ToggleMenu(new PlayFocus.AttackMenu());
                return null;

            case TurnAction.Cast:
                ToggleMenu(new PlayFocus.SpellMenu());
                return null;

            case TurnAction.Drink:
                return CommandedCombatant()?.Inventory.Weakest is { } potency
                    ? encounter.DrinkPotion(potency)
                    : new ActionRefusal("client.no_potion", "Nothing to drink.");

            case TurnAction.GivePotion: ArmTargeting(TargetKind.Potion); return null;

            case TurnAction.Trade:
                ToggleMenu(new PlayFocus.TradeMenu());
                return null;

            case TurnAction.DivineSparkHeal: ArmTargeting(TargetKind.SparkHeal); return null;
            case TurnAction.DivineSparkHarm: ArmTargeting(TargetKind.SparkHarm); return null;

            default: return null;
        }
    }

    /// <summary>
    /// Arms a targeting mode and points the cursor at the nearest thing it could be
    /// used on.
    /// </summary>
    /// <remarks>
    /// <b>Every road into targeting comes through here</b>, so the cursor is never left
    /// wherever the last action happened to leave it — which is what made choosing a
    /// target from the keyboard a hunt across the board before anything could be aimed.
    /// Nearest first because the nearest enemy is the one being asked about far more
    /// often than not; Tab walks the rest.
    /// </remarks>
    private void ArmTargeting(
        TargetKind kind,
        CombatAttack? attack = null,
        SpellDefinition? spell = null,
        int? slot = null,
        HealingPotion? potency = null)
    {
        // Targeting stacks over the menu that chose it rather than replacing it, so Esc
        // hands that menu back (#509). The menu stays on the stack but stops drawing, since
        // every menu draws only while it is on top — which is why the screen looks exactly
        // as it did when targeting replaced it outright.
        _focus.Push(new PlayFocus.Targeting(kind, attack, spell, slot, potency));

        if (PendingTargets() is [var nearest, ..])
        {
            _cursor = nearest.Position;
        }

        QueueRedraw();
    }

    /// <summary>
    /// Whom the armed action could be pointed at, nearest first, or empty when nothing
    /// is armed.
    /// </summary>
    /// <remarks>
    /// The list is <c>TargetChoice</c>'s, in <c>Game</c>, so this screen holds no opinion
    /// about who may be aimed at — and the engine still refuses anything that reaches it
    /// by another road, exactly as it does for every other client convenience.
    /// </remarks>
    private IReadOnlyList<Combatant> PendingTargets()
    {
        if (_encounter is not { } encounter || CommandedCombatant() is not { } actor)
        {
            return [];
        }

        var offered = Armed is not { } armed
            ? []
            : TargetChoice.For(encounter, actor, armed.Kind, attack: armed.Attack, spell: armed.Spell);

        // The fog filters the ring: Tab landing the cursor on a hidden monster would
        // hand the player its position for free. Allies are always seen.
        return offered
            .Where(target => target.SideId == PregeneratedParty.SideId
                || !_unseen.Contains(target.Position))
            .ToList();
    }

    /// <summary>Moves the cursor to the next target in the ring, wrapping round.</summary>
    private void CycleTarget()
    {
        if (PendingTargets() is not { Count: > 0 } targets)
        {
            return;
        }

        var here = _cursor is { } caret
            ? targets.FirstOrDefault(target => target.Position == caret)?.Id
            : null;

        if (TargetChoice.Next(targets, here) is { } next)
        {
            _cursor = next.Position;
            QueueRedraw();
        }
    }

    /// <summary>Takes a spell off the menu: the slot choice if there is one, else the target.</summary>
    private void ChooseSpell(SpellDefinition spell)
    {
        // Matches pre-#505 behaviour (qc review round): this spell menu stays on the stack,
        // hidden rather than popped, under whatever this call pushes or arms below — so
        // unlike a freshly constructed menu, it would otherwise keep its old highlight
        // across the round trip once Esc uncovers it again. ChooseAttack and ChooseSlot
        // never did this, even before #505 — see PlayFocus.RowMenu.ResetHighlight's remarks.
        if (_focus.Top is PlayFocus.RowMenu current)
        {
            current.ResetHighlight();
        }

        // A slotted spell with more than one slot level to burn is a real choice; one
        // level, or a cantrip, arms straight away and the engine picks as it always has.
        if (CommandedCombatant() is { } caster && SlotLevelsFor(caster, spell).Count > 1)
        {
            // Pushed, not replacing: Esc from the slot list goes back to the spell list
            // it was chosen from (#509).
            _focus.Push(new PlayFocus.SlotMenu(spell));
        }
        else
        {
            ArmTargeting(TargetKind.Spell, spell: spell);
        }

        QueueRedraw();
    }

    private void ChooseSlot(int level)
    {
        // The spell comes off the layer that offered the slots, which is the one place it
        // has been since ChooseSpell put it there — it can no longer be left behind in a
        // field by a menu that closed.
        var spell = _focus.Topmost<PlayFocus.SlotMenu>()?.Spell;

        if (spell is null)
        {
            return;
        }

        ArmTargeting(TargetKind.Spell, spell: spell, slot: level);
        QueueRedraw();
    }

    private void ChooseAttack(CombatAttack attack)
    {
        ArmTargeting(TargetKind.Attack, attack: attack);
        QueueRedraw();
    }

    /// <summary>Takes a potency off the Trade menu and arms the shared target picker.</summary>
    private void ChooseTradePotency(HealingPotion potency)
    {
        ArmTargeting(TargetKind.Trade, potency: potency);
        QueueRedraw();
    }

    /// <summary>The armed action, or null when nothing is armed.</summary>
    private PlayFocus.Targeting? Armed => _focus.Topmost<PlayFocus.Targeting>();

    /// <summary>The open stall and its last notice, or null when it is closed.</summary>
    private PlayFocus.Shop? Shopping => _focus.Topmost<PlayFocus.Shop>();

    /// <summary>
    /// How many rows the open menu has, or zero when none is open — or when the layer on
    /// top has changed since <c>_menuRows</c> was last filled (<see cref="MenuRowList"/>).
    /// </summary>
    private int OpenMenuLength => _menuRows.CountFor(_focus.Top as PlayFocus.RowMenu);

    /// <summary>
    /// Opens a menu over the board, or closes it again when it is the one already open.
    /// </summary>
    /// <remarks>
    /// The toggle the two menu buttons have always had, now over a stack. It drops to the
    /// board first, so pressing Cast with the attack menu up replaces it rather than
    /// leaving both set — a pair the old booleans could hold at once, and did whenever
    /// Cast was pressed while the slot menu was open.
    /// </remarks>
    private void ToggleMenu(PlayFocus menu)
    {
        var alreadyOpen = _focus.Top.GetType() == menu.GetType();

        _focus.PopToRoot();

        if (!alreadyOpen)
        {
            _focus.Push(menu);
        }
    }

    /// <summary>Takes the highlighted row of whichever menu is open.</summary>
    private void TakeHighlightedRow()
    {
        if (_focus.Top is PlayFocus.RowMenu menu)
        {
            TakeMenuRow(menu.MenuIndex);
        }
    }

    /// <summary>
    /// Takes one row of whichever menu is open, by index rather than by highlight — the
    /// keyboard's Enter and a click on a row both end up here, the first with the open
    /// menu's own <see cref="PlayFocus.RowMenu.MenuIndex"/>, the second with whichever row
    /// the pixel landed on (#503).
    /// </summary>
    /// <remarks>
    /// No longer a switch on <c>_focus.Top</c>'s type (#505): an in-range index already
    /// names the right row, and its closed-over <see cref="Action"/> is the whole of what
    /// taking it means — <em>provided</em> <c>_menuRows</c> was actually filled for the
    /// layer that is on top right now, which <see cref="MenuRowList.TryTake"/> is the one
    /// place that checks, by reference, rather than this method trusting the index alone.
    /// </remarks>
    private void TakeMenuRow(int index) => _menuRows.TryTake(index, _focus.Top as PlayFocus.RowMenu);

    /// <summary>The action a keypress means, or null when the key is not bound to a shown one.</summary>
    private TurnAction? ActionForKey(char typed)
    {
        if (CommandedCombatant() is not { } active || _encounter is not { } encounter)
        {
            return null;
        }

        foreach (var action in TurnOptions.For(encounter, active))
        {
            if (char.ToUpperInvariant(typed) == TurnOptions.Hotkey(action))
            {
                return action;
            }
        }

        return null;
    }

    private float AddButton(float x, float y, TurnAction action) =>
        AddButton(
            x,
            y,
            $"{TurnOptions.HotkeyLabel(action)} · {TurnOptions.Caption(action)}",
            () => Invoke(action),
            TurnOptions.Hint(action));

    private float AddButton(float x, float y, string caption, Func<ActionRefusal?> act, string? hint = null)
    {
        var width = TextFont.GetStringSize(caption, fontSize: 13).X + 22;
        _buttons.Add((new Rect2(x, y, width, 28), caption, act));

        if (!string.IsNullOrWhiteSpace(hint))
        {
            _buttonHints[caption] = hint;
        }

        return x + width + 8;
    }

    /// <summary>The active combatant when it is the player's to command, else null.</summary>
    private Combatant? CommandedCombatant() =>
        _phase == Phase.Fighting
        && _encounter is { IsComplete: false } encounter
        && encounter.ActiveCombatant is { } active
        && active.SideId == PregeneratedParty.SideId
        && active.CanAct
            ? active
            : null;

    /// <summary>
    /// Translate, route, execute — and no priority decision of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What beats what lives in <see cref="PlayFocusRouter"/></b> (#500). This method
    /// turns a Godot event into a <see cref="ClientInput"/>, asks the router what it
    /// means given the focus stack, and does that. It used to hold the order itself, in a
    /// cascade whose branches were only in the right sequence because they had been put
    /// there — a new modal inserted at the wrong depth inherited the wrong Esc, silently.
    /// </para>
    /// <para>
    /// <b>The click cascade moved too, in #503 (S4)</b> — a pixel's route now comes from
    /// <see cref="PlayFocusRouter.RouteClick"/> via <see cref="HandleClick"/>, the same
    /// division of labour as this method's own keyboard half. Three mouse paths stay here
    /// by design rather than by omission: the camera (wheel zoom, middle- or right-drag
    /// pan) is nobody's decision, just settling an input before anything else can misread
    /// it; the hover clock only ever clears a tooltip; and the outcome card's left-click
    /// commit, immediately below, is a boundary this method drew on purpose — it precedes
    /// <see cref="HandleClick"/> entirely and is not one of the click pipeline's nine
    /// steps. Folding it in would need its own scoped slice with a left-button-and-
    /// ordering characterization test, not a drive-by move.
    /// </para>
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (Perform(PlayFocusRouter.Route(_focus, Translate(@event), Context())))
        {
            return;
        }

        // The camera's inputs — wheel zoom, middle- or right-drag pan — are nobody
        // else's, so they are settled before the hover clock or a click can misread
        // them.
        if (_phase == Phase.Fighting && HandleCameraInput(@event))
        {
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            // Only real movement restarts the clock. Godot reports motion for sub-pixel
            // drift too, and a hand resting on a button is never perfectly still.
            if (_pointer.DistanceTo(motion.Position) > HoverJitterPixels)
            {
                _pointer = motion.Position;
                _hoverElapsed = 0;

                if (_hint is not null)
                {
                    _hint = null;
                    QueueRedraw();
                }
            }

            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
            && _focus.Holds<PlayFocus.Outcome>())
        {
            CompleteAndReport();
            QueueRedraw();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            // A click is an answer, not a question: whatever the pointer was explaining
            // goes away rather than hanging over the result.
            _hint = null;
            _hoverElapsed = 0;
            HandleClick(click.Position);
        }
    }

    /// <summary>How long the pointer must rest before a hint appears, in seconds.</summary>
    private const double HoverDelaySeconds = 2;

    /// <summary>How far the pointer may drift and still count as resting.</summary>
    private const float HoverJitterPixels = 3;

    /// <summary>
    /// Counts the pointer's rest and raises a hint once it has been still long enough.
    /// </summary>
    private void AdvanceHover(double delta)
    {
        if (_hoverElapsed >= HoverDelaySeconds)
        {
            // Already asked and answered. The hint is *not* re-read every frame: it is
            // taken once when the pause completes, so it cannot flicker as the fight
            // changes underneath a motionless pointer.
            return;
        }

        _hoverElapsed += delta;

        if (_hoverElapsed < HoverDelaySeconds)
        {
            return;
        }

        _hint = HintAt(_pointer);

        if (_hint is not null)
        {
            QueueRedraw();
        }
    }

    /// <summary>
    /// What the pointer is resting on, or null where nothing has anything to say.
    /// </summary>
    /// <remarks>
    /// Read at the moment the hint is shown rather than cached on hover, so a hint
    /// cannot outlive what it describes: a creature that dies while the pointer sits on
    /// it stops claiming to be standing.
    /// </remarks>
    private string? HintAt(Vector2 pixel)
    {
        if (_phase != Phase.Fighting || Shopping is not null)
        {
            return null;
        }

        foreach (var (rect, caption, _) in _buttons)
        {
            if (rect.HasPoint(pixel) && _buttonHints.TryGetValue(caption, out var hint))
            {
                return hint;
            }
        }

        if (_encounter is not { } encounter || SquareAt(pixel) is not { } square)
        {
            return null;
        }

        // Whoever is standing there, preferring the living: a corpse and a character can
        // share a square, and the one being asked about is the one on their feet. A
        // monster the fog hides answers no hover — a tooltip through a wall would be
        // the hint scouting for the player.
        var occupant = encounter.Combatants
            .Where(combatant => combatant.Position == square
                && (combatant.SideId == PregeneratedParty.SideId || !_unseen.Contains(square)))
            .OrderBy(combatant => combatant.IsDead ? 1 : 0)
            .FirstOrDefault();

        // The banner is what the screen already says about whoever is acting — name,
        // class, armour class, hit points and every attack with its damage — so hovering
        // asks the same question of somebody else and gets an answer worded once.
        return occupant is null ? null : string.Join("\n", TurnBanner.Lines(occupant));
    }

    /// <summary>Backs all the way out to the board.</summary>
    private void ClearPending() => _focus.PopToRoot();

    /// <summary>One Godot event in the client's own vocabulary.</summary>
    /// <remarks>
    /// Godot's event types derive from <c>RefCounted</c>, which cannot be constructed
    /// outside a running engine without taking the test host down with it — so the
    /// translation stops here and everything past it is testable. See
    /// <see cref="ClientInput"/>'s remarks for the measurement.
    /// </remarks>
    private static ClientInput Translate(InputEvent @event) => @event switch
    {
        InputEventKey { Pressed: true } key => new ClientInput(
            ClientInputKind.KeyPressed,
            key.Keycode switch
            {
                Key.Escape => ClientKey.Escape,
                Key.Tab => ClientKey.Tab,
                Key.Enter or Key.KpEnter => ClientKey.Enter,
                Key.Left => ClientKey.Left,
                Key.Right => ClientKey.Right,
                Key.Up => ClientKey.Up,
                Key.Down => ClientKey.Down,
                Key.Space => ClientKey.Space,
                _ => ClientKey.Other,
            },
            // Space is the End Turn hotkey and reaches ActionForKey as a space character,
            // exactly as it did when this cast lived inline.
            key.Keycode == Key.Space ? ' ' : (char)key.Keycode,
            0,
            0),

        InputEventMouseButton { Pressed: true } click =>
            ClientInput.Clicked(click.Position.X, click.Position.Y),

        InputEventMouseMotion motion =>
            new ClientInput(ClientInputKind.MouseMoved, ClientKey.Other, '\0', motion.Position.X, motion.Position.Y),

        // Everything else still reaches the router, because the quit confirmation
        // swallows every event while it is up — releases and drags included.
        _ => new ClientInput(ClientInputKind.Other, ClientKey.Other, '\0', 0, 0),
    };

    /// <summary>Everything outside the focus stack the routing decision still reads.</summary>
    private RouteContext Context()
    {
        var commanded = CommandedCombatant();

        return new RouteContext(
            Fighting: _phase == Phase.Fighting,
            ActInProgress: ActInProgress,
            MenuRowCount: OpenMenuLength,
            CanArmAttack: _encounter is { } fight
                && commanded is not null
                && TurnOptions.For(fight, commanded).Contains(TurnAction.Attacks),
            HasCommanded: commanded is not null,
            HasCursor: _cursor is not null,
            Interlude: _phase == Phase.Interlude,
            ShopAvailable: _shopAvailable);
    }

    /// <summary>
    /// Carries out one routed decision. Returns whether the input was consumed.
    /// </summary>
    private bool Perform(Route route)
    {
        switch (route.Action)
        {
            case RouteAction.Unhandled:
                return false;

            case RouteAction.Ignore:
                return true;

            case RouteAction.QuitGame:
                GetTree().Quit();
                return true;

            case RouteAction.DismissQuitConfirm:
                _focus.Pop();
                break;

            case RouteAction.AskToQuit:
                _focus.Push(new PlayFocus.QuitConfirm());
                break;

            case RouteAction.CommitOutcome:
                CompleteAndReport();
                break;

            case RouteAction.DropToBoard:
                ClearPending();
                break;

            case RouteAction.CloseTopLayer:
                _focus.Pop();
                break;

            case RouteAction.CycleTarget:
                CycleTarget();
                return true;

            case RouteAction.ArmAttack:
                // Arming names no attack: the ring is every living enemy, and Enter picks
                // the best attack for whoever it lands on — the same answer a bare click
                // on an enemy has always given.
                ArmTargeting(TargetKind.Attack);
                return true;

            case RouteAction.MoveMenuIndex:
                // Guaranteed a RowMenu: the router only emits this route when
                // context.MenuRowCount > 0 && focus.Top.TakesRowKeys, and TakesRowKeys is
                // true only for RowMenu (PlayFocusRouter.cs).
                (_focus.Top as PlayFocus.RowMenu)?.MoveHighlight(route.StepY, OpenMenuLength);
                break;

            case RouteAction.TakeHighlightedRow:
                TakeHighlightedRow();
                return true;

            case RouteAction.MoveCursor:
                if (CommandedCombatant() is not { } walker)
                {
                    return false;
                }

                var from = _cursor ?? walker.Position;

                _cursor = new GridPosition(
                    Math.Clamp(from.X + route.StepX, 0, GridWidth - 1),
                    Math.Clamp(from.Y + route.StepY, 0, GridHeight - 1));

                break;

            case RouteAction.ActivateSquare:
                if (_cursor is not { } chosen)
                {
                    return false;
                }

                ActivateSquare(chosen);
                return true;

            case RouteAction.RunHotkey:
                if (ActionForKey(route.Character) is not { } action)
                {
                    return false;
                }

                Run(() => Invoke(action));
                return true;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(route), route.Action, "No handler for this route.");
        }

        QueueRedraw();
        return true;
    }

    /// <summary>
    /// A click, translated to a route and performed. The priority order that used to live
    /// here — a second, hand-written copy of the keyboard's own — is gone; only hit-testing
    /// (<see cref="HitTest"/>) and executing the answer (<see cref="PerformClick"/>) remain
    /// (#503, S4).
    /// </summary>
    private void HandleClick(Vector2 pixel) =>
        PerformClick(PlayFocusRouter.RouteClick(_focus, HitTest(pixel), Context()));

    /// <summary>
    /// What one pixel hit — the node's half of the click pipeline. Rect hit-testing stays
    /// here because it is layout, not decision; <see cref="PlayFocusRouter.RouteClick"/>
    /// decides what the hit means <i>and which state makes it count</i>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every field is tested unconditionally</b> (#503, qc review round 1). This method
    /// used to gate which rects it even tried against the pixel on <c>_phase</c>,
    /// <c>Shopping</c> and <c>_focus.Top</c>'s menu type — which meant it had already
    /// resolved "is this the shop, is a menu open, which one" before the router ever ran,
    /// and a menu row that happened to test true always beat a button that also would have,
    /// because the loop that found the row returned before the button loop had a chance to
    /// run at all. Testing every rect regardless of state removes that: two regions that can
    /// both be visually live at once — an open menu's rows and the button strip beneath it —
    /// are both reported, and <see cref="PlayFocusRouter.RouteClick"/> is the only place
    /// that picks between them. A stale rect from a screen that is not currently showing
    /// (the shop's, say, mid-fight) simply produces a fact the router's own
    /// <see cref="RouteContext.Interlude"/>/focus-stack check declines to honour.
    /// </para>
    /// <para>
    /// <see cref="ClickHit.MenuRow"/> reads a single list now (#505): <c>_menuRows</c> holds
    /// rects for whichever menu is actually drawn, and only that one, because
    /// <see cref="ClearMenuRows"/> empties it before each frame's traversal repopulates at
    /// most one menu's worth. Before #505 this tested three separate lists in sequence,
    /// because at most one of them ever held live rectangles at once; the sequence is gone
    /// along with the lists it chose between, not because the priority decision this slice
    /// moves changed, but because there is only one list left to read.
    /// <see cref="MenuRowList.RowAt"/> is deliberately blind to which layer is on top for
    /// the same reason the rest of this method is (Whether a found row may actually be
    /// <i>taken</i> is <see cref="PlayFocusRouter.RouteClick"/>'s call, which resolves
    /// through <see cref="MenuRowList.TryTake"/>); it is not blind to <em>which menu filled
    /// it</em>, which is the ownership check that closes the stale-list window (qc review
    /// round, #505).
    /// </para>
    /// </remarks>
    private ClickHit HitTest(Vector2 pixel)
    {
        var overOverlay = OverOverlay(pixel);

        var shopBack = _shopBackButton.HasPoint(pixel);
        int? shopRow = null;

        for (var index = 0; index < _shopRows.Count; index++)
        {
            if (_shopRows[index].Rect.HasPoint(pixel))
            {
                shopRow = index;
                break;
            }
        }

        var shopOpen = _shopButton.HasPoint(pixel);
        var continueHit = _continueButton.HasPoint(pixel);

        // Unconditionally — no <see cref="_focus"/> branch here. _menuRows is emptied by
        // <see cref="ClearMenuRows"/> at the top of every _Draw, before anything decides
        // whether to repopulate it, so a menu that is not showing contributes no rectangles
        // and this finds nothing for it. Reading focus here would put the last gating
        // decision back on the wrong side of the seam this slice exists to draw: whether a
        // row may be taken is the router's call, which resolves through
        // <see cref="MenuRowList.TryTake"/>'s ownership check — and this method's only job
        // is to say which rectangles the pixel is inside.
        int? menuRow = _menuRows.RowAt(pixel);

        int? button = null;

        for (var index = 0; index < _buttons.Count; index++)
        {
            if (_buttons[index].Rect.HasPoint(pixel))
            {
                button = index;
                break;
            }
        }

        var square = SquareAt(pixel);

        return new ClickHit(shopBack, shopRow, shopOpen, continueHit, menuRow, button, square, overOverlay);
    }

    /// <summary>Carries out one routed click decision.</summary>
    private void PerformClick(Route route)
    {
        switch (route.Action)
        {
            case RouteAction.Ignore:
                return;

            case RouteAction.CloseTopLayer:
                _focus.Pop();
                break;

            case RouteAction.PurchaseShopRow:
                if (_run is { } shopping && route.Index < _shopRows.Count)
                {
                    var offer = _shopRows[route.Index].Offer;

                    // The engine's answer either way: a purchase re-lists the stall
                    // with the purse lighter, a refusal is shown with its code like
                    // every other rule.
                    _focus.ReplaceTop(new PlayFocus.Shop(
                        shopping.Purchase(offer) is { } refusal
                            ? $"[{refusal.Code}] {refusal.Message}"
                            : $"Bought: {offer.Description}."));
                }

                break;

            case RouteAction.OpenShop:
                _focus.Push(new PlayFocus.Shop());
                break;

            case RouteAction.ContinueFight:
                StartNextFight();
                return;

            case RouteAction.TakeMenuRowAt:
                TakeMenuRow(route.Index);
                return;

            case RouteAction.RunButtonRow:
                if (route.Index < _buttons.Count)
                {
                    Run(_buttons[route.Index].Act);
                }

                return;

            case RouteAction.DropToBoard:
                // A click on the grid closes an open menu rather than acting through
                // it. The spell that used to be nulled alongside the flags here rides
                // the SlotMenu layer now, so dropping to the board takes it with it.
                ClearPending();
                break;

            case RouteAction.ActivateSquareAt:
                ActivateSquare(route.Square);
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(route), route.Action, "No click handler for this route.");
        }

        QueueRedraw();
    }

    /// <summary>
    /// Whether a pixel sits on the fixed chrome — the initiative-and-log panel or the
    /// banner strip — where a click means the chrome, never the square underneath it.
    /// </summary>
    private bool OverOverlay(Vector2 pixel) =>
        pixel.X >= PanelLeft - 16 || BottomStrip.HasPoint(pixel);

    /// <summary>
    /// Acts on one square, whether a mouse clicked it or the keyboard's cursor sat on
    /// it and Enter was pressed.
    /// </summary>
    /// <remarks>
    /// <b>One path for both.</b> Arrow keys and a click have to mean exactly the same
    /// thing on the same square, and the surest way to guarantee that is for there to be
    /// only one place that decides. A null square is "nowhere" — off the board, or a
    /// click on the chrome — which backs out of anything armed without spending it.
    /// </remarks>
    private void ActivateSquare(GridPosition? at)
    {
        if (CommandedCombatant() is not { } active || _encounter is not { } encounter)
        {
            return;
        }

        var square = at ?? new GridPosition(-1, -1);

        if (Armed is { Kind: TargetKind.Attack } aimedAttack)
        {
            var chosen = aimedAttack.Attack;
            var struck = TokenAt(square);
            ClearPending();

            // A named attack swings at whatever was clicked and the engine rules on
            // it. Tab's bare arming named no attack, so it keeps the bare click's own
            // semantics whole: enemies only, best attack for this victim.
            if (struck is { } victim && (chosen is not null || victim.SideId != active.SideId))
            {
                Run(() => (chosen ?? AttackChoice.BestFor(active, victim, encounter.Combatants)) is { } attack
                    ? encounter.Attack(attack.Name, victim)
                    : new ActionRefusal("client.no_attack", $"{active.Name} has no attack that reaches {victim.Name}."));
            }
            else
            {
                _notice = null;
                QueueRedraw();
            }

            return;
        }

        if (Armed is { Kind: TargetKind.Spell, Spell: { } spell } aimedSpell)
        {
            var aimed = TokenAt(square);
            var ground = square;
            var slot = aimedSpell.Slot;
            ClearPending();

            if (aimed is { } target)
            {
                Run(() => encounter.CastSpell(spell.Id, target, slot));
            }
            else if (spell.Save?.Area is not null && ground is { } spot)
            {
                // An area spell aimed at bare ground: the engine's point overload
                // rules on it — range, shape and who the area catches are all its
                // answers, not this client's.
                Run(() => encounter.CastSpell(spell.Id, spot, target: null, slot));
            }
            else
            {
                _notice = null;
                QueueRedraw();
            }

            return;
        }

        if (Armed is { Kind: TargetKind.Potion })
        {
            var aimed = TokenAt(square);
            ClearPending();

            // The target's own flask first, the actor's pack second — the same order
            // the engine spends them in, so the potency named is one that exists.
            if (aimed is { } target
                && (target.Inventory.Weakest ?? active.Inventory.Weakest) is { } potency)
            {
                Run(() => encounter.DrinkPotion(potency, target));
            }
            else
            {
                _notice = null;
                QueueRedraw();
            }

            return;
        }

        if (Armed is { Kind: TargetKind.Trade, Potency: { } tradePotency })
        {
            var aimed = TokenAt(square);
            ClearPending();

            if (aimed is { } target)
            {
                Run(() => encounter.TradeItem(new CombatTradeItem.Potion(tradePotency), target));
            }
            else
            {
                _notice = null;
                QueueRedraw();
            }

            return;
        }

        if (Armed is { Kind: TargetKind.SparkHeal or TargetKind.SparkHarm } spark)
        {
            var aimed = TokenAt(square);
            var mode = spark.Kind == TargetKind.SparkHeal ? DivineSparkUse.Heal : DivineSparkUse.Harm;
            ClearPending();

            if (aimed is { } target)
            {
                // Radiant by default when harming; the console command is where the
                // Necrotic choice lives, the two types being identical to every
                // creature the resolver cannot tell apart.
                Run(() => encounter.DivineSpark(target, mode));
            }
            else
            {
                _notice = null;
                QueueRedraw();
            }

            return;
        }

        var occupant = TokenAt(square);

        if (occupant is { } somebody && somebody.SideId != PregeneratedParty.SideId)
        {
            Run(() => AttackChoice.BestFor(active, somebody, encounter.Combatants) is { } attack
                ? encounter.Attack(attack.Name, somebody)
                : new ActionRefusal("client.no_attack", $"{active.Name} has no attack that reaches {somebody.Name}."));
        }
        else if (at is not null
            && (occupant is null || occupant.HasCondition(ConditionType.Incapacitated)))
        {
            // Sent to the engine whether or not it is highlighted: the refusal is the
            // rule, the highlight only advice. A square holding a downed comrade is a
            // destination too — the engine's house rule lets a move end on a fallen
            // ally, and swallowing that click here left the rule unreachable from the
            // board: the reachable highlight lit the square and the click did nothing.
            Run(() => encounter.Move(square));
        }
    }

    /// <summary>
    /// The living combatant standing on a square, whichever side it is on — except a
    /// monster the fog hides, which the client's conveniences treat as absent: a click
    /// into the shadow reads as a move, and the engine's refusal of that move is what
    /// bumping into something unseen feels like.
    /// </summary>
    private Combatant? TokenAt(GridPosition square) =>
        _encounter?.Combatants.FirstOrDefault(combatant =>
            !combatant.IsDead
            && combatant.Position == square
            && (combatant.SideId == PregeneratedParty.SideId || !_unseen.Contains(square)));

    /// <summary>The living combatant under a pixel, whichever side it is on.</summary>
    /// <remarks>
    /// Deliberately unfiltered: a heal aimed at an enemy or a potion poured at a range
    /// is the engine's to allow or refuse, and its answer teaches the rule.
    /// </remarks>
    private Combatant? TokenTarget(Vector2 pixel) =>
        SquareAt(pixel) is { } square && _encounter is { } encounter
            ? encounter.Combatants.FirstOrDefault(combatant =>
                !combatant.IsDead
                && combatant.Position == square
                && (combatant.SideId == PregeneratedParty.SideId || !_unseen.Contains(square)))
            : null;

    private void Run(Func<ActionRefusal?> act)
    {
        var refusal = act();
        RefreshAfterAction(refusal);
    }

    private void RefreshAfterAction(ActionRefusal? refusal)
    {
        _notice = refusal is null ? null : $"{refusal.Message}  [{refusal.Code}]";
        _elapsed = 0;
        _reachable.Clear();
        _unseen.Clear();

        // Whatever just happened, the board plays it out: each Move step's walk — the
        // step carries the route, so the token glides it instead of teleporting — and
        // each attack's swing, in log order.
        if (_encounter is { } fought)
        {
            if (_animateWalks)
            {
                QueueActs(fought.Log, _walkStepsSeen, fought.Log.Count, TokensFrom(fought, _labels));
            }

            _walkStepsSeen = fought.Log.Count;
        }

        var commanded = CommandedCombatant();

        if (commanded is null || commanded.Id != _buttonsFor)
        {
            ClearPending();

            // A new character's turn starts the cursor on them rather than wherever the
            // last one left it, so the first arrow key moves somewhere meaningful.
            _cursor = commanded?.Position;
        }

        // Rebuilt after every action, not just on a change of character: the row now
        // shows only what can be used, and spending the Action is exactly what takes
        // Dodge and Dash out of it.
        if (commanded is not null)
        {
            BuildButtons(commanded);
        }

        // Where the active party member could walk. FindPath is the engine's own
        // reachability — allies cost double, enemies block, the budget is what is left
        // this turn — and the two condition gates mirror Move's early refusals so the
        // advice does not light squares the engine would refuse.
        if (commanded is { } mover
            && _encounter is { } encounter
            && !mover.HasCondition(ConditionType.Prone)
            && ConditionRules.ImmobilisedBy(mover) is null)
        {
            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    var square = new GridPosition(x, y);

                    if (MovementRules.FindPath(
                            encounter.Battlefield, mover, square, mover.Turn.MovementFeet, encounter.Combatants)
                        is not null)
                    {
                        _reachable.Add(square);
                    }
                }
            }
        }

        // The fog of war: squares nobody in the party can see (asked for from play,
        // 2026-08-21, replacing the acting character's Total Cover shade). PartyVision
        // in Game is the judgement — walls block, sight is the side's union, closed
        // eyes count for nothing — and this screen only draws its answer, and hides
        // what stands inside it, because a monster no one can see is not the player's
        // to know about. Party-wide rather than per-actor, so the fog holds still
        // through everyone's turns instead of jumping with the initiative.
        if (_encounter is { } looked)
        {
            var visible = PartyVision.VisibleSquares(
                looked.Battlefield,
                looked.Combatants,
                PregeneratedParty.SideId);

            foreach (var square in looked.Battlefield.AllSquares())
            {
                if (!visible.Contains(square))
                {
                    _unseen.Add(square);
                }
            }

            _fogTexture = BuildFogTexture(looked.Battlefield);
        }

        QueueRedraw();
    }
}
