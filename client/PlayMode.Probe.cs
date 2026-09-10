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

/// <summary>The self-driving probe: replays the real input path and captures what each step produced, for a change to be checked without a person clicking.</summary>
public partial class PlayMode : FightScreen
{
    /// <summary>
    /// Drives the screen through the real input path and captures each result: the
    /// run's opening interlude, then commanded turns — an unavailable action attempted
    /// on purpose, Tab arming and cycling from a cold turn, a hovered move's path
    /// preview, a walk, a swing, a feature,
    /// and when a caster's turn comes, the spell menu and a cast. How a change to this
    /// screen gets checked without a person clicking.
    /// </summary>
    /// <remarks>
    /// <b>Every required step below asserts a predicate before it trusts its own
    /// capture's name</b> (#705, sharpened across #719's review rounds): the focus layer
    /// (<see cref="ProbeExpectation.FocusIs"/>), the refusal code (<see
    /// cref="ProbeExpectation.NoticeCodeIs"/>), a resource the action must have left
    /// alone (<see cref="ProbeExpectation.Unchanged{T}"/>) or must actually have moved
    /// (<see cref="ProbeExpectation.Changed{T}"/>), an observed value that must equal a
    /// specific target (<see cref="ProbeExpectation.EqualsExpected{T}"/>), a count that
    /// must have gone down (<see cref="ProbeExpectation.Decreased"/>), a string that must
    /// be present before it is trusted as someone else's expectation (<see
    /// cref="ProbeExpectation.NonEmpty"/>), or at least one of several conditions holding
    /// (<see cref="ProbeExpectation.AnyOf"/>, for "the log grew or a notice printed" —
    /// neither alone is required, but doing neither is a fault) — or, for coverage a
    /// fight in progress may or may not offer, <see cref="ReportSkip"/>, named at the
    /// branch that could not even be <i>attempted</i> (checked by <see
    /// cref="ButtonOffered"/>, or by an index into a computed list, before acting, never
    /// by whether the click's own effect happened to land — #719's review: the old shape
    /// reported a reachable step's own failure as if the step had been unreachable). A
    /// failed <see cref="ProbeExpectation"/> throws via <see cref="Assert"/>, the same
    /// fault path a crash already takes (<see cref="ProbeFaults"/>): a required step's
    /// postcondition not holding is a fault, never a silently mislabelled PNG.
    /// <c>NoticeCodeIs(null)</c> and <c>FocusIs(Board)</c> alone are not proof a click did
    /// anything — both hold exactly as truly before a no-op click as after it, and
    /// deleting a click's handler entirely can leave the board just as clean — so every
    /// step whose click is expected to succeed also asserts that click's own effect; the
    /// full table is in <c>client/README.md</c>, "The probe".
    /// </remarks>
    private void RunProbeIfAsked()
    {
        if (_probeStarted || ArgumentValue("probe") is not { } directory)
        {
            return;
        }

        _probeStarted = true;
        this.FireAndObserve(RunProbe(directory));
    }

    private async Task RunProbe(string directory)
    {
        // --one-fight fixes the party at level 3 (FightScreen.ResolveFight), which a
        // fresh gauntlet does not reach for many fights — and the Slot menu needs a
        // caster with slots at more than one level for a spell, which no level 1
        // character has. Rather than a second full gauntlet seed, this reuses the
        // mode that already starts higher, with its own short sequence: nothing else
        // in this probe run needs the eight fixed steps below, and the level 1
        // sequence's exact turn order would only be disturbed by threading a search
        // for a level-3-only state through it.
        if (HasArgument("one-fight"))
        {
            await RunSlotMenuProbe(directory);
            GetTree().Quit();
            return;
        }

        if (_run is not null && _phase == Phase.Interlude)
        {
            await CaptureFrame(Path.Combine(directory, "run-0-interlude.png"));
            Click(_continueButton.GetCenter());
        }

        await NextCommandedTurn();
        Assert("play-1-turn-ready", new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)));
        await CaptureFrame(Path.Combine(directory, "play-1-turn-ready.png"));

        // The quit confirm, from a cold board: Esc asks, and anything but a second
        // Esc — a key here — backs out unharmed, so the rest of the probe starts
        // from the same clean turn it always did.
        Press(Key.Escape);
        Assert("play-1b-quit-confirm", new ProbeExpectation.FocusIs(typeof(PlayFocus.QuitConfirm)));
        await CaptureFrame(Path.Combine(directory, "play-1b-quit-confirm.png"));
        Press(Key.Space);

        // Stand Up while not Prone — deliberately, because a probe that only walks the
        // happy path would never notice a notice going missing. #521: TurnOptions only
        // offers Stand Up to a Prone character, so from a character that is not Prone
        // this never finds a button to click, and ClickButton is a documented no-op
        // when nothing matches — confirmed live (#521): no refusal, no notice, the
        // board unchanged. That is exactly what the expectations below pin. This
        // step used to be named play-2-refused and its doc comment claimed a refusal
        // that never actually happened (#705) — renamed so no filename claims more than
        // this asserts; retargeting it onto a refusal the probe can actually reach is
        // #521's open decision, not this one's.
        //
        // NoticeCodeIs(null) and FocusIs(Board) both hold just as truly *before* this
        // click as after it (#719 review) — a genuine no-op would pass them too, which
        // is exactly the shape this step exists to rule out. ActorVitals is the
        // predicate that actually distinguishes "nothing happened" from "something
        // happened and just didn't refuse": the commanded actor's position, hit
        // points, movement, and every resource its turn's economy tracks — Action,
        // Bonus Action, Reaction, and the Attack action's own remaining swings — all
        // unchanged by construction whenever the click found no button at all. Widened
        // at #719's second review from Action alone, which a misrouted click spending
        // only a Bonus Action would have passed undetected.
        var beforeStandUp = ActorVitals(CommandedCombatant());
        ClickButton("Stand Up");
        var afterStandUp = ActorVitals(CommandedCombatant());

        Assert(
            "play-2-stand-up-not-offered",
            new ProbeExpectation.NoticeCodeIs(null),
            new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)),
            new ProbeExpectation.Unchanged<ActorVitalsSnapshot?>(
                "the commanded actor's position, hit points, movement, Action, Bonus Action, "
                    + "Reaction and remaining attacks",
                beforeStandUp,
                afterStandUp));
        await CaptureFrame(Path.Combine(directory, "play-2-stand-up-not-offered.png"));

        await HoverFirstButton();
        await CaptureFrame(Path.Combine(directory, "play-2b-hint.png"));

        // #303 defect #4 (Codex review, PR #731 round 1): arming Attack must clear the
        // path preview even though the mouse never moves between the arm and this
        // check — hovering a reachable square first gives the assertion below
        // something real to lose, rather than an already-empty list a broken gate
        // could pass by accident.
        var previewProbeSquare = _reachable.Count > 0 ? _reachable.First() : (GridPosition?)null;

        if (previewProbeSquare is { } squareToHover)
        {
            var previewPixel = CentreOf(squareToHover);

            GetViewport().PushInput(new InputEventMouseMotion
            {
                Position = previewPixel,
                GlobalPosition = previewPixel,
            });

            Assert(
                "play-2c-tab-armed-preview",
                new ProbeExpectation.NonEmpty("the path preview before arming", PathAsText(_previewPath)));
        }
        else
        {
            ReportSkip(
                directory,
                "play-2c-tab-armed-preview",
                "no reachable square was open this turn to preview before arming");
        }

        // Tab from a cold turn: the first press arms the attack and aims at the
        // nearest enemy, the second walks the ring — then Esc backs out, so the rest
        // of the probe starts from the same clean turn it always did.
        Press(Key.Tab);

        // FocusIs(Targeting) alone does not say *which* action Tab armed (#719, fourth
        // review) — a routing regression that armed a Potion or a spell instead of an
        // attack would still land on the Targeting layer and pass unnoticed.
        // PlayFocusRouter's own documented contract is that a cold Tab arms the Attack
        // specifically.
        Assert(
            "play-2c-tab-armed",
            new ProbeExpectation.FocusIs(typeof(PlayFocus.Targeting)),
            new ProbeExpectation.EqualsExpected<TargetKind?>("what the first Tab armed", Armed?.Kind, TargetKind.Attack));

        // #302: the range envelope the board is drawing right now for the just-armed
        // attack must equal AttackRangeEnvelope's own answer for the exact same actor
        // and weapon (Tab's cold arm names none, so Attack is null here — the
        // generous, every-carried-weapon union AttackChoice.BestFor decides for the
        // click and client/README.md documents) —
        // never a second guess at what the envelope should look like.
        if (CommandedCombatant() is { } rangeActor && _encounter is { } rangeEncounter)
        {
            var expectedEnvelope = PlayMode.AttackRangeEnvelope(rangeEncounter.Battlefield, rangeActor, Armed?.Attack);

            Assert(
                "play-2g-attack-range",
                new ProbeExpectation.NonEmpty("the expected range envelope", PathAsText([.. expectedEnvelope.Normal])),
                new ProbeExpectation.EqualsExpected<string>(
                    "the drawn range envelope's normal band",
                    PathAsText([.. _rangeNormal.OrderBy(s => s.X).ThenBy(s => s.Y)]),
                    PathAsText([.. expectedEnvelope.Normal.OrderBy(s => s.X).ThenBy(s => s.Y)])),
                new ProbeExpectation.EqualsExpected<string>(
                    "the drawn range envelope's long band",
                    PathAsText([.. _rangeLong.OrderBy(s => s.X).ThenBy(s => s.Y)]),
                    PathAsText([.. expectedEnvelope.Long.OrderBy(s => s.X).ThenBy(s => s.Y)])));
            await CaptureFrame(Path.Combine(directory, "play-2g-attack-range.png"));
        }
        else
        {
            ReportSkip(directory, "play-2g-attack-range", "no commanded actor was available right after Tab armed the attack");
        }

        // #303 defect #4: the pointer has not moved since the NonEmpty check above —
        // this is the exact shape Codex's review named, a cold Tab with no mouse
        // motion in between — so a preview still showing here means ArmTargeting's own
        // UpdatePreviewPath call regressed, not that the pointer wandered off.
        if (previewProbeSquare is not null)
        {
            Assert(
                "play-2c-tab-armed-preview",
                new ProbeExpectation.EqualsExpected<string>(
                    "the path preview while Attack is armed",
                    PathAsText(_previewPath),
                    string.Empty));
        }

        var cursorAfterFirstTab = _cursor;
        var visibleEnemyCount = CommandedCombatant() is { } tabActive ? VisibleEnemiesOf(tabActive).Count : 0;

        Press(Key.Tab);

        if (visibleEnemyCount > 1)
        {
            // The second Tab is documented to "walk the ring" — with more than one
            // visible enemy to walk to, the aimed target must actually have moved, or
            // the second press did nothing (#719, fourth review: FocusIs(Targeting)
            // alone cannot tell "cycled" from "did nothing", since a no-op leaves the
            // same layer up).
            Assert(
                "play-2c-tab-armed",
                new ProbeExpectation.Changed<GridPosition?>("the aimed target", cursorAfterFirstTab, _cursor));
        }
        else
        {
            // Only one visible enemy — the ring has nothing else to walk to, and the
            // cursor staying put is correct, not a fault. Optional coverage, marked as
            // such: the probe does not control how many enemies a board offers.
            ReportSkip(directory, "play-2c-tab-cycled", "only one visible enemy — nothing for the second Tab to cycle to");
        }

        await CaptureFrame(Path.Combine(directory, "play-2c-tab-armed.png"));
        Press(Key.Escape);

        // The disarm's own mirror of the check above: the pointer still rests on
        // exactly the square it was hovering before Tab ever armed anything, so the
        // preview reappearing here — with no mouse motion since — is Escape's own
        // route back through PlayFocusRouter.Route triggering the same
        // UpdatePreviewPath call arming did, not a coincidence of the pointer moving.
        if (previewProbeSquare is not null)
        {
            Assert(
                "play-2c-tab-armed-preview",
                new ProbeExpectation.NonEmpty("the path preview after Escape disarmed", PathAsText(_previewPath)));
        }

        // #303 defect (Codex review, PR #731 round 2): the disarm check above proved
        // Esc's own keyboard-routed recompute; this proves the *mouse's* — arming
        // Attack again, then clicking the hovered square itself (nobody stands there,
        // so ActivateSquare's Attack branch takes its ClearPending-and-do-nothing
        // cancel path rather than swinging) must restore the preview too, since
        // ClearPending is the one call site every mouse-driven "back to Board, nothing
        // armed" transition shares.
        if (previewProbeSquare is { } squareToRearm)
        {
            Press(Key.Tab);

            Assert(
                "play-2c-tab-armed-preview",
                new ProbeExpectation.FocusIs(typeof(PlayFocus.Targeting)));

            Click(CentreOf(squareToRearm));

            Assert(
                "play-2c-tab-armed-preview",
                new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)),
                new ProbeExpectation.NonEmpty(
                    "the path preview after a mouse click cancelled Attack targeting",
                    PathAsText(_previewPath)));
        }

        if (CommandedCombatant() is { } active
            && NearestVisibleEnemyOf(active) is { } target)
        {
            if (_reachable.Count > 0)
            {
                var step = _reachable
                    .OrderBy(square => square.DistanceFeetTo(target.Position))
                    .ThenBy(square => square.X).ThenBy(square => square.Y)
                    .First();

                // #303: hovering the very square the click below is about to walk to
                // must preview the exact route that click will take. The expectation is
                // computed with the same MovementRules.FindPath call — and the same
                // arguments — HoverPreviewPath itself makes, never a second guess at
                // what the route should look like: this asserts the plumbing wires the
                // pointer to that call, not that the call is right (MovementRulesTests
                // and HoverPreviewPathTests already pin that).
                var expectedPreview = _encounter is { } encounterForPreview
                    ? MovementRules.FindPath(
                        encounterForPreview.Battlefield,
                        active,
                        step,
                        active.Turn.MovementFeet,
                        encounterForPreview.Combatants)?.Steps ?? []
                    : [];

                GetViewport().PushInput(new InputEventMouseMotion
                {
                    Position = CentreOf(step),
                    GlobalPosition = CentreOf(step),
                });

                Assert(
                    "play-2d-path-preview",
                    new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)),
                    new ProbeExpectation.NonEmpty("the previewed path", PathAsText(expectedPreview)),
                    new ProbeExpectation.EqualsExpected<string>(
                        "the previewed path",
                        PathAsText(_previewPath),
                        PathAsText(expectedPreview)));
                await CaptureFrame(Path.Combine(directory, "play-2d-path-preview.png"));

                // #303 defect #3 (Codex review, PR #731 round 1): a wheel zoom is
                // consumed entirely by HandleCameraInput and carries no motion event at
                // all — so a pointer that never itself moved could sit over a different
                // world square once the zoom rescaled the camera underneath it, with
                // the preview still showing the old square's route. Zoomed at a corner
                // far from the pointer, deliberately: ZoomAt keeps the square under
                // *its own* anchor fixed, so zooming under the pointer itself would
                // prove nothing. Checked here, with _reachable still the whole turn's
                // budget, rather than later once this turn's own move has spent most of
                // it down to nothing: the zoom is undone (the exact inverse factor, same
                // anchor) before the click below, so nothing here is meant to survive
                // into play-3-moved's own capture.
                if (_encounter is { } cameraCheckEncounter)
                {
                    var anchorPixel = CentreOf(step);
                    var zoomAnchor = new Vector2(20, 20);

                    var gridLeftBeforeZoom = GridLeft;

                    GetViewport().PushInput(new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.WheelUp,
                        Pressed = true,
                        Position = zoomAnchor,
                        GlobalPosition = zoomAnchor,
                    });

                    // The zoom must actually have changed the mapping, or the check
                    // below would pass by coincidence (the same square, never
                    // re-mapped) rather than by the fix. ZoomAt mutates GridLeft/
                    // GridTop/CellPixels synchronously, with no frame to wait for.
                    Assert(
                        "play-5b-camera-zoom-preview",
                        new ProbeExpectation.Changed<float>("GridLeft after the zoom", gridLeftBeforeZoom, GridLeft));

                    var squareUnderPointerAfterZoom = SquareAt(anchorPixel);

                    var expectedAfterZoom = squareUnderPointerAfterZoom is { } afterSquare
                        ? MovementRules.FindPath(
                            cameraCheckEncounter.Battlefield,
                            active,
                            afterSquare,
                            active.Turn.MovementFeet,
                            cameraCheckEncounter.Combatants)?.Steps ?? []
                        : [];

                    if (squareUnderPointerAfterZoom is { } distinctSquare
                        && distinctSquare != step
                        && expectedAfterZoom.Count > 0)
                    {
                        // NonEmpty first (#719's own lesson, reused here): a broken
                        // mapping and a broken preview could both independently land on
                        // empty, which EqualsExpected alone would wave through as
                        // agreement. expectedAfterZoom.Count > 0 above already rules
                        // that out for the expectation's own side.
                        Assert(
                            "play-5b-camera-zoom-preview",
                            new ProbeExpectation.EqualsExpected<string>(
                                "the previewed path once the zoom left the pointer's screen position "
                                    + "over a different, still-reachable world square",
                                PathAsText(_previewPath),
                                PathAsText(expectedAfterZoom)));
                    }
                    else
                    {
                        ReportSkip(
                            directory,
                            "play-5b-camera-zoom-preview",
                            "the zoom did not leave the pointer over a different, still-reachable square to preview");
                    }

                    // Undo the zoom — the exact inverse, same anchor — before this
                    // turn's real move click below, so nothing here survives into
                    // play-3-moved's own capture.
                    GetViewport().PushInput(new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.WheelDown,
                        Pressed = true,
                        Position = zoomAnchor,
                        GlobalPosition = zoomAnchor,
                    });

                    // Re-hover the move's own destination so the preview and _pointer
                    // are exactly what they were before this check ran, whatever the
                    // zoom-and-back left CellPixels at.
                    GetViewport().PushInput(new InputEventMouseMotion
                    {
                        Position = anchorPixel,
                        GlobalPosition = anchorPixel,
                    });
                }

                // #301: which reachable square, if any, previews a route that crosses a
                // threatened step. TryCaptureThreatPreview does its own independent
                // search (never the live _threatenedSteps field — see its own doc
                // comment, #734 review round 1) and, if seed 1's opening turn has
                // nothing to find, tries again on every later commanded turn in the
                // fight-1 play-out loop below, where melee contact actually happens;
                // the one skip this step can report is written once, after that loop,
                // not here.
                if (_encounter is { } threatEncounter)
                {
                    await TryCaptureThreatPreview(directory, threatEncounter, active);
                }

                // Re-hover the move's own destination before the click below, so
                // nothing from the threat check above survives into play-3-moved's
                // own capture.
                GetViewport().PushInput(new InputEventMouseMotion
                {
                    Position = CentreOf(step),
                    GlobalPosition = CentreOf(step),
                });

                var movementBeforeStep = active.Turn.MovementFeet;

                Click(CentreOf(step));

                // _reachable is the engine's own list of legal destinations (that is
                // what earns a square the highlight), so a click onto one of them is
                // expected to succeed outright, not merely to leave the board.
                // NoticeCodeIs(null) and FocusIs(Board) both already held before this
                // click (#719 review), so they alone would not notice a no-op; the
                // actor actually landing on the clicked square, with less movement left
                // than it had, is the click's own effect.
                Assert(
                    "play-3-moved",
                    new ProbeExpectation.NoticeCodeIs(null),
                    new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)),
                    new ProbeExpectation.EqualsExpected<GridPosition>("the actor's position", active.Position, step),
                    new ProbeExpectation.Decreased("the actor's movement remaining", movementBeforeStep, active.Turn.MovementFeet));
                await CaptureFrame(Path.Combine(directory, "play-3-moved.png"));
            }
            else
            {
                ReportSkip(directory, "play-3-moved", "no reachable square was open for this turn's move step");
            }

            var logCountBeforeAttack = _encounter?.Log.Count ?? 0;

            // #719, fourth review: NearestEnemyOf used to pick the geometrically
            // nearest enemy regardless of fog — a fog-hidden "nearest enemy" makes
            // TokenAt treat the clicked square as empty, and the click below becomes a
            // MOVE instead of an attack, whose own log entry satisfied the old "any log
            // growth" check just as well as a real attack would have.
            // NearestVisibleEnemyOf only ever offers a target the party can see; this
            // assertion is the independent check that TokenAt — the exact function the
            // real click handler consults — agrees, before the click that a mismatch
            // could exploit is ever sent.
            Assert(
                "play-4-attacked",
                new ProbeExpectation.EqualsExpected<Combatant?>("the token at the target's square", TokenAt(target.Position), target));

            Click(CentreOf(target.Position));

            // The attack itself can still legitimately refuse (out of reach after a
            // short move, Total Cover, and so on) — refusals included is documented,
            // working-as-intended coverage here, the same as the feature click below.
            // FocusIs(Board) alone is not evidence anything happened (#719, second
            // review): deleting the click's own handler entirely would still leave the
            // board uncovered. Nor is "the log grew" alone evidence this action
            // happened (#719, fourth review): an unrelated action — the move above,
            // say — would satisfy it too. The evidence below is attributed to this
            // attack specifically: a log entry naming both the actor and the target, or
            // a refusal from Attack's own curated code set.
            Assert(
                "play-4-attacked",
                new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)),
                new ProbeExpectation.AnyOf(
                    $"evidence the attack on {target.Name} resolved or was refused",
                    [
                        new ProbeExpectation.EqualsExpected<bool>(
                            $"a log entry naming {active.Name} and {target.Name}",
                            LogGrewNaming(logCountBeforeAttack, active.Name, target.Name),
                            true),
                        new ProbeExpectation.NoticeCodeIsOneOf(AttackRefusalCodes),
                    ]));

            // #299: the one place this can be checked live. xUnit cannot construct a
            // live `Token` at all (#490/#190 — a Node-derived scene, not reachable from
            // the test host), so `HealthBandTests`/`PanelRowTests` build hand-authored
            // Tokens instead. This recomputes the real board's own two calls —
            // `FightScreen.TokenFrom` off the actual struck `Combatant`, then
            // `BarColourFor`/`RowFor` off that Token, exactly as `PlayMode.Draw`'s own
            // redraw does every frame — and checks the wiring rather than the
            // arithmetic: that a token drawn from a real fight still carries
            // `Combatant.IsBloodied` unchanged, and that the board's bar colour and the
            // panel's row colour still agree for it. A monster on this seed's opening
            // exchange is rarely dropped to 0 (that is Death Save territory, pinned by
            // `DeathSavePipsTests` instead, not captured here), so this is ordinarily
            // the Healthy/Bloodied boundary rather than Downed — either is a valid
            // outcome for this check.
            var struckToken = TokenFrom(target, _labels);

            Assert(
                "play-4-attacked",
                new ProbeExpectation.EqualsExpected<bool>(
                    $"{target.Name}'s Token.IsBloodied against Combatant.IsBloodied ({target.CurrentHitPoints}/{target.Stats.MaximumHitPoints} hp)",
                    struckToken.IsBloodied,
                    target.IsBloodied),
                new ProbeExpectation.EqualsExpected<Color>(
                    $"{target.Name}'s board bar colour against its panel row colour",
                    BarColourFor(struckToken),
                    RowFor(struckToken, active: false, hidden: false).StateColour));

            await CaptureFrame(Path.Combine(directory, "play-4-attacked.png"));
        }
        else
        {
            ReportSkip(directory, "play-3-moved", "the first commanded turn had no visible living enemy to walk toward");
            ReportSkip(directory, "play-4-attacked", "the first commanded turn had no visible living enemy to attack");
        }

        // A feature if this character brought one — Cunning Dash succeeds after a move,
        // so prefer it; otherwise whatever the second row offers, refusals included.
        // The button's real caption carries its hotkey ("X · Cunning Dash"), same as
        // ClickButton's own match — a bare-string == here found nothing (#499).
        var feature = _buttons.FirstOrDefault(button => button.Caption.EndsWith(" · Cunning Dash", StringComparison.Ordinal)).Caption
            ?? _buttons.FirstOrDefault(button => button.Rect.Position.Y > ButtonRowTop + 1).Caption;

        // Optional coverage: not every commanded character brought a second-row
        // feature, and one that did can still legitimately refuse it (a targeted
        // feature aimed at nothing, a resource already spent) — refusals included is
        // the documented, working-as-intended shape of this step, unlike the two above.
        if (feature is not null)
        {
            ClickButton(feature);
            await CaptureFrame(Path.Combine(directory, "play-5-feature.png"));
        }
        else
        {
            ReportSkip(directory, "play-5-feature", "the first commanded character offered no second-row feature button");
        }

        // NoticeCodeIs(null) and FocusIs(Board) both already held before this click
        // (#719 review) — EndTurn's own handler always returns null, and the board was
        // never covered by a menu — so neither notices a no-op. What actually moved is
        // whose turn it is or which round it is: TurnState is captured before and after
        // the click and compared with Changed, the one predicate built for exactly this
        // ("something specific now differs", not "nothing refused").
        var turnStateBeforeEnd = TurnState();
        ClickButton("End Turn");
        Assert(
            "play-6-turn-ended",
            new ProbeExpectation.NoticeCodeIs(null),
            new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)),
            new ProbeExpectation.Changed<TurnStateSnapshot>("the active combatant or round", turnStateBeforeEnd, TurnState()));
        await CaptureFrame(Path.Combine(directory, "play-6-turn-ended.png"));

        // Play on to the next commanded turn; if it belongs to a caster, walk the cast
        // flow — menu, choice, target — through the same input path as everything else.
        await NextCommandedTurn();

        // Optional coverage: whether the second commanded turn is a caster with an
        // enemy to target, and whether Cast is actually offered this turn (slots and
        // prepared spells both fluctuate turn to turn even for a character that
        // CanCast in general), are both facts about a fight in progress. Both are
        // checked by *availability* — ButtonOffered, before acting — never by whether
        // the click's own effect happened to land: a offered Cast that fails to open
        // the spell menu is a fault below, not a second way to spell "not offered"
        // (#719 review — the old shape here reported a real failure as unavailable
        // coverage).
        if (CommandedCombatant() is { } caster
            && caster.Stats.Character?.CanCast == true
            && NearestEnemyOf(caster) is { } victim)
        {
            if (!ButtonOffered("Cast"))
            {
                ReportSkip(directory, "play-7-spell-menu", "Cast was not offered to this caster this turn");
                ReportSkip(directory, "play-8-cast", "Cast was not offered to this caster this turn");
            }
            else
            {
                ClickButton("Cast");

                // Cast is confirmed offered above, so failing to open the spell menu
                // here is the probe finding a real defect, not an absent turn of
                // events — a fault (Assert throws), never ReportSkip.
                Assert("play-7-spell-menu", new ProbeExpectation.FocusIs(typeof(PlayFocus.SpellMenu)));
                await CaptureFrame(Path.Combine(directory, "play-7-spell-menu.png"));

                if (_menuRows.Count > 0)
                {
                    // _menuRows carries only a rectangle and a delegate (#505), not the
                    // spell that filled it — DrawSpellMenu fills both in the same pass,
                    // over CastableSpells(caster) in order, so index 0 there is index 0
                    // here (the same recomputation RunSlotMenuProbe already relies on).
                    var expectedSpell = CastableSpells(caster).First();

                    Click(_menuRows[0].GetCenter());

                    // #719, third and fourth review: confirming *some* spell armed is
                    // not enough — a menu that resolved a different spell than the one
                    // it displayed at row 0 would still pass. Armed.Spell.Id is checked
                    // against expectedSpell.Id directly, before the click that could
                    // exploit a mismatch is ever sent.
                    if (Armed is not { Kind: TargetKind.Spell, Spell: { } armedSpell }
                        || armedSpell.Id != expectedSpell.Id)
                    {
                        throw new InvalidOperationException(
                            $"probe: required step 'play-8-cast' failed — the spell menu's first row "
                                + $"({expectedSpell.Name}) did not arm that spell (armed: "
                                + $"{Armed?.Spell?.Name ?? "nothing"}).");
                    }

                    var logCountBeforeCast = _encounter?.Log.Count ?? 0;

                    Click(CentreOf(victim.Position));

                    // Casting itself can still legitimately refuse (out of range, no
                    // valid target, an unseen target) the same way the attack above
                    // can — ClearPending runs whether the cast lands or is refused, so
                    // the focus popping back to the board is one thing this asserts
                    // unconditionally. The evidence below is attributed to *this
                    // spell specifically*: a log entry naming it, or a refusal from
                    // CastSpell's own curated code set — not a "spell." prefix (#719,
                    // fourth review: CastSpell can return target.unseen and the
                    // Action/Bonus-Action/Reaction codes shared with every other
                    // action, none of which start with it — a prefix would have
                    // wrongly faulted a legitimate refusal, e.g. a Blinded caster
                    // targeting an enemy only another party member can see).
                    Assert(
                        "play-8-cast",
                        new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)),
                        new ProbeExpectation.AnyOf(
                            $"evidence {armedSpell.Name} resolved or was refused",
                            [
                                new ProbeExpectation.EqualsExpected<bool>(
                                    $"a log entry naming {armedSpell.Name}",
                                    LogGrewNaming(logCountBeforeCast, armedSpell.Name),
                                    true),
                                new ProbeExpectation.NoticeCodeIsOneOf(CastSpellRefusalCodes),
                            ]));
                    await CaptureFrame(Path.Combine(directory, "play-8-cast.png"));
                }
                else
                {
                    // Cast was confirmed offered above (ButtonOffered("Cast")) and
                    // its menu confirmed open (FocusIs(SpellMenu)) — a menu with
                    // nothing to click is not a fact about this turn, it is those two
                    // confirmations disagreeing with each other. A fault (#719, third
                    // review), never the "unavailable coverage" this used to report.
                    throw new InvalidOperationException(
                        "probe: required step 'play-8-cast' failed — Cast was offered and its menu "
                            + "opened, but the spell menu showed no castable rows.");
                }
            }
        }
        else
        {
            ReportSkip(directory, "play-7-spell-menu", "the second commanded turn was not a caster with an enemy to target");
            ReportSkip(directory, "play-8-cast", "the second commanded turn was not a caster with an enemy to target");
        }

        // In a run, play the fight out through the same clicks — swing at the nearest
        // enemy, end the turn — to reach the other side of the fight: the post-fight
        // interlude with its level-ups, loot and save, or the defeat screen. Whichever
        // comes, the capture shows the run reporting it. Two more focuses live on this
        // stretch of the fight and cost it nothing extra to reach: the Outcome card —
        // HandleFightEnd used to skip it entirely under --probe, straight to
        // CompleteAndReport, from before this probe could press a key at all; now it
        // takes the real path and this loop is the one pressing the key — and the
        // Attack menu, offered to whichever party member is carrying more than one
        // weapon — Brenna, Korrin and Sable all do at level 1 — whenever their turn
        // comes up.
        if (_run is not null)
        {
            var safety = 0;
            var outcomeCaptured = false;
            var attackMenuCaptured = false;

            while (_phase == Phase.Fighting && safety < 5000)
            {
                safety++;

                // #301: the opening commanded turn (above) rarely has a threatened
                // square to find — the pregenerated party starts well outside melee
                // reach — so every later commanded turn gets its own try here too,
                // until one lands or the fight ends. TryCaptureThreatPreview is a
                // no-op the instant it has already captured or already tried this
                // exact commanded combatant, so this costs nothing once past that.
                if (!_threatPreviewCaptured
                    && CommandedCombatant() is { } threatFighter
                    && _encounter is { } threatEncounter)
                {
                    await TryCaptureThreatPreview(directory, threatEncounter, threatFighter);
                }

                if (_focus.Holds<PlayFocus.Outcome>())
                {
                    if (!outcomeCaptured)
                    {
                        await CaptureFrame(Path.Combine(directory, "run-9-outcome-card.png"));
                        outcomeCaptured = true;
                    }

                    // Any key moves on, same as a person acknowledging it — Escape
                    // stays reserved for the quit confirm everywhere else in the probe.
                    Press(Key.Space);
                }
                else if (!attackMenuCaptured
                    && CommandedCombatant() is { } menuCandidate
                    && menuCandidate.Stats.Attacks.Count > 1)
                {
                    // Carrying more than one named attack is not the same fact as
                    // "Attack is offered this instant" (action already spent, no
                    // target in range) — ButtonOffered checks the second, separately,
                    // so a turn that doesn't offer it yet simply loops to try again
                    // next frame, the same as before. Once offered and clicked,
                    // failing to open the menu is a fault (#719, second review): the
                    // old shape let this fall through silently, so a broken
                    // AttackMenu button was reported, wrongly, as "no such character
                    // ever took a turn" below.
                    if (ButtonOffered("Attack"))
                    {
                        ClickButton("Attack");
                        Assert("play-9-attack-menu", new ProbeExpectation.FocusIs(typeof(PlayFocus.AttackMenu)));
                        await CaptureFrame(Path.Combine(directory, "play-9-attack-menu.png"));
                        attackMenuCaptured = true;
                        Press(Key.Escape);
                    }

                    ClickButton("End Turn");
                }
                else if (CommandedCombatant() is { } fighter)
                {
                    if (NearestEnemyOf(fighter) is { } foe)
                    {
                        Click(CentreOf(foe.Position));
                    }

                    ClickButton("End Turn");
                }

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            if (!outcomeCaptured)
            {
                if (_phase == Phase.Fighting)
                {
                    // Still running: the loop's own safety budget ran out while the
                    // fight was genuinely still in progress. The #180 guard just
                    // below turns this into the stall fault with its own message —
                    // nothing more to report here.
                    ReportSkip(directory, "run-9-outcome-card", "the fight never completed within the probe's safety budget");
                }
                else
                {
                    // Completed, but not shown: phase moved on, so the fight plainly
                    // did complete — the exact HandleFightEnd -> CompleteAndReport
                    // bypass this loop exists to catch (see the comment above it),
                    // not "never got that far" (#719, third review). Faulting here,
                    // rather than silently falling through to capture
                    // run-9-after-fight as though nothing were wrong.
                    throw new InvalidOperationException(
                        "probe: required step 'run-9-outcome-card' failed — the fight completed "
                            + "but the Outcome card was never displayed.");
                }
            }

            if (!attackMenuCaptured)
            {
                ReportSkip(
                    directory,
                    "play-9-attack-menu",
                    "no party member carrying more than one weapon attack took a turn before the fight ended");
            }

            if (!_threatPreviewCaptured)
            {
                ReportSkip(
                    directory,
                    "play-2e-threat-preview",
                    "no reachable square on any commanded turn in fight 1 previewed a route crossing a "
                        + "threatened step");
            }

            // #180's own shape, guarded against rather than merely fixed: a play-out
            // loop that exhausts its safety budget still mid-fight used to be captured
            // anyway, under a name ("after-fight") that the screen it showed did not
            // earn. #499's ClickButton fix is why this should not trip today, but a
            // regression there should fault here rather than silently reproduce #180.
            if (_phase == Phase.Fighting)
            {
                throw new InvalidOperationException(
                    $"probe: required step 'run-9-after-fight' failed — the play-out loop hit its "
                        + $"safety budget (safety={safety}) still mid-fight, the exact stall #180 named.");
            }

            await CaptureFrame(Path.Combine(directory, "run-9-after-fight.png"));

            // The merchant reaches the party only at a Long Rest — the opening cycle's
            // own rungs after the first (GauntletLadder), which is exactly the
            // interlude fight 1 clearing on seed 1 lands on.
            if (_phase == Phase.Interlude && _shopAvailable)
            {
                Click(_shopButton.GetCenter());
                Assert("run-10-shop", new ProbeExpectation.FocusIs(typeof(PlayFocus.Shop)));
                await CaptureFrame(Path.Combine(directory, "run-10-shop.png"));

                // #704: a computed-centre click on a rect the window cannot show is not a
                // click at all — the pixel it lands on is off the viewport, so nothing is
                // there to receive it. Before the stall had a floor, this is exactly how
                // the probe could click Back and still pass at any offer count: the rect
                // existed on paper and the click landed nowhere, silently. Faulting here
                // makes an unreachable Back button a probe failure again rather than a
                // click that quietly did nothing.
                if (_shopBackButton.Position.Y + _shopBackButton.Size.Y > ScreenHeight)
                {
                    throw new InvalidOperationException(
                        $"probe: shop Back button's bottom edge ({_shopBackButton.Position.Y + _shopBackButton.Size.Y}) "
                            + $"is past the window's own bottom edge ({ScreenHeight}) — a computed-centre click on it "
                            + "would land off screen.");
                }

                Click(_shopBackButton.GetCenter());
            }
            else
            {
                ReportSkip(directory, "run-10-shop", "no Long Rest interlude was reached after this fight");
            }
        }

        GetTree().Quit();
    }

    /// <summary>
    /// A short, separate probe for the one focus the main sequence cannot reach at
    /// level 1: the Slot menu, offered only when a prepared spell can be upcast —
    /// which needs slots at two different levels for the same spell, and a level 1
    /// character never has that. <c>--one-fight</c> already fixes the party at level 3
    /// (<see cref="ResolveFight"/>), so this waits for whichever party member can
    /// cast, opens the first spell that qualifies, and takes the flow through the
    /// same real input every other step in this probe uses.
    /// </summary>
    private async Task RunSlotMenuProbe(string directory)
    {
        // A frame-counted poll, not a count of NextCommandedTurn calls: the same
        // commanded combatant can still be on screen several frames after its "End
        // Turn" click, and counting that as a used turn exhausted the budget on one
        // stalled transition before a caster's turn ever came up. The main run's own
        // play-out loop is exactly this shape for the same reason.
        var found = false;
        var safety = 0;

        while (!found && safety < 2000)
        {
            safety++;

            if (CommandedCombatant() is { } caster && caster.Stats.Character?.CanCast == true)
            {
                found = true;

                // Availability first, the same distinction the main run's Cast branch
                // makes (#719 review): whether Cast is offered this turn is a fact
                // about the fight, worth ReportSkip; whether an offered Cast opens the
                // spell menu is the probe's own effect, worth a fault if it does not.
                if (!ButtonOffered("Cast"))
                {
                    ReportSkip(directory, "play-9-spell-menu", "Cast was not offered to this caster this turn");
                    ReportSkip(directory, "play-9-slot-menu", "Cast was not offered to this caster this turn");
                }
                else
                {
                    ClickButton("Cast");
                    Assert("play-9-spell-menu", new ProbeExpectation.FocusIs(typeof(PlayFocus.SpellMenu)));

                    // A frame has to pass before _menuRows reflects the menu just
                    // opened — DrawSpellMenu fills it, and DrawSpellMenu runs on the
                    // next _Draw, not on the click itself. The same wait
                    // play-7-spell-menu already relies on before reading its own rows.
                    await CaptureFrame(Path.Combine(directory, "play-9-spell-menu.png"));

                    // _menuRows carries a rectangle and an Action now, not the spell
                    // that filled it (#505), so the upcastable row is found by
                    // recomputing the same castable ordering DrawSpellMenu just drew
                    // from and taking its index into _menuRows — the two are populated
                    // in the same pass, so the indices agree.
                    var castable = CastableSpells(caster).ToList();

                    // #302: an area spell's coverage preview, tried here first — before
                    // anything below clicks a row and closes this menu — with the
                    // spell menu fresh and _menuRows still lined up with castable.
                    // The pregenerated party's one caster (the Cleric) only reaches
                    // Spirit Guardians' Emanation at level 5 (its 3rd-level slot), so
                    // this is best-effort coverage on whatever level --one-fight
                    // started at, not guaranteed reach the way the upcast check below
                    // is — see client/README.md's "Arming an attack or a spell" for
                    // what this pins when it lands.
                    var areaSpellIndex = castable.FindIndex(spell => spell.Save?.Area is not null);

                    if (areaSpellIndex < 0)
                    {
                        ReportSkip(directory, "play-9-area-spell", "the caster's spell menu offered no area spell this turn");
                    }
                    else if (areaSpellIndex >= _menuRows.Count)
                    {
                        throw new InvalidOperationException(
                            $"probe: required step 'play-9-area-spell' failed — an area spell was found at "
                                + $"castable index {areaSpellIndex} but the drawn spell menu only has {_menuRows.Count} rows.");
                    }
                    else
                    {
                        var areaSpell = castable[areaSpellIndex];
                        Click(_menuRows[areaSpellIndex].GetCenter());

                        // A spell castable at more than one slot level opens the Slot
                        // menu first (ChooseSpell, PlayMode.Input.cs) — take the first
                        // slot offered so this reaches Targeting the same way a player
                        // choosing any slot would.
                        if (_focus.Top is PlayFocus.SlotMenu && _menuRows.Count > 0)
                        {
                            Click(_menuRows[0].GetCenter());
                        }

                        if (Armed is not { Kind: TargetKind.Spell, Spell: { } armedAreaSpell }
                            || armedAreaSpell.Id != areaSpell.Id)
                        {
                            throw new InvalidOperationException(
                                $"probe: required step 'play-9-area-spell' failed — the spell menu's area-spell "
                                    + $"row ({areaSpell.Name}) did not arm that spell (armed: "
                                    + $"{Armed?.Spell?.Name ?? "nothing"}).");
                        }

                        if (CommandedCombatant() is { } areaCaster && _encounter is { } areaEncounter)
                        {
                            // The caster's own square: always in range (distance zero),
                            // and the only square an Emanation like Spirit Guardians
                            // needs at all — its Cover answer ignores the aim point
                            // entirely (AreaTargeting's own remarks).
                            var hovered = areaCaster.Position;

                            GetViewport().PushInput(new InputEventMouseMotion
                            {
                                Position = CentreOf(hovered),
                                GlobalPosition = CentreOf(hovered),
                            });

                            var expectedArea = PlayMode.AreaCoverage(
                                areaEncounter.Battlefield,
                                areaCaster,
                                armedAreaSpell,
                                armedAreaSpell.Save!.Area!,
                                hovered,
                                areaEncounter.Combatants,
                                _unseen);

                            Assert(
                                "play-9-area-spell",
                                new ProbeExpectation.NonEmpty(
                                    "the expected area coverage", PathAsText([.. expectedArea.Squares])),
                                new ProbeExpectation.EqualsExpected<string>(
                                    "the drawn area coverage",
                                    PathAsText([.. _areaCoverage.OrderBy(s => s.X).ThenBy(s => s.Y)]),
                                    PathAsText([.. expectedArea.Squares.OrderBy(s => s.X).ThenBy(s => s.Y)])));
                            await CaptureFrame(Path.Combine(directory, "play-9-area-spell.png"));
                        }

                        // Back out without casting — Esc un-arms Targeting to the menu
                        // that armed it, a second Esc closes that menu — and reopen
                        // fresh, so the upcast check right below still finds an
                        // unclicked SpellMenu exactly as it did before this step
                        // existed.
                        Press(Key.Escape);
                        Press(Key.Escape);
                        ClickButton("Cast");
                        Assert("play-9-spell-menu", new ProbeExpectation.FocusIs(typeof(PlayFocus.SpellMenu)));

                        // _menuRows is repopulated by the next _Draw, not by the click
                        // itself — the same wait the very first open of this menu
                        // relies on, above.
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }

                    var upcastIndex = castable.FindIndex(spell => SlotLevelsFor(caster, spell).Count > 1);

                    // upcastIndex < 0 and upcastIndex >= _menuRows.Count used to share
                    // one skip message, but they are different facts (#719, second
                    // review): the first is "no such spell exists this turn" (a fact
                    // about the fight, ReportSkip); the second means an upcastable
                    // spell WAS found — availability is established — and the "the two
                    // are populated in the same pass" assumption above broke, which is
                    // a fault, not a second way to spell "not available".
                    if (upcastIndex < 0)
                    {
                        ReportSkip(
                            directory,
                            "play-9-slot-menu",
                            "the caster's spell menu offered no spell castable at more than one slot level");
                    }
                    else if (upcastIndex >= _menuRows.Count)
                    {
                        throw new InvalidOperationException(
                            $"probe: required step 'play-9-slot-menu' failed — an upcastable spell was found at "
                                + $"castable index {upcastIndex} but the drawn spell menu only has {_menuRows.Count} rows.");
                    }
                    else
                    {
                        Click(_menuRows[upcastIndex].GetCenter());

                        // The clicked row was chosen above specifically because
                        // SlotLevelsFor(caster, spell).Count > 1, and ChooseSpell
                        // (PlayMode.Input.cs) opens a Slot menu whenever that holds —
                        // so not landing here is a real defect, not "this spell
                        // doesn't support it" (#719 review; that fact is already
                        // confirmed above). A fault, not ReportSkip.
                        Assert("play-9-slot-menu", new ProbeExpectation.FocusIs(typeof(PlayFocus.SlotMenu)));
                        await CaptureFrame(Path.Combine(directory, "play-9-slot-menu.png"));

                        if (_menuRows.Count > 0)
                        {
                            Click(_menuRows[0].GetCenter());

                            if (_cursor is { } aimed)
                            {
                                Click(CentreOf(aimed));
                            }
                        }
                    }
                }
            }
            else if (CommandedCombatant() is { } fighter)
            {
                // Swings back rather than only ending the turn: a party that never
                // fights the level 3 Moderate encounter --one-fight builds can be
                // wiped before a caster's turn ever comes up, the same way the main
                // run's own play-out loop plays every non-scripted turn.
                if (NearestEnemyOf(fighter) is { } foe)
                {
                    Click(CentreOf(foe.Position));
                }

                ClickButton("End Turn");
            }
            else if (_encounter?.IsComplete == true)
            {
                // Nobody left to command and nothing left to wait for — stop burning
                // the safety budget once the fight itself has already decided this.
                break;
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (!found)
        {
            // Neither capture was ever attempted — both need a marker, or an auditor
            // sees a missing play-9-spell-menu.png with nothing explaining it, exactly
            // the "capture that vanished in silence" shape this PR exists to close.
            ReportSkip(directory, "play-9-spell-menu", "no caster's turn came up within the probe's turn budget");
            ReportSkip(directory, "play-9-slot-menu", "no caster's turn came up within the probe's turn budget");
        }
    }

    /// <summary>
    /// Marks a capture the probe could not reach — loud rather than a silently
    /// missing file, so a shrunk capture set is something a diff sees rather than
    /// something a person has to notice by counting. Reserved for <b>optional</b>
    /// coverage: a step whose reachability is itself a fact about the fight in progress
    /// (whether a character brought a feature, whether a turn belongs to a caster,
    /// whether a fight stays clear long enough to reach a Long Rest). A <b>required</b>
    /// step's failed predicate is a fault (<see cref="Assert"/>), never this (#705).
    /// </summary>
    private static void ReportSkip(string directory, string name, string reason)
    {
        GD.Print($"probe: skipped {name} — {reason}");
        File.WriteAllText(Path.Combine(directory, name + ".skipped.txt"), reason + "\n");
    }

    /// <summary>
    /// A path rendered as one comparable string. <see
    /// cref="ProbeExpectation.EqualsExpected{T}"/> compares with
    /// <c>EqualityComparer&lt;T&gt;.Default</c>, which is reference equality for a list
    /// — two structurally identical <see cref="GridPosition"/> sequences built by two
    /// different calls would never compare equal as lists, so play-2d-path-preview
    /// compares this instead.
    /// </summary>
    private static string PathAsText(IReadOnlyList<GridPosition> path) =>
        string.Join(" ", path.Select(square => $"({square.X},{square.Y})"));

    /// <summary>
    /// Checks <paramref name="expectations"/> against the screen's live state right now
    /// and names <paramref name="step"/> in the fault if one fails (#705). Read the
    /// state once, into a plain <see cref="ProbeSnapshot"/>, so the actual comparison —
    /// what a fault message says and why — is the pure half a Godot-free xUnit test can
    /// pin (<c>ProbeExpectationTests</c>); this method itself is the Godot-dependent
    /// remainder, reading <see cref="_focus"/> and <see cref="_notice"/> off a live
    /// scene, the same split <see cref="ProbeFaults"/> and <see cref="CaptureOutcome"/>
    /// each make for their own half of this issue.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A required step's postcondition did not hold — thrown rather than printed or
    /// skipped, so <see cref="ProbeFaults"/> reports the probe crashed and exits 1.
    /// </exception>
    private void Assert(string step, params ProbeExpectation[] expectations)
    {
        var snapshot = new ProbeSnapshot(_focus.Top, _notice);

        foreach (var expectation in expectations)
        {
            if (expectation.Failure(snapshot) is { } failure)
            {
                throw new InvalidOperationException(
                    $"probe: required step '{step}' failed its predicate — {failure}");
            }
        }
    }

    private Combatant? NearestEnemyOf(Combatant active) =>
        _encounter?.EnemiesOf(active)
            .Where(enemy => !enemy.IsDead)
            .OrderBy(enemy => enemy.Position.DistanceFeetTo(active.Position))
            .FirstOrDefault();

    /// <summary>
    /// Living enemies the party can actually see, nearest first — the same fog filter
    /// <see cref="TokenAt"/> and <see cref="PendingTargets"/> already apply, computed
    /// directly here rather than assumed from a stale field.
    /// </summary>
    /// <remarks>
    /// <see cref="NearestEnemyOf"/> ignores the fog (#719, fourth review): the
    /// geometrically nearest enemy can be one no party member can see, and clicking its
    /// square is not a click <see cref="TokenAt"/> — the function the real click
    /// handler consults — will recognise as occupied at all. A step that needs a target
    /// the probe's own click can actually land on needs this, not <see
    /// cref="NearestEnemyOf"/>.
    /// </remarks>
    private IReadOnlyList<Combatant> VisibleEnemiesOf(Combatant active)
    {
        if (_encounter is not { } encounter)
        {
            return [];
        }

        var visible = PartyVision.VisibleSquares(encounter.Battlefield, encounter.Combatants, PregeneratedParty.SideId);

        return encounter.EnemiesOf(active)
            .Where(enemy => !enemy.IsDead && visible.Contains(enemy.Position))
            .OrderBy(enemy => enemy.Position.DistanceFeetTo(active.Position))
            .ToList();
    }

    private Combatant? NearestVisibleEnemyOf(Combatant active) => VisibleEnemiesOf(active).FirstOrDefault();

    /// <summary>
    /// Searches every square <see cref="_reachable"/> currently offers for one whose
    /// previewed route crosses an Opportunity-Attack threat for <paramref
    /// name="mover"/>, and if one exists, hovers it for real and asserts the live
    /// <see cref="_threatenedSteps"/> against an independently-recomputed expectation
    /// — capturing <c>play-2e-threat-preview.png</c> once for the whole probe run
    /// (#301).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The search itself never reads <see cref="_threatenedSteps"/></b> — the field
    /// under test (#734 review round 1). The step's first version searched by hovering
    /// each candidate for real and checking whether the live field came back non-empty,
    /// which meant a wiring bug that never marks anything at all (the <c>AddRange</c>
    /// in <c>UpdatePreviewPath</c> missing, or the visible-enemies filter inverted so
    /// every enemy reads as hidden) would make every candidate look "nothing to find"
    /// — a skip, not a fault, hiding exactly the defect this step exists to catch.
    /// The search below instead recomputes <see cref="HoverPreviewPath"/> and <see
    /// cref="ThreatenedSteps"/> fresh for each candidate; only once a real candidate is
    /// found does it touch the live screen at all, to prove the *wiring* — that hovering
    /// for real produces the same answer the seam alone does.
    /// </para>
    /// <para>
    /// Called once for the opening commanded turn and, if that turn has nothing to
    /// find, again for every later commanded turn the fight-1 play-out loop drives —
    /// melee contact, and so a real threatened square, is not guaranteed before the
    /// party has closed distance. <see cref="_threatPreviewLastAttemptedId"/> keeps a
    /// turn spanning many probe frames from re-searching every frame; <see
    /// cref="_threatPreviewCaptured"/> stops the whole search, everywhere it is called,
    /// the instant one capture lands.
    /// </para>
    /// </remarks>
    private async Task TryCaptureThreatPreview(string directory, Encounter encounter, Combatant mover)
    {
        if (_threatPreviewCaptured || mover.Id == _threatPreviewLastAttemptedId)
        {
            return;
        }

        _threatPreviewLastAttemptedId = mover.Id;

        var visibleEnemies = encounter.Combatants
            .Where(combatant => combatant.SideId != mover.SideId && !_unseen.Contains(combatant.Position))
            .ToList();

        GridPosition? threatSquare = null;
        IReadOnlyList<GridPosition> expectedThreatened = [];

        foreach (var candidate in _reachable)
        {
            var candidatePath = HoveredPath(encounter.Battlefield, mover, candidate, _reachable, encounter.Combatants);
            var candidateThreatened = ThreatenedSteps(
                encounter.Battlefield, mover, candidatePath, visibleEnemies, _unseen);

            if (candidateThreatened.Count > 0)
            {
                threatSquare = candidate;
                expectedThreatened = candidateThreatened;
                break;
            }
        }

        if (threatSquare is not { } square)
        {
            return;
        }

        var pixel = CentreOf(square);

        GetViewport().PushInput(new InputEventMouseMotion
        {
            Position = pixel,
            GlobalPosition = pixel,
        });

        Assert(
            "play-2e-threat-preview",
            new ProbeExpectation.NonEmpty(
                "the threatened steps on the previewed route", PathAsText(expectedThreatened)),
            new ProbeExpectation.EqualsExpected<string>(
                "the threatened steps on the previewed route",
                PathAsText(_threatenedSteps),
                PathAsText(expectedThreatened)));
        await CaptureFrame(Path.Combine(directory, "play-2e-threat-preview.png"));

        // #734 review round 1: the keyboard cursor's own ring and the threat mark
        // draw the identical bordered Rect2 at the identical width on whatever square
        // each sits on, so a cursor parked on a threatened step is the one case that
        // proves the inset actually chosen (ThreatMarkInsetPixels, PlayMode.Draw.cs:
        // draw order alone cannot separate two same-width strokes on one rectangle)
        // — neither one is a plain function DrawTests can
        // pin, so this capture is the seam. _cursor is set directly rather than
        // walked there with synthesized arrow presses: what this proves is which
        // shape survives on top once both are drawn on the same square, not that
        // arrow-key routing can reach it (a separate, already-covered concern), and
        // restoring it after keeps this step from moving the keyboard cursor out
        // from under whatever a later step assumes about it.
        var cursorBeforeThreatCapture = _cursor;
        _cursor = square;
        await CaptureFrame(Path.Combine(directory, "play-2f-threat-under-cursor.png"));
        _cursor = cursorBeforeThreatCapture;

        _threatPreviewCaptured = true;
    }

    /// <summary>
    /// Whether any combat-log entry appended since <paramref name="countBefore"/>
    /// names every one of <paramref name="names"/> — the evidence a required step
    /// attributes to one specific action, not "the log grew" (#719, fourth review: an
    /// unrelated action's own entry would satisfy a bare length check just as well).
    /// </summary>
    private bool LogGrewNaming(int countBefore, params string[] names) =>
        _encounter is { } encounter
        && encounter.Log.Skip(countBefore)
            .Any(step => Array.TrueForAll(names, name => step.Narration.Contains(name, StringComparison.Ordinal)));

    /// <summary>
    /// Every refusal code <see cref="Encounter.Attack"/> — and the client's own "no
    /// reaching attack" fallback right below — can produce.
    /// </summary>
    /// <remarks>
    /// Curated from the engine's source rather than guessed from a prefix (#719, fourth
    /// review): a prefix like <c>"attack."</c> would miss <c>action.spent</c>,
    /// <c>combatant.cannot_act</c> and <c>encounter.complete</c>, all shared with other
    /// actions. Sourced from <c>Encounter.Attack</c> and its two shared preambles,
    /// <c>TryGetActingCombatant</c> and <c>CheckUsage</c>
    /// (<c>Encounter.Entries.cs</c>); <c>client.no_attack</c> is this file's own
    /// synthetic code for "no attack reaches", raised in <c>PlayMode.Input.cs</c> rather
    /// than the engine. Re-derive by hand if <c>Encounter.Attack</c>'s own refusal set
    /// changes — there is no enum or constant list in <c>Core</c> to read this from
    /// automatically.
    /// </remarks>
    private static readonly HashSet<string> AttackRefusalCodes =
    [
        "encounter.complete",
        "combatant.cannot_act",
        "action.spent",
        "attack.unknown",
        "target.dead",
        "attack.charmed",
        "attack.out_of_range",
        "attack.total_cover",
        "attack.not_in_multiattack",
        "attack.composition_exhausted",
        "entry.not_recharged",
        "entry.no_uses_left",
        "client.no_attack",
    ];

    /// <summary>Every refusal code <see cref="Encounter.CastSpell(string,Combatant,int?)"/> can produce.</summary>
    /// <remarks>
    /// Curated the same way, and for the same reason, as <see cref="AttackRefusalCodes"/>
    /// (#719, fourth review — this is the set that replaced the false
    /// <c>NoticeCodeStartsWith("spell.")</c> claim): <c>CastSpell</c> can return
    /// <c>target.unseen</c> (Concealed's shared mechanism, #673 — a Blinded caster
    /// targeting an enemy only another party member can see reaches exactly this) and
    /// the Action/Bonus-Action/Reaction "already spent" codes shared with every other
    /// action, none of which start with <c>"spell."</c>. Sourced from
    /// <c>Encounter.Casting.cs</c>'s <c>CastSpell</c> and <c>CheckCastingCost</c>, plus
    /// <c>Encounter.cs</c>'s <c>UnseenTargetRefusal</c>. <c>combatant.cannot_act</c> is
    /// deliberately absent: <c>CastSpell</c>'s own comment says it is not routed through
    /// <c>TryGetActingCombatant</c>, so that code is not reachable from here. Re-derive
    /// by hand if <c>CastSpell</c>'s own refusal set changes.
    /// </remarks>
    private static readonly HashSet<string> CastSpellRefusalCodes =
    [
        "encounter.complete",
        "spell.not_a_caster",
        "spell.unknown",
        "spell.out_of_range",
        "spell.wrong_target_type",
        "spell.total_cover",
        "spell.target_not_dead",
        "spell.dead_too_long",
        "spell.no_room_to_stand",
        "spell.charmed",
        "target.unseen",
        "spell.needs_target",
        "spell.not_implemented",
        "spell.area_not_modelled",
        "spell.save_effect_not_modelled",
        "action.spent",
        "bonus_action.spent",
        "reaction.spent",
        "spell.too_slow",
        "spell.cantrip_needs_no_slot",
        "spell.slot_below_spell",
        "spell.no_slot",
    ];

    /// <summary>
    /// A snapshot of exactly the facts a no-op click on a live <see cref="Combatant"/>
    /// would leave untouched — read into value-typed fields so it survives past the
    /// moment the live object itself changes underneath it (#719 review, play-2's
    /// "board unchanged" claim).
    /// </summary>
    /// <remarks>
    /// Widened at #719's second review: the first version compared only <see
    /// cref="Combatant.Turn"/>'s <c>HasAction</c>, so a misrouted click that spent only
    /// the Bonus Action or the Reaction — or burned a swing off <see
    /// cref="FeatureState.AttacksRemainingThisAction"/> without touching the
    /// Action itself — passed <c>play-2</c> undetected. Every resource a turn's economy
    /// actually tracks is compared now: the Action, the Bonus Action, the Reaction, and
    /// the Attack action's own remaining swings.
    /// </remarks>
    private readonly record struct ActorVitalsSnapshot(
        GridPosition Position,
        int HitPoints,
        int Movement,
        bool HasAction,
        bool HasBonusAction,
        bool HasReaction,
        int AttacksRemaining);

    /// <summary>Null when nobody is commanded — a real fact worth comparing, not a value to paper over.</summary>
    private static ActorVitalsSnapshot? ActorVitals(Combatant? combatant) =>
        combatant is null
            ? null
            : new ActorVitalsSnapshot(
                combatant.Position,
                combatant.CurrentHitPoints,
                combatant.Turn.MovementFeet,
                combatant.Turn.HasAction,
                combatant.Turn.HasBonusAction,
                combatant.Turn.HasReaction,
                combatant.Features.AttacksRemainingThisAction);

    /// <summary>The two facts that "a turn actually ended" moves — who is acting, and which round it is.</summary>
    /// <param name="ActiveCombatant">
    /// <see cref="Combatant.Id"/>, not <see cref="Combatant.Name"/> (#719, second
    /// review) — a name is not a turn identity: "2 Giant Wasps" is a legal encounter
    /// (<c>EncounterFactory</c>'s own comment), and two same-named combatants acting
    /// consecutively would make a name-keyed comparison see no change where the active
    /// combatant plainly did change. <c>Id</c> is assigned uniquely per combatant
    /// (<c>$"monster{index}"</c> for monsters) specifically so a repeated name never
    /// collides with it.
    /// </param>
    private readonly record struct TurnStateSnapshot(string? ActiveCombatant, int Round);

    private TurnStateSnapshot TurnState() =>
        new(_encounter?.ActiveCombatant?.Id, _encounter?.Round ?? -1);

    /// <summary>
    /// Waits for a commanded turn to come up. Throws rather than returning silently
    /// exhausted (#705): every call site depends on a commanded combatant being present
    /// afterward, and a timed-out wait used to be indistinguishable from one that
    /// found a turn immediately — a stalled or ended-early run would have every
    /// downstream step act on a null <see cref="CommandedCombatant"/>, whatever that
    /// happens to mean for each one, rather than the run failing at the point the stall
    /// actually happened.
    /// </summary>
    private async Task NextCommandedTurn()
    {
        var waited = 0;

        while (CommandedCombatant() is null && waited < 3000)
        {
            waited++;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (CommandedCombatant() is null)
        {
            throw new InvalidOperationException(
                $"probe: no commanded turn arrived within {waited} frames — the run stalled or ended early.");
        }
    }

    /// <summary>
    /// Clicks a button by the name a person reads on it, not by its pixel rect — the
    /// probe's whole point is driving the screen the way a person would, by what a
    /// button says rather than where it happens to sit.
    /// </summary>
    /// <remarks>
    /// <b>Every button's real caption carries its hotkey</b> (<see cref="AddButton(float,float,TurnAction)"/>
    /// builds <c>"{HotkeyLabel} · {Caption}"</c> — "Space · End Turn", "A · Attack") —
    /// so a bare-string match against <c>caption</c> alone never found anything.
    /// Found the hard way (#499): every <c>ClickButton</c> call in this probe was a
    /// silent no-op, masked because <see cref="NothingLeftButEndTurn"/> usually ends a
    /// turn on its own shortly after — except for a character still holding an unused
    /// Second Wind or Action Surge, whose turn then never ends at all. This affects only
    /// the probe: a real click is a pixel through <see cref="HandleClick"/>, never this
    /// method, so no player-facing behaviour changes.
    /// </remarks>
    private void ClickButton(string caption)
    {
        var button = _buttons.FirstOrDefault(candidate => MatchesCaption(candidate.Caption, caption));

        if (button.Caption is not null)
        {
            Click(button.Rect.GetCenter());
        }
    }

    /// <summary>
    /// Whether a button reading <paramref name="caption"/> is on the row right now —
    /// checked *before* acting, so a branch can tell "this turn doesn't offer it"
    /// (optional coverage, <see cref="ReportSkip"/>) apart from "it is offered and the
    /// click's own effect failed" (a fault): the old shape asked only the second
    /// question and reported every failure as the first (#719 review).
    /// </summary>
    private bool ButtonOffered(string caption) =>
        _buttons.Any(candidate => MatchesCaption(candidate.Caption, caption));

    /// <summary>Shared by <see cref="ClickButton"/> and <see cref="ButtonOffered"/> so the two can never drift apart on what counts as a match.</summary>
    private static bool MatchesCaption(string candidateCaption, string caption) =>
        candidateCaption == caption || candidateCaption.EndsWith(" · " + caption, StringComparison.Ordinal);

    /// <summary>A real keypress, pushed through the viewport like every click.</summary>
    private void Press(Key keycode)
    {
        GetViewport().PushInput(new InputEventKey
        {
            Keycode = keycode,
            Pressed = true,
        });
    }

    /// <summary>A real click, pushed through the viewport, not a call around the input layer.</summary>
    private void Click(Vector2 position)
    {
        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = position,
            GlobalPosition = position,
        });
    }

    /// <summary>
    /// Rests the pointer on the first action button and waits out the hover delay, then
    /// asserts the hint it produced (#719 review — the old version checked only that a
    /// button existed *before* acting and never read <see cref="_hint"/> at all, so a
    /// hover that silently produced nothing, or the wrong button's text, still passed).
    /// </summary>
    /// <remarks>
    /// Through the real input path like every other probe step — a synthesized motion
    /// event, not a poke at <c>_hint</c> to make the assertion trivially true — because
    /// the thing worth verifying is that resting a pointer produces the hint that
    /// button is registered for, not that a field can be assigned. The wait is real
    /// time rather than a frozen clock: the hover delay is deliberately measured in
    /// seconds a person waits, and <c>--probe</c> freezes only the *animation* clock.
    /// </remarks>
    private async Task HoverFirstButton()
    {
        if (_buttons.Count == 0)
        {
            // A commanded turn always offers at least End Turn (NothingLeftButEndTurn),
            // so an empty row here is not "nothing to hover this time" — it means the
            // commanded turn itself is in a state this probe does not recognise. Fault
            // rather than the silent early return this used to be (#705): a required
            // step that could not even attempt its action is exactly the shape #705
            // exists to stop being invisible.
            throw new InvalidOperationException(
                "probe: required step 'play-2b-hint' failed — no button was offered to hover.");
        }

        var hovered = _buttons[0];

        // Every button on this row is built from a TurnAction (BuildButtons' one call
        // site), and TurnOptions.Hint returns non-empty text for every TurnAction, so
        // this is always registered.
        var expectedHint = _buttonHints.TryGetValue(hovered.Caption, out var hint) ? hint : null;

        // Checked before the comparison below, not folded into it (#719, second
        // review): if registration itself broke, expectedHint and the hint HintAt
        // reads at runtime would both independently be null, and EqualsExpected's own
        // null == null would pass — the exact "both sides went missing together" shape
        // a bare comparison cannot catch. NonEmpty rules that out on its own, first.
        Assert("play-2b-hint", new ProbeExpectation.NonEmpty($"the registered hint for '{hovered.Caption}'", expectedHint));

        var centre = hovered.Rect.Position + (hovered.Rect.Size / 2);

        GetViewport().PushInput(new InputEventMouseMotion
        {
            Position = centre,
            GlobalPosition = centre,
        });

        await ToSignal(GetTree().CreateTimer(HoverDelaySeconds + 0.4), SceneTreeTimer.SignalName.Timeout);

        Assert("play-2b-hint", new ProbeExpectation.EqualsExpected<string?>("the hint text", _hint, expectedHint));
    }
}
