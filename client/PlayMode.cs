using Godot;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Viewer;

/// <summary>
/// Plays a fight with the mouse: the party's turns wait for the player, every other side
/// is taken by <see cref="SimpleTacticsPolicy"/>, one turn per beat so it can be watched.
/// </summary>
/// <remarks>
/// <para>
/// <b>The loop only ever calls the engine's public actions and shows what comes back.</b>
/// A refusal is displayed with its code rather than swallowed — a refusal is the engine
/// explaining a rule, and hiding it would make this client a second place rules live.
/// The engine is also the authority on every click: an unreachable square is <i>sent</i>
/// to <c>Move</c> and refused there, so the highlight is advice, never a rule.
/// </para>
/// <para>
/// The one choice the client makes is which attack a click means, and it is the same
/// choice the console client makes, from the same shared code: the hardest-hitting
/// attack that reaches (<see cref="AttackChoice"/>).
/// </para>
/// </remarks>
public partial class PlayMode : FightScreen
{
    private Encounter _encounter = null!;
    private Labels _labels = null!;
    private string _subtitle = string.Empty;
    private double _elapsed;
    private double _pace = SecondsPerTurn;
    private readonly HashSet<GridPosition> _reachable = [];
    private readonly List<(Rect2 Rect, string Caption, Func<ActionRefusal?> Act)> _buttons = [];
    private string? _notice;
    private bool _probeStarted;

    protected override string Title => "SRD_Combat — playing a fight";

    protected override void OnReady()
    {
        var seed = SeedArgument();
        var fight = ResolveFight(seed);

        _encounter = fight.Encounter;
        _labels = Labels.For(_encounter.Combatants);
        AdoptBattlefield(_encounter);
        _subtitle = $"seed {seed} — the party against {RosterOf(fight)}";

        BuildButtons();

        // A probe run drives the screen through its own input path — synthesized clicks
        // through the viewport — and captures what each one produced. Monsters hurry so
        // the probe spends its time on the party's turn, the part being verified.
        if (HasArgument("probe"))
        {
            _pace = 0.05;
        }

        RefreshAfterAction(null);
    }

    /// <summary>
    /// The actions that need no target. Everything with a target is a click on the grid.
    /// </summary>
    private void BuildButtons()
    {
        var x = (float)GridLeft;
        var y = GridTop + (GridHeight * CellPixels) + 14f;

        foreach (var (caption, act) in new (string, Func<ActionRefusal?>)[]
        {
            ("Dodge", () => _encounter.Dodge()),
            ("Dash", () => _encounter.Dash()),
            ("Disengage", () => _encounter.Disengage()),
            ("Stand Up", () => _encounter.StandUp()),
            ("Escape", () => _encounter.Escape()),
            ("End Turn", () => { _encounter.EndTurn(); return null; }),
        })
        {
            var width = TextFont.GetStringSize(caption, fontSize: 13).X + 22;
            _buttons.Add((new Rect2(x, y, width, 28), caption, act));
            x += width + 8;
        }
    }

    /// <summary>The active combatant when it is the player's to command, else null.</summary>
    private Combatant? CommandedCombatant() =>
        !_encounter.IsComplete
        && _encounter.ActiveCombatant is { } active
        && active.SideId == PregeneratedParty.SideId
        && active.CanAct
            ? active
            : null;

    public override void _Process(double delta)
    {
        if (_encounter.IsComplete)
        {
            RunProbeIfAsked();
            return;
        }

        if (_encounter.ActiveCombatant is not { } active)
        {
            return;
        }

        if (CommandedCombatant() is not null)
        {
            RunProbeIfAsked();
            return;
        }

        // Somebody else's turn — the policy's, or a party member who cannot act. One
        // turn per beat, so the player can follow what is happening to them.
        _elapsed += delta;

        if (_elapsed < _pace)
        {
            return;
        }

        _elapsed = 0;

        if (active.SideId != PregeneratedParty.SideId)
        {
            SimpleTacticsPolicy.TakeTurn(_encounter);
        }
        else
        {
            // A downed or Incapacitated party member has no commands to give; ending
            // the turn is what the console client does, and the engine owns whatever
            // happens at the boundary — Death Saving Throws included.
            _encounter.EndTurn();
        }

        RefreshAfterAction(null);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetTree().Quit();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            HandleClick(click.Position);
        }
    }

    private void HandleClick(Vector2 pixel)
    {
        if (CommandedCombatant() is not { } active)
        {
            return;
        }

        foreach (var (rect, _, act) in _buttons)
        {
            if (rect.HasPoint(pixel))
            {
                Run(act);
                return;
            }
        }

        if (SquareAt(pixel) is not { } square)
        {
            return;
        }

        var occupant = _encounter.Combatants.FirstOrDefault(combatant =>
            !combatant.IsDead && combatant.Position == square);

        if (occupant is { } somebody && somebody.SideId != PregeneratedParty.SideId)
        {
            Run(() => AttackChoice.BestFor(active, somebody) is { } attack
                ? _encounter.Attack(attack.Name, somebody)
                : new ActionRefusal("client.no_attack", $"{active.Name} has no attack that reaches {somebody.Name}."));
        }
        else if (occupant is null)
        {
            // Sent to the engine whether or not it is highlighted: the refusal is the
            // rule, the highlight only advice.
            Run(() => _encounter.Move(square));
        }

        // Clicking an ally does nothing yet — administering a potion is a later slice.
    }

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

        // Where the active party member could walk. FindPath is the engine's own
        // reachability — allies cost double, enemies block, the budget is what is left
        // this turn — and the two condition gates mirror Move's early refusals so the
        // advice does not light squares the engine would refuse.
        if (CommandedCombatant() is { } mover
            && !mover.HasCondition(ConditionType.Prone)
            && ConditionRules.ImmobilisedBy(mover) is null)
        {
            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    var square = new GridPosition(x, y);

                    if (MovementRules.FindPath(
                            _encounter.Battlefield, mover, square, mover.Turn.MovementFeet, _encounter.Combatants)
                        is not null)
                    {
                        _reachable.Add(square);
                    }
                }
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        var active = _encounter.ActiveCombatant;
        var commanded = CommandedCombatant();

        DrawChrome(_subtitle, StatusLine(commanded));
        DrawGrid();

        // Advice under the tokens: where a walk could end, and who a click would attack.
        foreach (var square in _reachable)
        {
            DrawRect(
                new Rect2(GridLeft + (square.X * CellPixels), GridTop + (square.Y * CellPixels), CellPixels, CellPixels),
                new Color(PartyColour, 0.16f));
        }

        var tokens = TokensFrom(_encounter, _labels);

        if (commanded is not null)
        {
            foreach (var enemy in _encounter.EnemiesOf(commanded))
            {
                if (!enemy.IsDead && AttackChoice.BestFor(commanded, enemy) is not null)
                {
                    DrawCircle(CentreOf(enemy.Position), (CellPixels / 2f) - 4, MonsterColour, filled: false, width: 2);
                }
            }
        }

        DrawTokens(tokens, active?.Id);
        DrawTurnOrder(tokens, active?.Id);
        DrawLog(_encounter.Log, _encounter.Log.Count, tokens.Count);

        if (commanded is not null)
        {
            foreach (var (rect, caption, _) in _buttons)
            {
                DrawRect(rect, GridLine);
                DrawRect(rect, Dim, filled: false, width: 1);
                DrawString(
                    TextFont,
                    new Vector2(rect.Position.X + 11, rect.Position.Y + 19),
                    caption,
                    fontSize: 13,
                    modulate: Ink);
            }
        }

        if (_notice is { } notice)
        {
            var buttonsBottom = GridTop + (GridHeight * CellPixels) + 14 + 28;
            DrawString(
                TextFont,
                new Vector2(GridLeft, buttonsBottom + 24),
                Trim(notice, 78),
                fontSize: 13,
                modulate: MonsterColour);
        }
    }

    private string StatusLine(Combatant? commanded)
    {
        if (_encounter.IsComplete)
        {
            return _encounter.WinningSide == PregeneratedParty.SideId
                ? "the party wins — [esc] quit"
                : "the party has fallen — [esc] quit";
        }

        if (commanded is { } active)
        {
            var turn = active.Turn;
            return $"{active.Name}'s turn — Action {Tick(turn.HasAction)}  Bonus {Tick(turn.HasBonusAction)}  " +
                   $"Move {turn.MovementFeet} ft — click a square to move, an enemy to attack   [esc] quit";
        }

        return "the other side is acting…   [esc] quit";
    }

    private static string Tick(bool available) => available ? "✓" : "✗";

    /// <summary>
    /// Drives one commanded turn through the real input path and captures each result:
    /// walk toward the nearest enemy, swing at it, end the turn. How a change to this
    /// screen gets checked without a person clicking.
    /// </summary>
    private void RunProbeIfAsked()
    {
        if (_probeStarted || ArgumentValue("probe") is not { } directory)
        {
            return;
        }

        _probeStarted = true;
        RunProbe(directory);
    }

    private async void RunProbe(string directory)
    {
        await CaptureFrame(Path.Combine(directory, "play-1-turn-ready.png"));

        // A refusal on purpose — standing up while not Prone — because showing the
        // refusal with its code is a commitment, and a probe that only walks the happy
        // path would never notice the notice going missing.
        var standUp = _buttons.Single(button => button.Caption == "Stand Up");
        Click(standUp.Rect.GetCenter());
        await CaptureFrame(Path.Combine(directory, "play-2-refused.png"));

        if (CommandedCombatant() is { } active
            && _encounter.EnemiesOf(active).Where(enemy => !enemy.IsDead)
                .OrderBy(enemy => enemy.Position.DistanceFeetTo(active.Position))
                .FirstOrDefault() is { } target)
        {
            if (_reachable.Count > 0)
            {
                var step = _reachable
                    .OrderBy(square => square.DistanceFeetTo(target.Position))
                    .ThenBy(square => square.X).ThenBy(square => square.Y)
                    .First();

                Click(CentreOf(step));
                await CaptureFrame(Path.Combine(directory, "play-3-moved.png"));
            }

            Click(CentreOf(target.Position));
            await CaptureFrame(Path.Combine(directory, "play-4-attacked.png"));
        }

        var endTurn = _buttons.Single(button => button.Caption == "End Turn");
        Click(endTurn.Rect.GetCenter());
        await CaptureFrame(Path.Combine(directory, "play-5-turn-ended.png"));

        GetTree().Quit();
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
}
