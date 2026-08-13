using Godot;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Characters;
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
/// to <c>Move</c> and refused there, so the highlight is advice, never a rule. The same
/// goes for the buttons — they are filtered by what the character <i>has</i>, which is
/// display, while whether an action may happen now is always the engine's answer.
/// </para>
/// <para>
/// The one choice the client makes is which attack a click means, and it is the same
/// choice the console client makes, from the same shared code: the hardest-hitting
/// attack that reaches (<see cref="AttackChoice"/>). Potions reach for the weakest
/// carried for the console's reason too — spending a supreme potion on a scratch wastes
/// the difference, and that default decides nothing a rule cares about.
/// </para>
/// </remarks>
public partial class PlayMode : FightScreen
{
    /// <summary>What the next click on the grid means, when it is not a move or an attack.</summary>
    private enum Pending
    {
        Nothing,

        /// <summary>A spell was chosen; the next token clicked is its target.</summary>
        SpellTarget,

        /// <summary>Give Potion was pressed; the next ally clicked drinks it.</summary>
        PotionTarget,
    }

    private Encounter _encounter = null!;
    private Labels _labels = null!;
    private string _subtitle = string.Empty;
    private double _elapsed;
    private double _pace = SecondsPerTurn;
    private readonly HashSet<GridPosition> _reachable = [];
    private readonly List<(Rect2 Rect, string Caption, Func<ActionRefusal?> Act)> _buttons = [];
    private readonly List<(Rect2 Rect, SpellDefinition Spell)> _spellRows = [];
    private string? _buttonsFor;
    private bool _spellMenuOpen;
    private Pending _pending;
    private SpellDefinition? _pendingSpell;
    private string? _notice;
    private bool _probeStarted;

    protected override string Title => "SRD_Combat — playing a fight";

    private float ButtonRowTop => GridTop + (GridHeight * CellPixels) + 14f;

    protected override void OnReady()
    {
        var seed = SeedArgument();
        var fight = ResolveFight(seed);

        _encounter = fight.Encounter;
        _labels = Labels.For(_encounter.Combatants);
        AdoptBattlefield(_encounter);
        _subtitle = $"seed {seed} — the party against {RosterOf(fight)}";

        // A probe run drives the screen through its own input path — synthesized clicks
        // through the viewport — and captures what each one produced. Monsters hurry so
        // the probe spends its time on the party's turns, the part being verified.
        if (HasArgument("probe"))
        {
            _pace = 0.05;
        }

        RefreshAfterAction(null);
    }

    /// <summary>
    /// Two rows of buttons: the actions anybody can take, then what this character
    /// brought — features, spells, potions. Everything with a target is a click on the
    /// grid; Cast and Give Potion arm the next click instead.
    /// </summary>
    /// <remarks>
    /// Filtering by granted features is display, not a rule: a shown button can still be
    /// refused (no uses left, already moved), and the refusal is the answer. What must
    /// never happen is a button for a feature the character does not have doing nothing
    /// silently — absent is honest, inert is not.
    /// </remarks>
    private void BuildButtons(Combatant active)
    {
        _buttons.Clear();
        _buttonsFor = active.Id;

        var x = (float)GridLeft;

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
            x = AddButton(x, ButtonRowTop, caption, act);
        }

        x = GridLeft;
        var row2 = ButtonRowTop + 36;

        void FeatureButton(ClassFeature feature, string caption, Func<ActionRefusal?> act)
        {
            if (active.Stats.Has(feature))
            {
                x = AddButton(x, row2, caption, act);
            }
        }

        FeatureButton(ClassFeature.Rage, "Rage", () => _encounter.Rage());
        FeatureButton(ClassFeature.RecklessAttack, "Reckless", () => _encounter.RecklessAttack());
        FeatureButton(ClassFeature.SecondWind, "Second Wind", () => _encounter.SecondWind());
        FeatureButton(ClassFeature.ActionSurge, "Action Surge", () => _encounter.ActionSurge());
        FeatureButton(ClassFeature.SteadyAim, "Steady Aim", () => _encounter.SteadyAim());
        FeatureButton(ClassFeature.CunningAction, "Cunning Dash", () => _encounter.CunningAction(CunningActionKind.Dash));
        FeatureButton(ClassFeature.CunningAction, "Cunning Disengage", () => _encounter.CunningAction(CunningActionKind.Disengage));
        FeatureButton(ClassFeature.CunningStrike, "Trip", () => _encounter.CunningStrike(CunningStrikeEffect.Trip));

        if (active.Stats.Character?.CanCast == true)
        {
            x = AddButton(x, row2, "Cast", () =>
            {
                _spellMenuOpen = !_spellMenuOpen;
                return null;
            });
        }

        if (active.Inventory.TotalPotions > 0)
        {
            x = AddButton(x, row2, "Drink", () =>
                active.Inventory.Weakest is { } potency
                    ? _encounter.DrinkPotion(potency)
                    : new ActionRefusal("client.no_potion", $"{active.Name} carries no potions."));

            AddButton(x, row2, "Give Potion", () =>
            {
                _pending = Pending.PotionTarget;
                return null;
            });
        }
    }

    private float AddButton(float x, float y, string caption, Func<ActionRefusal?> act)
    {
        var width = TextFont.GetStringSize(caption, fontSize: 13).X + 22;
        _buttons.Add((new Rect2(x, y, width, 28), caption, act));
        return x + width + 8;
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
            // Esc backs out of whatever is armed before it quits anything.
            if (_pending != Pending.Nothing || _spellMenuOpen)
            {
                ClearPending();
                QueueRedraw();
                return;
            }

            GetTree().Quit();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            HandleClick(click.Position);
        }
    }

    private void ClearPending()
    {
        _pending = Pending.Nothing;
        _pendingSpell = null;
        _spellMenuOpen = false;
    }

    private void HandleClick(Vector2 pixel)
    {
        if (CommandedCombatant() is not { } active)
        {
            return;
        }

        // An armed click resolves first: the next token is the target, anywhere else
        // backs out. Cancelling must never cost anything, so nothing is spent until the
        // engine call itself.
        if (_pending == Pending.SpellTarget && _pendingSpell is { } spell)
        {
            var aimed = TokenTarget(pixel);
            ClearPending();

            if (aimed is { } target)
            {
                Run(() => _encounter.CastSpell(spell.Id, target));
            }
            else
            {
                _notice = null;
                QueueRedraw();
            }

            return;
        }

        if (_pending == Pending.PotionTarget)
        {
            var aimed = TokenTarget(pixel);
            ClearPending();

            if (aimed is { } target && active.Inventory.Weakest is { } potency)
            {
                Run(() => _encounter.DrinkPotion(potency, target));
            }
            else
            {
                _notice = null;
                QueueRedraw();
            }

            return;
        }

        if (_spellMenuOpen)
        {
            foreach (var (rect, chosen) in _spellRows)
            {
                if (rect.HasPoint(pixel))
                {
                    _spellMenuOpen = false;
                    _pendingSpell = chosen;
                    _pending = Pending.SpellTarget;
                    QueueRedraw();
                    return;
                }
            }
        }

        foreach (var (rect, _, act) in _buttons)
        {
            if (rect.HasPoint(pixel))
            {
                Run(act);
                return;
            }
        }

        // A click on the grid closes the menu rather than acting through it.
        if (_spellMenuOpen)
        {
            _spellMenuOpen = false;
            QueueRedraw();
            return;
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
    }

    /// <summary>The living combatant under a pixel, whichever side it is on.</summary>
    /// <remarks>
    /// Deliberately unfiltered: a heal aimed at an enemy or a potion poured at a range
    /// is the engine's to allow or refuse, and its answer teaches the rule.
    /// </remarks>
    private Combatant? TokenTarget(Vector2 pixel) =>
        SquareAt(pixel) is { } square
            ? _encounter.Combatants.FirstOrDefault(combatant =>
                !combatant.IsDead && combatant.Position == square)
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

        var commanded = CommandedCombatant();

        if (commanded is null || commanded.Id != _buttonsFor)
        {
            ClearPending();
        }

        if (commanded is not null && commanded.Id != _buttonsFor)
        {
            BuildButtons(commanded);
        }

        // Where the active party member could walk. FindPath is the engine's own
        // reachability — allies cost double, enemies block, the budget is what is left
        // this turn — and the two condition gates mirror Move's early refusals so the
        // advice does not light squares the engine would refuse.
        if (commanded is { } mover
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

        if (commanded is not null && _pending == Pending.Nothing)
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

        if (commanded is { } character)
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

            DrawString(
                TextFont,
                new Vector2(GridLeft, ButtonRowTop + 36 + 28 + 20),
                ResourceLine(character),
                fontSize: 12,
                modulate: Dim);

            DrawSpellMenu(character);
        }

        if (_notice is { } notice)
        {
            DrawString(
                TextFont,
                new Vector2(GridLeft, ButtonRowTop + 36 + 28 + 42),
                Trim(notice, 78),
                fontSize: 13,
                modulate: MonsterColour);
        }
    }

    /// <summary>What this character has left to spend. Read off the state, never computed.</summary>
    private static string ResourceLine(Combatant character)
    {
        var parts = new List<string>();

        var slots = character.Features.SpellSlotsRemaining
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .Select(pair => $"L{pair.Key} ×{pair.Value}")
            .ToArray();

        if (slots.Length > 0)
        {
            parts.Add("slots " + string.Join("  ", slots));
        }

        if (character.Stats.Has(ClassFeature.Rage))
        {
            parts.Add($"Rage ×{character.Features.RagesRemaining}");
        }

        if (character.Stats.Has(ClassFeature.SecondWind))
        {
            parts.Add($"Second Wind ×{character.Features.SecondWindRemaining}");
        }

        if (character.Stats.Has(ClassFeature.ActionSurge))
        {
            parts.Add($"Action Surge ×{character.Features.ActionSurgeRemaining}");
        }

        if (character.Inventory.TotalPotions > 0)
        {
            parts.Add($"potions ×{character.Inventory.TotalPotions}");
        }

        return string.Join(" · ", parts);
    }

    private void DrawSpellMenu(Combatant character)
    {
        _spellRows.Clear();

        if (!_spellMenuOpen || character.Stats.Character is not { } features)
        {
            return;
        }

        var top = ButtonRowTop + 36 + 28 + 54;

        DrawString(TextFont, new Vector2(GridLeft, top - 6), "SPELLS — click one, then its target", fontSize: 12, modulate: Dim);

        var y = top + 6;

        foreach (var spell in features.Spells.OrderBy(spell => spell.Level).ThenBy(spell => spell.Name, StringComparer.Ordinal))
        {
            var rect = new Rect2(GridLeft, y, 260, 20);
            _spellRows.Add((rect, spell));

            DrawRect(rect, GridLine);
            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 15),
                spell.IsCantrip ? $"{spell.Name} — cantrip" : $"{spell.Name} — level {spell.Level}",
                fontSize: 12,
                modulate: Ink);

            y += 24;
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

        if (_pending == Pending.SpellTarget && _pendingSpell is { } spell)
        {
            return $"choose a target for {spell.Name} — click anywhere else to cancel";
        }

        if (_pending == Pending.PotionTarget)
        {
            return "choose who drinks the potion — click anywhere else to cancel";
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
    /// Drives commanded turns through the real input path and captures each result:
    /// a refusal on purpose, a walk, a swing, a feature, and — when a caster's turn
    /// comes — the spell menu and a cast. How a change to this screen gets checked
    /// without a person clicking.
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
        ClickButton("Stand Up");
        await CaptureFrame(Path.Combine(directory, "play-2-refused.png"));

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
                await CaptureFrame(Path.Combine(directory, "play-3-moved.png"));
            }

            Click(CentreOf(target.Position));
            await CaptureFrame(Path.Combine(directory, "play-4-attacked.png"));
        }

        // A feature if this character brought one — Cunning Dash succeeds after a move,
        // so prefer it; otherwise whatever the second row offers, refusals included.
        var feature = _buttons.FirstOrDefault(button => button.Caption == "Cunning Dash").Caption
            ?? _buttons.FirstOrDefault(button => button.Rect.Position.Y > ButtonRowTop + 1).Caption;

        if (feature is not null)
        {
            ClickButton(feature);
            await CaptureFrame(Path.Combine(directory, "play-5-feature.png"));
        }

        ClickButton("End Turn");
        await CaptureFrame(Path.Combine(directory, "play-6-turn-ended.png"));

        // Play on to the next commanded turn; if it belongs to a caster, walk the cast
        // flow — menu, choice, target — through the same input path as everything else.
        await NextCommandedTurn();

        if (CommandedCombatant() is { } caster
            && caster.Stats.Character?.CanCast == true
            && NearestEnemyOf(caster) is { } victim)
        {
            ClickButton("Cast");
            await CaptureFrame(Path.Combine(directory, "play-7-spell-menu.png"));

            if (_spellRows.Count > 0)
            {
                Click(_spellRows[0].Rect.GetCenter());
                Click(CentreOf(victim.Position));
                await CaptureFrame(Path.Combine(directory, "play-8-cast.png"));
            }
        }

        GetTree().Quit();
    }

    private Combatant? NearestEnemyOf(Combatant active) =>
        _encounter.EnemiesOf(active)
            .Where(enemy => !enemy.IsDead)
            .OrderBy(enemy => enemy.Position.DistanceFeetTo(active.Position))
            .FirstOrDefault();

    private async Task NextCommandedTurn()
    {
        var waited = 0;

        while (CommandedCombatant() is null && !_encounter.IsComplete && waited < 3000)
        {
            waited++;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void ClickButton(string caption)
    {
        var button = _buttons.FirstOrDefault(candidate => candidate.Caption == caption);

        if (button.Caption == caption)
        {
            Click(button.Rect.GetCenter());
        }
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
