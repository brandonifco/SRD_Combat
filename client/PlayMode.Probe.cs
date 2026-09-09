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
    /// on purpose, Tab arming and cycling from a cold turn, a walk, a swing, a feature,
    /// and when a caster's turn comes, the spell menu and a cast. How a change to this
    /// screen gets checked without a person clicking.
    /// </summary>
    /// <remarks>
    /// <b>Every required step below asserts a predicate before it trusts its own
    /// capture's name</b> (#705): a focus layer (<see cref="Assert"/> with <see
    /// cref="ProbeExpectation.FocusIs"/>), a notice or refusal code (<see
    /// cref="ProbeExpectation.NoticeCodeIs"/>), or — for coverage a fight in progress
    /// may or may not offer — <see cref="ReportSkip"/>, named at the branch that could
    /// not be taken rather than left to a missing file. A failed <see
    /// cref="ProbeExpectation"/> throws, the same fault path a crash already takes
    /// (<see cref="ProbeFaults"/>): a required step's postcondition not holding is a
    /// fault, never a silently mislabelled PNG.
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
        // board unchanged. That is exactly what the two expectations below pin. This
        // step used to be named play-2-refused and its doc comment claimed a refusal
        // that never actually happened (#705) — renamed so no filename claims more than
        // this asserts; retargeting it onto a refusal the probe can actually reach is
        // #521's open decision, not this one's.
        ClickButton("Stand Up");
        Assert(
            "play-2-stand-up-not-offered",
            new ProbeExpectation.NoticeCodeIs(null),
            new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)));
        await CaptureFrame(Path.Combine(directory, "play-2-stand-up-not-offered.png"));

        await HoverFirstButton();
        await CaptureFrame(Path.Combine(directory, "play-2b-hint.png"));

        // Tab from a cold turn: the first press arms the attack and aims at the
        // nearest enemy, the second walks the ring — then Esc backs out, so the rest
        // of the probe starts from the same clean turn it always did.
        Press(Key.Tab);
        Press(Key.Tab);
        Assert("play-2c-tab-armed", new ProbeExpectation.FocusIs(typeof(PlayFocus.Targeting)));
        await CaptureFrame(Path.Combine(directory, "play-2c-tab-armed.png"));
        Press(Key.Escape);

        if (CommandedCombatant() is { } active
            && NearestEnemyOf(active) is { } target)
        {
            if (_reachable.Count > 0)
            {
                var step = _reachable
                    .OrderBy(square => square.DistanceFeetTo(target.Position))
                    .ThenBy(square => square.X).ThenBy(square => square.Y)
                    .First();

                Click(CentreOf(step));

                // _reachable is the engine's own list of legal destinations (that is
                // what earns a square the highlight), so a click onto one of them is
                // expected to succeed outright, not merely to leave the board.
                Assert(
                    "play-3-moved",
                    new ProbeExpectation.NoticeCodeIs(null),
                    new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)));
                await CaptureFrame(Path.Combine(directory, "play-3-moved.png"));
            }
            else
            {
                ReportSkip(directory, "play-3-moved", "no reachable square was open for this turn's move step");
            }

            Click(CentreOf(target.Position));

            // Unlike the move above, this bare click's own attack can legitimately
            // refuse (out of reach after a short move, no attack that reaches this
            // target) — refusals included is documented, working-as-intended coverage
            // here, the same as the feature click below. What must hold regardless is
            // that a bare board click never leaves an armed layer behind.
            Assert("play-4-attacked", new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)));
            await CaptureFrame(Path.Combine(directory, "play-4-attacked.png"));
        }
        else
        {
            ReportSkip(directory, "play-3-moved", "the first commanded turn had no living enemy to walk toward");
            ReportSkip(directory, "play-4-attacked", "the first commanded turn had no living enemy to attack");
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

        ClickButton("End Turn");
        Assert(
            "play-6-turn-ended",
            new ProbeExpectation.NoticeCodeIs(null),
            new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)));
        await CaptureFrame(Path.Combine(directory, "play-6-turn-ended.png"));

        // Play on to the next commanded turn; if it belongs to a caster, walk the cast
        // flow — menu, choice, target — through the same input path as everything else.
        await NextCommandedTurn();

        // Optional coverage on both branches below: whether the second commanded turn
        // is a caster with an enemy to target, and whether Cast was actually offered
        // this turn (slots and prepared spells both fluctuate turn to turn even for a
        // character that CanCast in general), are both facts about a fight in
        // progress. ClickButton("Cast") finding no button is a no-op the same as
        // Stand Up above — capturing play-7-spell-menu without first checking that the
        // menu actually opened would repeat the exact defect #705 fixed there.
        if (CommandedCombatant() is { } caster
            && caster.Stats.Character?.CanCast == true
            && NearestEnemyOf(caster) is { } victim)
        {
            ClickButton("Cast");

            if (_focus.Top is PlayFocus.SpellMenu)
            {
                await CaptureFrame(Path.Combine(directory, "play-7-spell-menu.png"));

                if (_menuRows.Count > 0)
                {
                    Click(_menuRows[0].GetCenter());
                    Click(CentreOf(victim.Position));

                    // Casting can legitimately refuse (out of range, no valid target)
                    // the same way the attack above can — ClearPending runs whether the
                    // cast lands or is refused, so the focus popping back to the board
                    // is the one thing this asserts unconditionally.
                    Assert("play-8-cast", new ProbeExpectation.FocusIs(typeof(PlayFocus.Board)));
                    await CaptureFrame(Path.Combine(directory, "play-8-cast.png"));
                }
                else
                {
                    ReportSkip(directory, "play-8-cast", "the caster's spell menu offered no castable rows");
                }
            }
            else
            {
                ReportSkip(directory, "play-7-spell-menu", "Cast was not offered to this caster this turn");
                ReportSkip(directory, "play-8-cast", "Cast was not offered to this caster this turn");
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
                    ClickButton("Attack");

                    if (_focus.Top is PlayFocus.AttackMenu)
                    {
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
                ReportSkip(directory, "run-9-outcome-card", "the fight never completed within the probe's safety budget");
            }

            if (!attackMenuCaptured)
            {
                ReportSkip(
                    directory,
                    "play-9-attack-menu",
                    "no party member carrying more than one weapon attack took a turn before the fight ended");
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
                ClickButton("Cast");

                // A frame has to pass before _menuRows reflects the menu just opened —
                // DrawSpellMenu fills it, and DrawSpellMenu runs on the next _Draw, not
                // on the click itself. The same wait play-7-spell-menu already relies on
                // before reading its own rows.
                await CaptureFrame(Path.Combine(directory, "play-9-spell-menu.png"));

                // _menuRows carries a rectangle and an Action now, not the spell that
                // filled it (#505), so the upcastable row is found by recomputing the
                // same castable ordering DrawSpellMenu just drew from and taking its
                // index into _menuRows — the two are populated in the same pass, so the
                // indices agree.
                var castable = CastableSpells(caster).ToList();
                var upcastIndex = castable.FindIndex(spell => SlotLevelsFor(caster, spell).Count > 1);

                if (upcastIndex < 0 || upcastIndex >= _menuRows.Count)
                {
                    ReportSkip(
                        directory,
                        "play-9-slot-menu",
                        "the caster's spell menu offered no spell castable at more than one slot level");
                }
                else
                {
                    Click(_menuRows[upcastIndex].GetCenter());

                    if (_focus.Top is PlayFocus.SlotMenu)
                    {
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
                    else
                    {
                        ReportSkip(directory, "play-9-slot-menu", "the chosen spell did not open a Slot menu");
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
        var button = _buttons.FirstOrDefault(candidate =>
            candidate.Caption == caption
            || candidate.Caption.EndsWith(" · " + caption, StringComparison.Ordinal));

        if (button.Caption is not null)
        {
            Click(button.Rect.GetCenter());
        }
    }

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
    /// Rests the pointer on the first action button and waits out the hover delay, so a
    /// capture catches the hint actually drawn.
    /// </summary>
    /// <remarks>
    /// Through the real input path like every other probe step — a synthesized motion
    /// event, not a poke at <c>_hint</c> — because the thing worth verifying is that
    /// resting a pointer produces a hint, not that a field can be assigned. The wait is
    /// real time rather than a frozen clock: the hover delay is deliberately measured in
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

        var centre = _buttons[0].Rect.Position + (_buttons[0].Rect.Size / 2);

        GetViewport().PushInput(new InputEventMouseMotion
        {
            Position = centre,
            GlobalPosition = centre,
        });

        await ToSignal(GetTree().CreateTimer(HoverDelaySeconds + 0.4), SceneTreeTimer.SignalName.Timeout);
    }
}
