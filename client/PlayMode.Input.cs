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

        // Arming is every path's own last step — Tab's cold-arm, a button, a menu row
        // — so this is the one place that closes #303 defect #4 (PR #731 round 1
        // review) for all of them at once: the preview's own gate (Armed is null)
        // only helps once something re-asks it, and a click that arms Targeting is
        // not itself a mouse motion or a completed action.
        UpdatePreviewPath(_pointer);
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
    /// The stall's own wheel scroll (#704) — one row per notch. Kept out of
    /// <see cref="PlayFocusRouter"/> because a wheel notch is never a
    /// <see cref="ClientKey"/>; see <see cref="_UnhandledInput"/>'s remarks.
    /// </summary>
    private bool HandleShopWheelInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                ScrollShop(-1);
                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                ScrollShop(1);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Moves the stall's scroll window by <paramref name="rows"/> offers, clamped to
    /// however many are on sale this visit. Recomputes the offer list rather than caching
    /// it — <see cref="DrawShop"/> already does the same on every frame the stall is open,
    /// so this stays the one place that count comes from rather than a second copy that
    /// could drift from it.
    /// </summary>
    /// <remarks>
    /// <see cref="ShopLayout.Scroll"/> normalises <c>shop.Offset</c> against the current
    /// offer count before applying <paramref name="rows"/> (Codex review round, #710) — a
    /// purchase can shrink the list out from under a scrolled-down stall, leaving the
    /// stored offset past the end even though <see cref="DrawShop"/>'s own display is
    /// already clamped. Adding the delta to that stale value first (the naive
    /// <c>ClampOffset(shop.Offset + rows, offerCount)</c> this used to be) could compute
    /// the same clamped result twice in a row, making the wheel or Page Up/Down look dead
    /// right after a purchase thinned the list.
    /// </remarks>
    private void ScrollShop(int rows)
    {
        if (Shopping is not { } shop || _run is not { } run)
        {
            return;
        }

        var offerCount = Shop.Offers(_content!, run.Party, run.States).Count;
        var offset = ShopLayout.Scroll(shop.Offset, rows, offerCount);

        if (offset != shop.Offset)
        {
            _focus.ReplaceTop(shop with { Offset = offset });
            QueueRedraw();
        }
    }

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
    /// division of labour as this method's own keyboard half. Four mouse paths stay here
    /// by design rather than by omission: the camera (wheel zoom, middle- or right-drag
    /// pan) is nobody's decision, just settling an input before anything else can misread
    /// it; the shop's own wheel scroll (#704) is the same kind of settling, for the one
    /// screen the camera never reaches; the hover clock only ever clears a tooltip; and
    /// the outcome card's left-click commit, immediately below, is a boundary this method
    /// drew on purpose — it precedes <see cref="HandleClick"/> entirely and is not one of
    /// the click pipeline's nine steps. Folding any of them in would need its own scoped
    /// slice with a left-button-and-ordering characterization test, not a drive-by move.
    /// </para>
    /// <para>
    /// <b>The wheel is a mouse button in Godot</b> (<c>MouseButton.WheelUp</c>/
    /// <c>WheelDown</c>), so <see cref="Translate"/> already turns it into a
    /// <see cref="ClientInputKind.MousePressed"/> <see cref="ClientInput"/> before this
    /// method ever sees the raw event — <see cref="PlayFocusRouter.Route"/> reports
    /// <see cref="RouteAction.Unhandled"/> for it every time, the same as it does for a
    /// click, since <c>input.IsKey</c> is false. Page Up/Down are ordinary keys and *do*
    /// go through the router (see its own remarks on <see cref="RouteAction.ScrollShop"/>);
    /// only the wheel's raw <see cref="InputEventMouseButton"/> is read here, because a
    /// row-at-a-time scroll has no keyboard equivalent to share a <see cref="ClientKey"/>
    /// with.
    /// </para>
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (Perform(PlayFocusRouter.Route(_focus, Translate(@event), Context())))
        {
            // Any successfully routed keyboard action can change whether the preview
            // should show at all — arming an attack with a cold Tab, or Esc handing it
            // back — without ever moving the mouse (#303 defect #4, PR #731 round 1
            // review). UpdatePreviewPath's own gate (Board focus, nothing armed) is
            // only as good as the last time it ran, so this makes every routed action
            // a trigger for it too, not only mouse motion and completed acts.
            UpdatePreviewPath(_pointer);
            QueueRedraw();
            return;
        }

        // The stall's own wheel scroll (#704): one row per notch, regardless of phase —
        // unlike the camera below, the shop is only ever open outside a fight, so gating
        // this on Phase.Fighting the way the camera is would leave the wheel exactly as
        // dead here as the bug report found it.
        if (Shopping is not null && HandleShopWheelInput(@event))
        {
            return;
        }

        // The camera's inputs — wheel zoom, middle- or right-drag pan — are nobody
        // else's, so they are settled before the hover clock or a click can misread
        // them.
        if (_phase == Phase.Fighting && HandleCameraInput(@event))
        {
            // HandleCameraInput consumes its event whole and returns before the plain
            // motion branch below ever sees it (#303 defect #3, PR #731 round 1
            // review): a drag's own InputEventMouseMotion never reaches that branch at
            // all, and a wheel zoom carries no motion event to reach in the first
            // place — yet both can change what GridLeft/GridTop/CellPixels answer for
            // the very same screen pixel. So this updates the tracked pointer (a
            // drag's final position; a zoom leaves it where it was, which still needs
            // re-mapping against the new scale) and refreshes the preview
            // unconditionally, rather than only on the motion this branch stole.
            if (@event is InputEventMouseMotion cameraMotion)
            {
                _pointer = cameraMotion.Position;
            }

            UpdatePreviewPath(_pointer);
            QueueRedraw();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            var redraw = false;

            // _pointer always adopts the motion's own position (#303 defect, PR #731
            // round 2 review): a refresh that reads it later — a routed keyboard
            // action, a camera change, RefreshAfterAction — must see wherever the
            // pointer actually is, not a jitter-filtered pixel that could still be
            // sitting one grid square behind. TrackedPointer is the whole of that
            // contract, extracted so a future "optimisation" gating it by distance
            // again has a named seam to break.
            _pointer = TrackedPointer(_pointer, motion.Position);

            // The hint clock's own rest detection is unrelated: only real movement
            // restarts it, since Godot reports motion for sub-pixel drift too, and a
            // hand resting on a button is never perfectly still. _hintAnchor is its
            // own last-considered-settled pixel, kept separate from _pointer since
            // round 3 — the two used to be the same field, which is how defect #1
            // happened.
            if (_hintAnchor.DistanceTo(motion.Position) > HoverJitterPixels)
            {
                _hintAnchor = motion.Position;
                _hoverElapsed = 0;

                if (_hint is not null)
                {
                    _hint = null;
                    redraw = true;
                }
            }

            // The path preview tracks the pointer's *square*, never its pixel distance
            // (#303 defect #2, PR #731 round 1 review): HoverJitterPixels exists only
            // to stop the tooltip flickering on sub-pixel drift, which has nothing to
            // do with when a route should change — a pointer one pixel from a grid
            // line can cross it in a two-pixel move, well under that threshold, and be
            // looking at a different walk the instant it happens. So this asks
            // PreviewSquareChanged on every raw motion sample, independent of the
            // jitter-gated branch above, and it is deliberately not gated behind
            // HoverDelaySeconds either: the reachable wash the preview sits inside
            // already lights up with no delay at all, and a route is advice about the
            // very same click a hint only explains in words.
            if (PreviewSquareChanged(_previewSquare, SquareAt(motion.Position)))
            {
                UpdatePreviewPath(motion.Position);
                redraw = true;
            }

            if (redraw)
            {
                QueueRedraw();
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

    /// <summary>
    /// How long the pointer must rest before a hint appears, in seconds (#304). Was 2 —
    /// "an eternity mid-fight" per the review that filed the issue — dropped to about
    /// half a second. This is a rest-detection threshold, not a cost budget: the hover
    /// path calls <see cref="HintAt"/>, which reads a button's hint or an occupant's
    /// line and recomputes nothing on the board, so the delay was never covering for
    /// work (#726/#728's <see cref="RefreshAfterAction"/> speed-up is unrelated to it).
    /// The terrain-vocabulary half of #304 — what an empty square's hint says — is a
    /// separate, later slice; this constant is the whole of this one.
    /// </summary>
    private const double HoverDelaySeconds = 0.5;

    /// <summary>How far the pointer may drift and still count as resting.</summary>
    private const float HoverJitterPixels = 3;

    /// <summary>
    /// Whether <paramref name="hoverElapsedSeconds"/> of rest is enough to raise a hint
    /// (#304). Extracted from <see cref="AdvanceHover"/> so the threshold itself — not
    /// merely that some delay exists — is pinned by <c>SRDCombat.Viewer.Tests</c>
    /// without a live Godot node.
    /// </summary>
    internal static bool HoverDelayElapsed(double hoverElapsedSeconds) =>
        hoverElapsedSeconds >= HoverDelaySeconds;

    /// <summary>
    /// Counts the pointer's rest and raises a hint once it has been still long enough.
    /// </summary>
    private void AdvanceHover(double delta)
    {
        if (HoverDelayElapsed(_hoverElapsed))
        {
            // Already asked and answered. The hint is *not* re-read every frame: it is
            // taken once when the pause completes, so it cannot flicker as the fight
            // changes underneath a motionless pointer.
            return;
        }

        _hoverElapsed += delta;

        if (!HoverDelayElapsed(_hoverElapsed))
        {
            return;
        }

        _hint = HintAt(_hintAnchor);

        if (_hint is not null)
        {
            QueueRedraw();
        }
    }

    /// <summary>
    /// Recomputes <see cref="_previewPath"/> for whatever square <paramref
    /// name="pixel"/> maps to right now, against the board's current state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from every place that can change either <em>where</em> the preview
    /// should point or <em>whether</em> it should show at all (PR #731 round 1
    /// review, defects #2-#4): the jitter-independent motion branch and the
    /// camera-consumed branch of <see cref="_UnhandledInput"/>, every successfully
    /// routed keyboard action (arming or disarming Targeting can change the answer
    /// with the mouse dead still), <see cref="ArmTargeting"/> itself (a click on a
    /// button or a menu row arms the same way), the camera's own automatic glide in
    /// <c>PlayMode._Process</c>, and <see cref="RefreshAfterAction"/> so a turn change
    /// or a completed move refreshes it immediately rather than leaving the previous
    /// mover's route on screen until the pointer next twitches.
    /// </para>
    /// <para>
    /// Takes the pixel explicitly rather than always reading <see cref="_pointer"/>
    /// directly at the call site: most callers do pass <see cref="_pointer"/> (it
    /// tracks every raw motion sample unconditionally as of round 2's fix — see
    /// <see cref="TrackedPointer"/>), but the plain-motion branch of
    /// <see cref="_UnhandledInput"/> passes the still-in-flight <c>motion.Position</c>
    /// instead, before that assignment happens, so the two can never race.
    /// </para>
    /// <para>
    /// Also checks <see cref="OverOverlay"/> on <paramref name="pixel"/>, via
    /// <see cref="PreviewMayShow"/> (#303 defect, PR #731 round 2 review): a reachable
    /// square can sit under the fixed chrome (the initiative/log panel, the bottom
    /// banner), and a click there means the chrome, never the square underneath it —
    /// the same check <see cref="HitTest"/> already makes for the click path itself,
    /// asked here rather than re-derived, so the two can never disagree about which
    /// pixels are chrome.
    /// </para>
    /// </remarks>
    private void UpdatePreviewPath(Vector2 pixel)
    {
        _previewSquare = SquareAt(pixel);
        _previewPath.Clear();
        _threatenedSteps.Clear();
        _rangeNormal.Clear();
        _rangeLong.Clear();
        _areaCoverage.Clear();
        _areaCaughtIds.Clear();
        _targetingOutOfRange = false;
        _targetingOutOfRangeCode = null;

        if (_phase != Phase.Fighting || _encounter is not { } encounter)
        {
            return;
        }

        // The targeting family takes over whenever an attack or a spell is armed
        // (#302) — the counterpart to the movement family just below, which
        // PreviewMayShow already refuses the moment anything is armed. The two are
        // mutually exclusive by construction: Armed is not null only while
        // PlayFocus.Targeting sits on top, and the movement branch's own gate reads
        // exactly that.
        if (Armed is { Kind: TargetKind.Attack or TargetKind.Spell } armed
            && TargetingPreviewMayShow(armed: true, OverOverlay(pixel))
            && CommandedCombatant() is { } actor)
        {
            UpdateTargetingPreview(encounter, actor, armed);
            return;
        }

        if (!PreviewMayShow(_focus.Top is PlayFocus.Board, Armed is not null, OverOverlay(pixel)))
        {
            return;
        }

        var mover = CommandedCombatant();

        // The full, unfiltered route — shared by _previewPath's own fog-trim below
        // and by ThreatenedSteps, which needs the walk's real adjacency rather than
        // the already-thinned squares (#734 review round 1; see ThreatenedSteps' own
        // remarks). One HoveredPath call rather than HoverPreviewPath's own duplicate
        // of the same FindPath search.
        var hoveredPath = HoveredPath(encounter.Battlefield, mover, _previewSquare, _reachable, encounter.Combatants);

        _previewPath.AddRange(hoveredPath.Where(step => !_unseen.Contains(step)));

        if (mover is not null)
        {
            // Only enemies the party can presently see may mark a threat (#301) — the
            // same standard client/README.md's fog section already holds a hidden
            // occupant's token, ring and hover hint to. Passing every enemy instead
            // would show the player a threat sourced from a monster nobody has seen
            // yet, the #732 leak shape this is deliberately the other side of.
            var visibleEnemies = encounter.Combatants
                .Where(combatant => combatant.SideId != mover.SideId && !_unseen.Contains(combatant.Position))
                .ToList();

            _threatenedSteps.AddRange(
                ThreatenedSteps(encounter.Battlefield, mover, hoveredPath, visibleEnemies, _unseen));
        }
    }

    /// <summary>
    /// Whether the targeting preview (#302) — the range envelope, the area coverage,
    /// the out-of-range mark — may show at all right now: something is actually armed,
    /// and the pointer is not over the fixed chrome. Deliberately independent of which
    /// focus layer is on top the way <see cref="PreviewMayShow"/> is not: <see
    /// cref="Armed"/> being non-null already implies <see cref="PlayFocus.Targeting"/>
    /// is the top layer (arming is the only way onto that layer, and nothing pushes a
    /// further layer over it — see <see cref="PlayFocus.Targeting"/>'s own remarks), so
    /// asking again here would only restate that invariant rather than test anything.
    /// </summary>
    internal static bool TargetingPreviewMayShow(bool armed, bool overOverlay) => armed && !overOverlay;

    /// <summary>
    /// Fills the targeting-preview fields for an armed attack or spell (#302) — the
    /// targeting family's own counterpart to <see cref="UpdatePreviewPath"/>'s movement
    /// branch, called from the exact same place so it recomputes everywhere the route
    /// preview does. Every number drawn from here is the engine's own:
    /// <see cref="SRDCombat.Core.Combat.CombatAttack.CanReach"/> and
    /// <see cref="SRDCombat.Core.Combat.CombatAttack.IsAtLongRange"/> for an attack,
    /// <see cref="SpellDefinition.TargetRangeFeet"/> for a ranged spell, and
    /// <see cref="AreaTargeting.Cover"/> — the identical call
    /// <see cref="Encounter.CastSpell"/> makes internally — for an area. Nothing here
    /// re-derives any of those; this only asks each its own question for the hovered
    /// square and the commanded actor.
    /// </summary>
    private void UpdateTargetingPreview(Encounter encounter, Combatant actor, PlayFocus.Targeting armed)
    {
        if (armed.Kind == TargetKind.Attack)
        {
            var envelope = AttackRangeEnvelope(encounter.Battlefield, actor, armed.Attack);
            _rangeNormal.AddRange(envelope.Normal);
            _rangeLong.AddRange(envelope.Long);

            if (_previewSquare is { } hovered
                && TokenAt(hovered) is { } target
                && target.SideId != actor.SideId)
            {
                var code = AttackOutOfRangeCode(actor.Stats.Attacks, armed.Attack, actor.DistanceFeetTo(target));

                if (code is not null)
                {
                    _targetingOutOfRange = true;
                    _targetingOutOfRangeCode = code;
                }
            }

            return;
        }

        if (armed is not { Kind: TargetKind.Spell, Spell: { } spell })
        {
            return;
        }

        if (spell.Save?.Area is { } area)
        {
            if (_previewSquare is not { } origin)
            {
                return;
            }

            var preview = AreaCoverage(encounter.Battlefield, actor, spell, area, origin, encounter.Combatants, _unseen);
            _areaCoverage.AddRange(preview.Squares);

            foreach (var id in preview.CaughtCombatantIds)
            {
                _areaCaughtIds.Add(id);
            }

            if (preview.OutOfRange)
            {
                _targetingOutOfRange = true;
                _targetingOutOfRangeCode = "spell.out_of_range";
            }

            return;
        }

        _rangeNormal.AddRange(SpellRangeEnvelope(encounter.Battlefield, actor, spell));

        if (_previewSquare is { } hoveredSquare
            && TokenAt(hoveredSquare) is { } spellTarget
            && SpellOutOfRangeCode(spell, actor.DistanceFeetTo(spellTarget)) is { } spellCode)
        {
            _targetingOutOfRange = true;
            _targetingOutOfRangeCode = spellCode;
        }
    }

    /// <summary>
    /// The normal- and long-range bands of an armed attack's envelope (#302): every
    /// square on the battlefield a target could stand in for the click that is about to
    /// happen to actually reach, split the way the attack's own numbers split it —
    /// <see cref="SRDCombat.Core.Combat.CombatAttack.CanReach"/> for the whole envelope,
    /// <see cref="SRDCombat.Core.Combat.CombatAttack.IsAtLongRange"/> for the far band a
    /// ranged attack pays Disadvantage inside.
    /// </summary>
    /// <remarks>
    /// <b>Tab's cold arm names no weapon, and the reading here is the same "generous"
    /// one <see cref="TargetChoice"/> already states for exactly that case</b>: with no
    /// attack chosen, a click still swings whichever carried attack reaches
    /// (<c>AttackChoice.BestFor</c>), so the envelope shown is the union of every
    /// carried attack's own reach — normal band only, since combining several weapons'
    /// own long-range bands into one picture would show a Disadvantage warning that
    /// might belong to a weapon the click never ends up using.
    /// </remarks>
    internal static (IReadOnlyCollection<GridPosition> Normal, IReadOnlyCollection<GridPosition> Long)
        AttackRangeEnvelope(Battlefield field, Combatant actor, CombatAttack? attack)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(actor);

        var normal = new List<GridPosition>();
        var far = new List<GridPosition>();

        foreach (var square in field.AllSquares())
        {
            var distance = actor.DistanceFeetTo(square);

            if (attack is not null)
            {
                if (attack.IsAtLongRange(distance))
                {
                    far.Add(square);
                }
                else if (attack.CanReach(distance))
                {
                    normal.Add(square);
                }
            }
            else if (actor.Stats.Attacks.Any(candidate => candidate.CanReach(distance)))
            {
                normal.Add(square);
            }
        }

        return (normal, far);
    }

    /// <summary>
    /// The refusal code a click on <paramref name="target"/> would produce for an armed
    /// attack right now, at <paramref name="distanceFeet"/> — or null when the click
    /// would reach. Mirrors <see cref="ActivateSquare"/>'s own two paths exactly, so the
    /// code shown before the click is never invented: a chosen weapon (<paramref
    /// name="chosen"/> non-null) refuses through <c>Encounter.Attack</c>'s own
    /// <c>attack.out_of_range</c>; Tab's cold arm has no weapon to hand the engine at
    /// all, so a victim nothing in <paramref name="carried"/> reaches falls through to
    /// this screen's own <c>client.no_attack</c> fallback instead.
    /// </summary>
    internal static string? AttackOutOfRangeCode(
        IReadOnlyList<CombatAttack> carried, CombatAttack? chosen, int distanceFeet)
    {
        ArgumentNullException.ThrowIfNull(carried);

        if (chosen is not null)
        {
            return chosen.CanReach(distanceFeet) ? null : "attack.out_of_range";
        }

        return carried.Any(candidate => candidate.CanReach(distanceFeet)) ? null : "client.no_attack";
    }

    /// <summary>
    /// The range envelope of a non-area spell (#302) — every square within
    /// <see cref="SpellDefinition.TargetRangeFeet"/>, the same number
    /// <see cref="Encounter.CastSpell"/> checks a target against. Empty for a
    /// self-ranged spell (nothing to aim at but the caster) and for one with no
    /// printed distance at all — Touch already reads as five feet inside
    /// <see cref="SpellDefinition.TargetRangeFeet"/> itself, so only Sight and
    /// Unlimited reach here, and a wash covering the whole board would say nothing a
    /// player does not already know from the spell's own printed range text.
    /// </summary>
    internal static IReadOnlyCollection<GridPosition> SpellRangeEnvelope(
        Battlefield field, Combatant actor, SpellDefinition spell)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(spell);

        if (spell.IsSelfRanged || spell.TargetRangeFeet is not { } range)
        {
            return [];
        }

        return field.AllSquares().Where(square => actor.DistanceFeetTo(square) <= range).ToArray();
    }

    /// <summary>
    /// The refusal code a click aimed at a creature <paramref name="distanceFeet"/> away
    /// would produce for an armed spell right now, or null when the click would reach —
    /// <see cref="Encounter.CastSpell"/>'s own <c>spell.out_of_range</c>, the one code
    /// its range check ever returns, for both the creature-aimed and the point-aimed
    /// overload. A self-ranged spell has no range to be out of.
    /// </summary>
    internal static string? SpellOutOfRangeCode(SpellDefinition spell, int distanceFeet)
    {
        ArgumentNullException.ThrowIfNull(spell);

        return !spell.IsSelfRanged && spell.TargetRangeFeet is { } range && distanceFeet > range
            ? "spell.out_of_range"
            : null;
    }

    /// <summary>
    /// What an armed area spell would cover for the hovered origin, and who it would
    /// actually catch (#302) — <see cref="AreaTargeting.Cover"/> called with exactly the
    /// arguments <c>Encounter.SaveVictims</c> passes it (the caster's own square as the
    /// origin, the hovered square as the aim point), so the drawn coverage can never
    /// diverge from what the real cast would resolve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Out of range short-circuits before the geometry is even asked</b>, the same
    /// order <see cref="Encounter.CastSpell"/>'s own point-aimed check runs in: a
    /// self-ranged area (an Emanation centred on the caster, or a Cone/Line whose range
    /// is "Self") has nothing to be out of range of, so only a spell with a real
    /// <see cref="SpellDefinition.TargetRangeFeet"/> is checked at all.
    /// </para>
    /// <para>
    /// <b>Fog trims the drawn squares the same way <see cref="_previewPath"/> is
    /// trimmed</b> (#732): a square the party cannot presently see is dropped from what
    /// is drawn, never from what <see cref="AreaTargeting.Cover"/> is asked — clipping
    /// the query itself would make the preview lie about where the real cast's area
    /// actually lands the instant the fog cleared. <b>A caught creature is reported only
    /// when it is both inside the covered squares and not itself hidden</b> — the same
    /// standard a hidden occupant's token, ring and hover hint are already held to — so
    /// a Sphere dropped over fogged ground never announces a monster standing in it
    /// before the fog itself would.
    /// </para>
    /// </remarks>
    internal static AreaCoveragePreview AreaCoverage(
        Battlefield field,
        Combatant caster,
        SpellDefinition spell,
        EffectArea area,
        GridPosition hoveredOrigin,
        IReadOnlyCollection<Combatant> combatants,
        IReadOnlySet<GridPosition> unseen)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(unseen);

        if (SpellOutOfRangeCode(spell, caster.DistanceFeetTo(hoveredOrigin)) is not null)
        {
            return new AreaCoveragePreview([], [], OutOfRange: true);
        }

        var squares = AreaTargeting.Cover(area, caster.Position, hoveredOrigin, field)
            .Where(square => !unseen.Contains(square))
            .ToArray();

        var covered = squares.ToHashSet();

        // No separate "is this combatant's own square unseen" guard: covered has
        // already dropped every unseen square from the raw AreaTargeting.Cover answer
        // above, so a combatant whose occupied square is fogged can never overlap
        // covered at all — checking the combatant's own position again here would be
        // redundant for a single-square creature and, for a multi-square one straddling
        // the fog boundary, actively wrong (an anchor-only check could hide a creature
        // one of whose *other* occupied squares genuinely is in visible, covered
        // ground). Mirrors Encounter.CreaturesIn's own shape exactly, fog folded into
        // the squares it is handed rather than re-asked of the creature.
        var caught = combatants.Where(combatant =>
            combatant.IsActive && combatant.Space.Squares().Any(covered.Contains));

        var reached = area.EnemiesOnly
            ? caught.Where(combatant => combatant.SideId != caster.SideId)
            : caught;

        return new AreaCoveragePreview(squares, reached.Select(combatant => combatant.Id).ToArray(), OutOfRange: false);
    }

    /// <summary>
    /// What an armed area spell's coverage looks like for one hovered origin (#302): the
    /// exact squares, who it would catch (visible creatures only), and whether the
    /// origin itself is out of the spell's own range — see <see cref="AreaCoverage"/>.
    /// </summary>
    internal readonly record struct AreaCoveragePreview(
        IReadOnlyCollection<GridPosition> Squares,
        IReadOnlyCollection<string> CaughtCombatantIds,
        bool OutOfRange);

    /// <summary>
    /// Whether the path preview must recompute for a pointer that was over <paramref
    /// name="previousSquare"/> and is now over <paramref name="newSquare"/> —
    /// deliberately independent of any pixel distance (#303 defect #2, PR #731 round 1
    /// review). <see cref="HoverJitterPixels"/> exists only to stop the tooltip
    /// flickering on sub-pixel drift; a route's destination is a grid square, not a
    /// pixel, and a pointer one pixel from a grid line can cross it in a two-pixel
    /// move — well under that threshold — and be looking at a different walk the
    /// instant it happens. So this compares squares, never distance, and the jitter
    /// gate in <see cref="_UnhandledInput"/> governs the hint clock alone.
    /// </summary>
    internal static bool PreviewSquareChanged(GridPosition? previousSquare, GridPosition? newSquare) =>
        previousSquare != newSquare;

    /// <summary>
    /// What <see cref="_pointer"/> becomes after a raw motion sample lands at
    /// <paramref name="motionPosition"/> — always that position, regardless of
    /// <paramref name="previousPointer"/> or how close the two are (#303 defect, PR
    /// #731 round 2 review). Kept as a two-argument seam rather than a bare identity
    /// function so a future "optimisation" that reintroduces a jitter gate here —
    /// exactly the bug this fixes — has a named test to break rather than a silent
    /// inline edit: every refresh that reads <see cref="_pointer"/> later (a routed
    /// keyboard action, a camera change, <see cref="RefreshAfterAction"/>) must see
    /// wherever the pointer actually is, never a pixel that lagged behind an
    /// in-square move the tooltip's own <see cref="HoverJitterPixels"/> was only ever
    /// meant to keep the *hint* from flickering over.
    /// </summary>
    internal static Vector2 TrackedPointer(Vector2 previousPointer, Vector2 motionPosition) =>
        motionPosition;

    /// <summary>
    /// Whether the path preview may show at all right now — only while the click
    /// under the pointer would actually be a move: the board itself sits on top of
    /// the focus stack, nothing is armed, and the pointer is not over the fixed
    /// chrome. With an attack, a spell, a potion or similar armed (Tab's cold-arm, a
    /// button, a menu row — <see cref="ArmTargeting"/> is every one of their last
    /// steps), or a menu open over the board, a click on reachable-looking ground
    /// clears targeting or resolves the menu instead of walking there (#303 defect
    /// #4, PR #731 round 1 review) — and over the initiative/log panel or the bottom
    /// banner strip, a click means the chrome, never the square underneath it, the
    /// same <see cref="OverOverlay"/> the click path (<see cref="HitTest"/> via
    /// <see cref="PlayFocusRouter.RouteClick"/>) already asks (#303 defect, PR #731
    /// round 2 review) — so a reachable square happening to sit under the panel must
    /// not light a route for a click that lands on the chrome instead.
    /// </summary>
    internal static bool PreviewMayShow(bool focusIsBoard, bool armed, bool overOverlay) =>
        focusIsBoard && !armed && !overOverlay;

    /// <summary>
    /// The path a click on <paramref name="hovered"/> would actually walk right now
    /// (#303) — <see cref="MovementRules.FindPath"/>'s own answer, asked with exactly the
    /// arguments <see cref="Encounter.Move"/> passes it internally (the mover's own
    /// remaining <see cref="TurnResources.MovementFeet"/>, the encounter's own
    /// combatants), so the drawn route can never diverge from the one a click on that
    /// square would produce. This is the only place the client asks <c>FindPath</c> for
    /// a route to one destination — <see cref="_reachable"/> (<see
    /// cref="MovementRules.Reachable"/>) answers "which squares", this answers "and by
    /// what way", and neither re-derives the other's search.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty for three reasons, each an acceptance case of its own: <paramref
    /// name="mover"/> is null (nobody commanded), <paramref name="hovered"/> is null (the
    /// pointer is off the board or nowhere in particular), or the square is not in
    /// <paramref name="reachable"/> — <see cref="MovementRules.Reachable"/>'s own set for
    /// this mover and budget, so anything missing from it is a square <c>FindPath</c>
    /// would refuse anyway (<see cref="MovementRules.Reachable"/>'s remarks on the two
    /// always agreeing); checking it first only saves the search.
    /// </para>
    /// <para>
    /// <b>Fog holds by filtering what is drawn, not by asking a different question —
    /// and that is a narrower claim than it first sounds</b> (qualified after Codex's
    /// PR #731 round 1 review). The route itself is asked for exactly as <see
    /// cref="Encounter.Move"/> would ask for it — clipping the search to what is
    /// currently seen would make the preview lie about where the real click actually
    /// lands — and a square the party cannot presently see is dropped from what comes
    /// back, to the same standard <c>client/README.md</c>'s "fog of war" already holds
    /// a hidden occupant's token, ring and hover hint to: a route's picture never shows
    /// more ground than the fog already would. What this does <em>not</em> do is make
    /// the route itself fog-blind: <see cref="MovementRules.FindPath"/> is asked with
    /// full knowledge of every combatant, seen or not, exactly as <see
    /// cref="_reachable"/> (<see cref="MovementRules.Reachable"/>) already is — so an
    /// unseen occupant blocking the cheaper corridor can still make the offered route
    /// go the other way, which is a route's *shape* telling the player something the
    /// fog itself would not. Not a regression this slice introduces — the reachable
    /// wash has always been computed this way — and not fixed here; see
    /// <c>client/README.md</c>'s own qualification of the same claim.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<GridPosition> HoverPreviewPath(
        Battlefield field,
        Combatant? mover,
        GridPosition? hovered,
        IReadOnlyCollection<GridPosition> reachable,
        IReadOnlyCollection<Combatant> combatants,
        IReadOnlySet<GridPosition> unseen)
    {
        ArgumentNullException.ThrowIfNull(unseen);

        return HoveredPath(field, mover, hovered, reachable, combatants)
            .Where(step => !unseen.Contains(step))
            .ToList();
    }

    /// <summary>
    /// The engine's own route to <paramref name="hovered"/>, before fog trims what
    /// <see cref="HoverPreviewPath"/> draws from it — <see
    /// cref="MovementRules.FindPath"/>'s answer, full and unfiltered. Shared so <see
    /// cref="ThreatenedSteps"/>'s caller can walk the walk's <em>real</em>
    /// square-to-square adjacency rather than the fog-thinned one <see
    /// cref="HoverPreviewPath"/> draws (#734 review round 1 — see
    /// <see cref="ThreatenedSteps"/>'s own remarks for why that distinction is
    /// load-bearing, not cosmetic).
    /// </summary>
    private static IReadOnlyList<GridPosition> HoveredPath(
        Battlefield field,
        Combatant? mover,
        GridPosition? hovered,
        IReadOnlyCollection<GridPosition> reachable,
        IReadOnlyCollection<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(combatants);

        if (mover is null || hovered is not { } square || !reachable.Contains(square))
        {
            return [];
        }

        return MovementRules.FindPath(field, mover, square, mover.Turn.MovementFeet, combatants)?.Steps ?? [];
    }

    /// <summary>
    /// Which of <paramref name="path"/>'s squares provoke an Opportunity Attack, if
    /// <paramref name="mover"/> walks the path in order from its own actual position
    /// (#301) — <see cref="MovementRules.FindOpportunityAttackers"/>'s own answer for
    /// each step in turn, exactly the sequence <c>Encounter.WalkPath</c> asks it for
    /// (mirrored, for the same reason, by <c>SimpleTacticsPolicy.ProvokedDamageAlong</c>
    /// scoring a candidate move): nothing here re-derives who threatens what or how far
    /// a reach extends, it only supplies the real from/to pair for each step and reads
    /// back which of them <see cref="MovementRules.FindOpportunityAttackers"/> answers
    /// non-empty for. <paramref name="path"/> is <see cref="HoveredPath"/>'s full,
    /// unfiltered route — <b>not</b> the fog-thinned squares <see
    /// cref="_previewPath"/> draws, see the remarks below for why that distinction
    /// matters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fog holds by restricting who may threaten and which square is reported,
    /// never by asking a different question about the walk itself</b> (corrected
    /// #734 review round 1). <paramref name="visibleEnemies"/> is the only source of
    /// attackers this asks about — an enemy the party cannot presently see
    /// contributes no mark — and a step in <paramref name="unseen"/> is dropped from
    /// the *result*, to the same standard <c>client/README.md</c>'s fog section
    /// already holds a hidden occupant's token, ring and hover hint to.
    /// </para>
    /// <para>
    /// <paramref name="path"/> must be the walk's real, full sequence — every square
    /// <see cref="MovementRules.FindPath"/> actually routes through, fog or no fog —
    /// because the trigger is about real adjacency: whether the mover's next square
    /// leaves a reach the previous one was inside. Walking the already-fog-filtered
    /// squares instead (this method's first version) could silently treat two
    /// non-adjacent visible squares as if they bordered each other: mover leaves A
    /// (visible, out of an enemy's reach) for B (fogged, inside that enemy's reach)
    /// and then C (visible, out of reach again) — the real walk provokes leaving B,
    /// but a check that never sees B compares A directly against C and finds no
    /// change of reach at all, marking nothing. Walking the full path and dropping
    /// only the *reported* square keeps the walk's own adjacency correct while still
    /// never drawing a mark past what the preview itself shows.
    /// </para>
    /// <para>
    /// <b>A provoking step is reported as the square entered, not the square left,
    /// though the printed trigger fires in the square being left</b> ("the Opportunity
    /// Attack occurs right before it leaves your reach", per <c>Encounter.WalkPath</c>'s
    /// own remarks — the attack resolves while the mover still stands in <c>from</c>).
    /// This is a deliberate, player-facing choice rather than a misreading of the rule:
    /// the route is drawn as a sequence of squares the click would walk *into* — every
    /// square in <see cref="_previewPath"/> already means "stepping here" — so marking
    /// the square left would put the warning one square *behind* the step that actually
    /// costs the swing, on ground the player may already be reading as cleared. Marking
    /// the square entered keeps every warning on the same axis the route already draws
    /// in: "reaching this square is what leaves the reach behind it".
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<GridPosition> ThreatenedSteps(
        Battlefield field,
        Combatant? mover,
        IReadOnlyList<GridPosition> path,
        IReadOnlyCollection<Combatant> visibleEnemies,
        IReadOnlySet<GridPosition> unseen)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(visibleEnemies);
        ArgumentNullException.ThrowIfNull(unseen);

        // Nobody commanded: nothing to walk from. An empty path needs no separate
        // guard — the loop below simply runs zero times and returns the empty list it
        // started with (#734 review round 1: a path.Count == 0 branch here read as a
        // guard but changed no behaviour either way, so it is not carried forward).
        if (mover is null)
        {
            return [];
        }

        var threatened = new List<GridPosition>();
        var from = mover.Position;

        foreach (var step in path)
        {
            if (!unseen.Contains(step)
                && MovementRules.FindOpportunityAttackers(field, mover, from, step, visibleEnemies).Count > 0)
            {
                threatened.Add(step);
            }

            from = step;
        }

        return threatened;
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
    /// <remarks>
    /// Also recomputes the path preview (#303 defect, PR #731 round 2 review): a
    /// mouse-driven cancel — clicking to abandon an armed target, or clicking outside
    /// an open menu — pops focus back to Board without ever going through
    /// <see cref="_UnhandledInput"/>'s routed-keyboard-action branch that already
    /// covers Tab and Esc, and without any accompanying mouse motion for the plain
    /// motion branch to catch either: the pointer is still resting on whatever square
    /// it was, so <see cref="PreviewSquareChanged"/> would see no change to react to.
    /// The thing that changed is not the square, it is whether a click on it would
    /// move — <see cref="UpdatePreviewPath"/> is unconditional, so calling it here
    /// needs no separate "force" flag, only a call site that was missing one.
    /// </remarks>
    private void ClearPending()
    {
        _focus.PopToRoot();
        UpdatePreviewPath(_pointer);
    }

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
                Key.Pageup => ClientKey.PageUp,
                Key.Pagedown => ClientKey.PageDown,
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

            case RouteAction.ScrollShop:
                // A router-issued scroll is always a page (Page Up/Down) rather than a
                // single row — the wheel's own row-at-a-time step is applied directly by
                // HandleShopWheelInput, which never goes through the router at all (see
                // _UnhandledInput's remarks). _shopVisibleCount is last frame's page
                // size; a fresh stall that has not drawn yet reads zero, so a page is at
                // least one row rather than a no-op.
                ScrollShop(route.StepY * Math.Max(_shopVisibleCount, 1));
                break;

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
                    // every other rule. The scroll window rides along (#704) — a
                    // purchase deep in a long list must not snap the shopper back to
                    // its top to read the answer.
                    _focus.ReplaceTop(new PlayFocus.Shop(
                        shopping.Purchase(offer) is { } refusal
                            ? $"[{refusal.Code}] {refusal.Message}"
                            : $"Bought: {offer.Description}.",
                        Shopping?.Offset ?? 0));
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

        // Every case that falls through to here (CloseTopLayer's own single Pop
        // included — the QuitConfirm dismiss and the shop's Back button both route
        // through it) can land focus back on Board, and ClearPending's own recompute
        // above only covers DropToBoard (#303 defect, PR #731 round 2 review).
        // Redundant with ClearPending's own call for that one case, harmless for the
        // others: PreviewMayShow's own gate keeps this empty wherever it should be.
        UpdatePreviewPath(_pointer);
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

        // Where the active party member could walk. MovementRules.Reachable is the
        // engine's own reachability — allies cost double, enemies block, the budget is
        // what is left this turn — and the two condition gates mirror Move's early
        // refusals so the advice does not light squares the engine would refuse.
        //
        // One bounded search for the whole board (#726/#328). This asked FindPath once
        // per square instead — 504 searches on the 28 × 18 grid the review quoted, 784
        // on the warband board #726 measured — after every action by every combatant,
        // most of them draining the entire frontier only to answer "no".
        // The answer is the same set: Reachable returns exactly the squares FindPath
        // answers non-null for, pinned square-by-square in MovementRulesTests.
        if (commanded is { } mover
            && _encounter is { } encounter
            && !mover.HasCondition(ConditionType.Prone)
            && ConditionRules.ImmobilisedBy(mover) is null)
        {
            _reachable.UnionWith(MovementRules.Reachable(
                encounter.Battlefield, mover, mover.Turn.MovementFeet, encounter.Combatants));
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

        // Recomputed here too, not only on the next real mouse motion (#303): _reachable
        // and _unseen above just changed underneath whatever the pointer happens to be
        // resting on, and a turn ending on a still pointer must not leave the previous
        // mover's route on screen, or worse, this mover's route drawn against the stale
        // reachable set an instant before it was refreshed.
        UpdatePreviewPath(_pointer);

        QueueRedraw();
    }
}
