using Godot;
using SRDCombat.Core.Combat;
using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Viewer;

/// <summary>
/// Plays the gauntlet with the mouse: the party's turns wait for the player, every other
/// side is taken by <see cref="SimpleTacticsPolicy"/>, one turn per beat so it can be
/// watched, and between fights an interlude carries the run — the rest taken, who came
/// back, who levelled, what was found — exactly as the console client narrates it.
/// <c>--one-fight</c> plays a single encounter instead, and the run autosaves after
/// every cleared fight so <c>--continue</c> resumes it.
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
/// <para>
/// The run itself is all <see cref="GauntletRun"/>'s: rests, experience, levelling,
/// loot and the autosave format live in <c>Game</c>, and this screen only shows what
/// the run reports and asks it to begin the next fight.
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

        /// <summary>A named attack was chosen; the next enemy clicked takes it.</summary>
        AttackTarget,

        /// <summary>Give Potion was pressed; the next ally clicked drinks it.</summary>
        PotionTarget,

        /// <summary>Divine Spark (heal) was pressed; the next ally clicked is restored.</summary>
        SparkHealTarget,

        /// <summary>Divine Spark (harm) was pressed; the next enemy clicked saves or burns.</summary>
        SparkHarmTarget,
    }

    /// <summary>Where the screen is: in a fight, between fights, or after the run.</summary>
    private enum Phase
    {
        Fighting,
        Interlude,
        RunOver,
    }

    private Encounter? _encounter;
    private Labels _labels = null!;
    private string _subtitle = string.Empty;
    private int _seed;
    private double _elapsed;
    private double _pace = SecondsPerTurn;
    private readonly HashSet<GridPosition> _reachable = [];

    /// <summary>Squares the active character has Total Cover against — fogged, not merely dim.</summary>
    private readonly HashSet<GridPosition> _blocked = [];

    /// <summary>
    /// The keyboard's place on the board. Arrow keys move it, Enter acts on it, and it
    /// resolves through the very same path a click does — so the two ways of playing
    /// can never mean different things.
    /// </summary>
    private GridPosition? _cursor;

    /// <summary>
    /// The highlighted row of whichever menu is open. Arrows move it and Enter takes
    /// it, so choosing a spell never has to reach for the mouse.
    /// </summary>
    private int _menuIndex;
    private readonly List<(Rect2 Rect, string Caption, Func<ActionRefusal?> Act)> _buttons = [];
    private readonly List<(Rect2 Rect, SpellDefinition Spell)> _spellRows = [];
    private readonly List<(Rect2 Rect, CombatAttack Attack)> _attackRows = [];
    private readonly List<(Rect2 Rect, int Level)> _slotRows = [];
    private string? _buttonsFor;
    private bool _spellMenuOpen;
    private bool _attackMenuOpen;
    private bool _slotMenuOpen;
    private Pending _pending;
    private SpellDefinition? _pendingSpell;
    private CombatAttack? _pendingAttack;
    private int? _pendingSlot;
    private string? _notice;
    private bool _probeStarted;

    /// <summary>How much of the fight's log has already been scanned for walks to play.</summary>
    private int _walkStepsSeen;

    /// <summary>
    /// Off during a probe: the probe reads a capture the instant after each click, and a
    /// token photographed mid-hop is a token that looks like it never arrived. The same
    /// reason the monsters hurry.
    /// </summary>
    private bool _animateWalks = true;

    private GauntletRun? _run;
    private Fight? _fight;
    private SeededRandomSource _dice = null!;
    private string _savePath = "srdcombat-save.json";

    /// <summary>
    /// A created party's drafts, set by <see cref="CreateMode"/> before this node is
    /// added. Null means the pregens, exactly as before creation existed.
    /// </summary>
    public IReadOnlyList<CharacterDraft>? CreatedDrafts { get; init; }
    private Phase _phase = Phase.Fighting;
    private readonly List<string> _interlude = [];
    private SrdContent? _content;
    private Rect2 _continueButton;
    private Rect2 _shopButton;
    private Rect2 _shopBackButton;
    private bool _shopAvailable;
    private bool _shopView;
    private string? _shopNotice;
    private readonly List<(Rect2 Rect, ShopOffer Offer)> _shopRows = [];
    private bool _fightEndHandled;

    protected override string Title => "SRD_Combat — playing";

    /// <summary>Baseline of the active-combatant banner, directly under the grid.</summary>
    private float BannerTop => GridTop + (GridHeight * CellPixels) + 20f;

    private float ButtonRowTop => BannerTop + 42f;

    /// <summary>
    /// How wide a shop row is. Generous on purpose: an offer's effect line names both
    /// weapons with their whole damage expressions, and a row that clipped it would
    /// hide the very number the shopper opened the stall to compare.
    /// </summary>
    private const int ShopRowWidth = 700;

    protected override void OnReady()
    {
        _seed = SeedArgument();

        // A probe run drives the screen through its own input path — synthesized clicks
        // through the viewport — and captures what each one produced. Monsters hurry so
        // the probe spends its time on the party's turns, the part being verified.
        if (HasArgument("probe"))
        {
            _pace = 0.05;
            _animateWalks = false;
        }

        if (HasArgument("one-fight"))
        {
            var fight = ResolveFight(_seed);

            _fight = null;
            _encounter = fight.Encounter;
            _labels = Labels.For(_encounter.Combatants);
            AdoptBattlefield(_encounter);
            _subtitle = $"one fight — seed {_seed} — the party against {RosterOf(fight)}";
            _walkStepsSeen = 0;

            RefreshAfterAction(null);
            return;
        }

        var content = LoadContent();
        _content = content;

        _dice = new SeededRandomSource(_seed);
        _savePath = ArgumentValue("save") ?? "srdcombat-save.json";

        if (HasArgument("continue"))
        {
            // A save that cannot be read is shown and nothing is started: silently
            // beginning a fresh run here would overwrite the file being asked about.
            if (!File.Exists(_savePath))
            {
                _phase = Phase.RunOver;
                _interlude.Add($"No save at '{_savePath}'. Pass --save=<path> or start a new run.");
                _subtitle = $"seed {_seed}";
                return;
            }

            try
            {
                _run = GauntletRun.Resume(content, RunSave.FromJson(File.ReadAllText(_savePath)));
            }
            catch (Exception failure) when (failure is System.Text.Json.JsonException or InvalidDataException)
            {
                _phase = Phase.RunOver;
                _interlude.Add($"Cannot load '{_savePath}': {failure.Message}");
                _subtitle = $"seed {_seed}";
                return;
            }

            _subtitle = $"continuing after fight {_run.Cleared} of {_run.Ladder.Count} — seed {_seed}";
        }
        else
        {
            var level = ArgumentValue("level") is { } text && int.TryParse(text, out var parsed)
                ? Math.Clamp(parsed, 1, 5)
                : 1;

            _run = CreatedDrafts is not null
                ? GauntletRun.Start(content, CreatedDrafts)
                : GauntletRun.Start(content, GauntletLadder.Default(), level);
            _subtitle = $"a gauntlet of {_run.Ladder.Count} fights — seed {_seed}";
        }

        EnterInterlude([]);
    }

    /// <summary>
    /// Sets up the between-fights screen: what the last fight left behind, then what the
    /// run says about the next one — the rest taken, who returned, how everybody stands.
    /// </summary>
    /// <remarks>
    /// The wording deliberately matches the console client's, because both are only
    /// repeating what <see cref="GauntletRun"/> reports. The save was already written
    /// when the fight completed; the rest applied here lives in the run's memory and is
    /// reapplied on resume, exactly as the console's loop ordering has it.
    /// </remarks>
    private void EnterInterlude(IEnumerable<string> after)
    {
        _interlude.Clear();
        _interlude.AddRange(after);
        _phase = Phase.Interlude;

        if (_run is not { } run)
        {
            _phase = Phase.RunOver;
            QueueRedraw();
            return;
        }

        if (run.Outcome == RunOutcome.Survived)
        {
            _interlude.Add(string.Empty);
            _interlude.Add($"The gauntlet is beaten — {run.Ladder.Count} fights cleared.");
            AddFallen(run);
            _phase = Phase.RunOver;
        }
        else if (run.Outcome == RunOutcome.Defeated)
        {
            _interlude.Add(string.Empty);
            _interlude.Add($"The run ends after {run.Cleared} fight(s).");

            if (run.Cleared > 0)
            {
                _interlude.Add("Launch with --continue to retry from after the last cleared fight.");
            }

            AddFallen(run);
            _phase = Phase.RunOver;
        }
        else if (run.Next is { } step)
        {
            var returnsBefore = run.Returns.Count;
            var rest = run.PrepareForNext(_dice);

            _interlude.Add(string.Empty);
            _interlude.Add(
                $"Fight {run.Cleared + 1} of {run.Ladder.Count} — " +
                $"{step.Difficulty.ToString().ToLowerInvariant()} difficulty.");

            if (rest is { } taken)
            {
                _interlude.Add($"The party takes a {taken} Rest.");
            }

            // The merchant reaches the party at each Long Rest, exactly as the
            // console's shop does. The button is the door; nothing is automatic.
            _shopAvailable = rest == RestKind.Long;
            _shopView = false;
            _shopNotice = null;

            foreach (var returned in run.Returns.Skip(returnsBefore))
            {
                _interlude.Add(returned + ".");
            }

            _interlude.Add(string.Empty);

            foreach (var (member, state) in run.Party.Zip(run.States))
            {
                _interlude.Add(state.IsDead
                    ? $"{member.Draft.Name} — dead"
                    : $"{member.Draft.Name} — level {state.Level}, " +
                      $"{state.CurrentHitPoints}/{member.Sheet.MaximumHitPoints} hp, " +
                      $"{state.HitDiceRemaining} hit {(state.HitDiceRemaining == 1 ? "die" : "dice")}, " +
                      $"{state.ExperiencePoints} xp");
            }
        }

        QueueRedraw();
    }

    private void AddFallen(GauntletRun run)
    {
        var fallen = run.Fallen.ToArray();

        if (fallen.Length > 0)
        {
            _interlude.Add("Fallen: " + string.Join(", ", fallen) + ".");
        }
        else if (run.Casualties.Count > 0)
        {
            _interlude.Add($"Everyone made it, though {run.Casualties.Count} went down along the way.");
        }
    }

    private void StartNextFight()
    {
        if (_run is not { } run || run.Next is null)
        {
            return;
        }

        var fight = run.BeginNext(_dice);

        _fight = fight;
        _encounter = fight.Encounter;
        _labels = Labels.For(_encounter.Combatants);
        AdoptBattlefield(_encounter);
        _subtitle = $"fight {run.Cleared + 1} of {run.Ladder.Count} — seed {_seed} — the party against {RosterOf(fight)}";
        _phase = Phase.Fighting;
        _fightEndHandled = false;
        _buttonsFor = null;

        // A fresh fight is a fresh log: nothing has been scanned for walks, and any
        // hop the last fight left half-played dies with it.
        _walkStepsSeen = 0;
        ClearWalks();

        RefreshAfterAction(null);
    }

    /// <summary>
    /// Reports a finished fight to the run and saves. The save happens only after a
    /// cleared fight, never after the defeat itself — the file keeps the last state
    /// worth returning to, which is what makes reloading a retry.
    /// </summary>
    private void HandleFightEnd()
    {
        if (_fightEndHandled)
        {
            return;
        }

        _fightEndHandled = true;

        if (_run is not { } run || _fight is not { } fight || _encounter is not { } encounter)
        {
            QueueRedraw();
            return;
        }

        var levelUpsBefore = run.LevelUps.Count;
        var lootBefore = run.LootFound.Count;

        run.CompleteFight(fight, _dice);

        var after = new List<string>
        {
            encounter.WinningSide == PregeneratedParty.SideId
                ? $"Fight {run.Cleared} cleared!"
                : "The party falls.",
        };

        after.AddRange(run.LevelUps.Skip(levelUpsBefore).Select(line => line + "!"));
        after.AddRange(run.LootFound.Skip(lootBefore).Select(line => line + "!"));

        if (run.Outcome != RunOutcome.Defeated)
        {
            File.WriteAllText(_savePath, RunSave.ToJson(run));
            after.Add($"Saved to {_savePath}.");
        }

        EnterInterlude(after);
    }

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
        _buttonsFor = active.Id;

        if (_encounter is not { } encounter)
        {
            return;
        }

        // Row one is what anybody can do; row two is what this character brought.
        var universal = new[]
        {
            TurnAction.Dodge, TurnAction.Dash, TurnAction.Disengage,
            TurnAction.StandUp, TurnAction.Escape, TurnAction.EndTurn,
        };

        var x = (float)GridLeft;

        foreach (var action in TurnOptions.For(encounter, active).Where(universal.Contains))
        {
            x = AddButton(x, ButtonRowTop, action);
        }

        x = GridLeft;

        foreach (var action in TurnOptions.For(encounter, active).Where(action => !universal.Contains(action)))
        {
            x = AddButton(x, ButtonRowTop + 36, action);
        }
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
                _spellMenuOpen = false;
                _menuIndex = 0;

                // With one attack there is nothing to choose, so it arms targeting
                // straight away; the menu is for characters carrying a choice.
                if (CommandedCombatant() is { } swinging && swinging.Stats.Attacks.Count == 1)
                {
                    _attackMenuOpen = false;
                    _pendingAttack = swinging.Stats.Attacks[0];
                    _pending = Pending.AttackTarget;
                    return null;
                }

                _attackMenuOpen = !_attackMenuOpen;
                return null;

            case TurnAction.Cast:
                _spellMenuOpen = !_spellMenuOpen;
                _attackMenuOpen = false;
                _menuIndex = 0;
                return null;

            case TurnAction.Drink:
                return CommandedCombatant()?.Inventory.Weakest is { } potency
                    ? encounter.DrinkPotion(potency)
                    : new ActionRefusal("client.no_potion", "Nothing to drink.");

            case TurnAction.GivePotion: _pending = Pending.PotionTarget; return null;
            case TurnAction.DivineSparkHeal: _pending = Pending.SparkHealTarget; return null;
            case TurnAction.DivineSparkHarm: _pending = Pending.SparkHarmTarget; return null;

            default: return null;
        }
    }

    /// <summary>Takes a spell off the menu: the slot choice if there is one, else the target.</summary>
    private void ChooseSpell(SpellDefinition spell)
    {
        _spellMenuOpen = false;
        _pendingSpell = spell;
        _menuIndex = 0;

        // A slotted spell with more than one slot level to burn is a real choice; one
        // level, or a cantrip, arms straight away and the engine picks as it always has.
        if (CommandedCombatant() is { } caster && SlotLevelsFor(caster, spell).Count > 1)
        {
            _slotMenuOpen = true;
        }
        else
        {
            _pending = Pending.SpellTarget;
        }

        QueueRedraw();
    }

    private void ChooseSlot(int level)
    {
        _slotMenuOpen = false;
        _pendingSlot = level;
        _pending = Pending.SpellTarget;
        QueueRedraw();
    }

    private void ChooseAttack(CombatAttack attack)
    {
        _attackMenuOpen = false;
        _pendingAttack = attack;
        _pending = Pending.AttackTarget;
        QueueRedraw();
    }

    /// <summary>How many rows the open menu has, or zero when none is open.</summary>
    private int OpenMenuLength =>
        _spellMenuOpen ? _spellRows.Count
        : _slotMenuOpen ? _slotRows.Count
        : _attackMenuOpen ? _attackRows.Count
        : 0;

    /// <summary>Takes the highlighted row of whichever menu is open.</summary>
    private void TakeHighlightedRow()
    {
        if (_spellMenuOpen && _menuIndex < _spellRows.Count)
        {
            ChooseSpell(_spellRows[_menuIndex].Spell);
        }
        else if (_slotMenuOpen && _menuIndex < _slotRows.Count)
        {
            ChooseSlot(_slotRows[_menuIndex].Level);
        }
        else if (_attackMenuOpen && _menuIndex < _attackRows.Count)
        {
            ChooseAttack(_attackRows[_menuIndex].Attack);
        }
    }

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
        AddButton(x, y, $"{TurnOptions.HotkeyLabel(action)} · {TurnOptions.Caption(action)}", () => Invoke(action));

    private float AddButton(float x, float y, string caption, Func<ActionRefusal?> act)
    {
        var width = TextFont.GetStringSize(caption, fontSize: 13).X + 22;
        _buttons.Add((new Rect2(x, y, width, 28), caption, act));
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

    public override void _Process(double delta)
    {
        if (_phase != Phase.Fighting || _encounter is not { } encounter)
        {
            RunProbeIfAsked();
            return;
        }

        // The idle and walk loops tick whatever else the beat is doing — a fight where
        // nothing is happening is still a fight where everyone is breathing.
        if (AdvanceSpriteAnimation(delta))
        {
            QueueRedraw();
        }

        // A walk plays out before anything else happens: the token hops square to
        // square, and the next beat — the policy's turn, the fight's end — waits for it.
        if (AdvanceWalks(delta))
        {
            QueueRedraw();
        }

        if (WalkInProgress)
        {
            return;
        }

        if (encounter.IsComplete)
        {
            HandleFightEnd();
            RunProbeIfAsked();
            return;
        }

        if (encounter.ActiveCombatant is not { } active)
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
            SimpleTacticsPolicy.TakeTurn(encounter);
        }
        else
        {
            // A downed or Incapacitated party member has no commands to give; ending
            // the turn is what the console client does, and the engine owns whatever
            // happens at the boundary — Death Saving Throws included.
            encounter.EndTurn();
        }

        RefreshAfterAction(null);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            // Esc backs out of whatever is armed before it quits anything — the
            // merchant's stall included.
            if (_shopView)
            {
                _shopView = false;
                _shopNotice = null;
                QueueRedraw();
                return;
            }

            if (_pending != Pending.Nothing || _spellMenuOpen || _attackMenuOpen || _slotMenuOpen)
            {
                ClearPending();
                QueueRedraw();
                return;
            }

            GetTree().Quit();
            return;
        }

        if (@event is InputEventKey { Pressed: true } key && _phase == Phase.Fighting && !_shopView)
        {
            // The board under the keyboard: arrows walk a cursor, Enter acts on it
            // through the same path a click takes. The cursor appears the moment it is
            // asked for, starting on the character whose turn it is.
            var step = key.Keycode switch
            {
                Key.Left => new GridPosition(-1, 0),
                Key.Right => new GridPosition(1, 0),
                Key.Up => new GridPosition(0, -1),
                Key.Down => new GridPosition(0, 1),
                _ => (GridPosition?)null,
            };

            // An open menu takes the arrows first: while a spell list is up, Up and Down
            // belong to it rather than to the board behind it.
            if (OpenMenuLength is > 0 and var rows)
            {
                if (step is { } scroll && scroll.X == 0)
                {
                    _menuIndex = Math.Clamp(_menuIndex + scroll.Y, 0, rows - 1);
                    QueueRedraw();
                    return;
                }

                if (key.Keycode is Key.Enter or Key.KpEnter)
                {
                    TakeHighlightedRow();
                    return;
                }
            }

            if (step is { } move && CommandedCombatant() is { } walker)
            {
                var from = _cursor ?? walker.Position;

                _cursor = new GridPosition(
                    Math.Clamp(from.X + move.X, 0, GridWidth - 1),
                    Math.Clamp(from.Y + move.Y, 0, GridHeight - 1));

                QueueRedraw();
                return;
            }

            if (key.Keycode is Key.Enter or Key.KpEnter && _cursor is { } chosen)
            {
                ActivateSquare(chosen);
                return;
            }

            // A key runs exactly what its button would, and only while that button is
            // shown — so a keypress can never reach an action the row is hiding.
            if (_pending == Pending.Nothing)
            {
                var typed = key.Keycode == Key.Space ? ' ' : (char)key.Keycode;

                if (ActionForKey(typed) is { } action)
                {
                    Run(() => Invoke(action));
                    return;
                }
            }
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
        _pendingAttack = null;
        _pendingSlot = null;
        _spellMenuOpen = false;
        _attackMenuOpen = false;
        _slotMenuOpen = false;
    }

    private void HandleClick(Vector2 pixel)
    {
        if (_phase == Phase.Interlude)
        {
            if (_shopView && _run is { } shopping)
            {
                if (_shopBackButton.HasPoint(pixel))
                {
                    _shopView = false;
                    _shopNotice = null;
                    QueueRedraw();
                    return;
                }

                foreach (var (rect, offer) in _shopRows)
                {
                    if (rect.HasPoint(pixel))
                    {
                        // The engine's answer either way: a purchase re-lists the
                        // stall with the purse lighter, a refusal is shown with its
                        // code like every other rule.
                        _shopNotice = shopping.Purchase(offer) is { } refusal
                            ? $"[{refusal.Code}] {refusal.Message}"
                            : $"Bought: {offer.Description}.";
                        QueueRedraw();
                        return;
                    }
                }

                return;
            }

            if (_shopAvailable && _shopButton.HasPoint(pixel))
            {
                _shopView = true;
                QueueRedraw();
                return;
            }

            if (_continueButton.HasPoint(pixel))
            {
                StartNextFight();
            }

            return;
        }

        if (CommandedCombatant() is not { } active || _encounter is not { } encounter)
        {
            return;
        }

        // An armed click resolves first: the next token is the target, anywhere else
        // backs out. Cancelling must never cost anything, so nothing is spent until the
        // engine call itself.
        if (_pending != Pending.Nothing)
        {
            ActivateSquare(SquareAt(pixel));
            return;
        }

        if (_spellMenuOpen)
        {
            foreach (var (rect, chosen) in _spellRows)
            {
                if (rect.HasPoint(pixel))
                {
                    ChooseSpell(chosen);
                    return;
                }
            }
        }

        if (_slotMenuOpen)
        {
            foreach (var (rect, level) in _slotRows)
            {
                if (rect.HasPoint(pixel))
                {
                    ChooseSlot(level);
                    return;
                }
            }
        }

        if (_attackMenuOpen)
        {
            foreach (var (rect, chosen) in _attackRows)
            {
                if (rect.HasPoint(pixel))
                {
                    ChooseAttack(chosen);
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

        // A click on the grid closes an open menu rather than acting through it.
        if (_spellMenuOpen || _attackMenuOpen || _slotMenuOpen)
        {
            _spellMenuOpen = false;
            _attackMenuOpen = false;
            _slotMenuOpen = false;
            _pendingSpell = null;
            QueueRedraw();
            return;
        }

        ActivateSquare(SquareAt(pixel));
    }

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

        if (_pending == Pending.AttackTarget && _pendingAttack is { } chosenAttack)
        {
            var struck = TokenAt(square);
            ClearPending();

            if (struck is { } victim)
            {
                Run(() => encounter.Attack(chosenAttack.Name, victim));
            }
            else
            {
                _notice = null;
                QueueRedraw();
            }

            return;
        }

        if (_pending == Pending.SpellTarget && _pendingSpell is { } spell)
        {
            var aimed = TokenAt(square);
            var ground = square;
            var slot = _pendingSlot;
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

        if (_pending == Pending.PotionTarget)
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

        if (_pending is Pending.SparkHealTarget or Pending.SparkHarmTarget)
        {
            var aimed = TokenAt(square);
            var mode = _pending == Pending.SparkHealTarget ? DivineSparkUse.Heal : DivineSparkUse.Harm;
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
        else if (occupant is null && at is not null)
        {
            // Sent to the engine whether or not it is highlighted: the refusal is the
            // rule, the highlight only advice.
            Run(() => encounter.Move(square));
        }
    }

    /// <summary>The living combatant standing on a square, whichever side it is on.</summary>
    private Combatant? TokenAt(GridPosition square) =>
        _encounter?.Combatants.FirstOrDefault(combatant => !combatant.IsDead && combatant.Position == square);

    /// <summary>The living combatant under a pixel, whichever side it is on.</summary>
    /// <remarks>
    /// Deliberately unfiltered: a heal aimed at an enemy or a potion poured at a range
    /// is the engine's to allow or refuse, and its answer teaches the rule.
    /// </remarks>
    private Combatant? TokenTarget(Vector2 pixel) =>
        SquareAt(pixel) is { } square && _encounter is { } encounter
            ? encounter.Combatants.FirstOrDefault(combatant =>
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
        _blocked.Clear();

        // Whatever just happened, any walk it wrote gets played: the Move step carries
        // the route, and the token hops it instead of teleporting.
        if (_encounter is { } fought)
        {
            if (_animateWalks)
            {
                QueueWalks(fought.Log, _walkStepsSeen, fought.Log.Count);
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

        // Squares this character has Total Cover against — nothing standing there can be
        // shot, targeted by a spell, or caught by an area, and the engine refuses all
        // three. Shading them says so before a turn is spent finding out. CoverRules is
        // the engine's own judgement; this only asks it a question per square.
        if (commanded is { } shooter && _encounter is { } field)
        {
            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    var square = new GridPosition(x, y);

                    if (square != shooter.Position
                        && CoverRules.Between(field.Battlefield, shooter.Position, square, field.Combatants)
                            == CoverDegree.Total)
                    {
                        _blocked.Add(square);
                    }
                }
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_phase != Phase.Fighting || _encounter is not { } encounter)
        {
            DrawChrome(_subtitle, StatusLine(null));
            DrawInterlude();
            return;
        }

        var active = encounter.ActiveCombatant;
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

        // Fog over what this character cannot reach with anything at range: Total Cover
        // refuses an attack, a spell and an area alike, so a square behind the wall is
        // not a target and should not look like one.
        foreach (var square in _blocked)
        {
            DrawRect(
                new Rect2(GridLeft + (square.X * CellPixels), GridTop + (square.Y * CellPixels), CellPixels, CellPixels),
                new Color(0f, 0f, 0f, 0.55f));
        }

        // The keyboard's cursor, drawn over the advice so it is never lost in it.
        if (_cursor is { } caret)
        {
            DrawRect(
                new Rect2(GridLeft + (caret.X * CellPixels), GridTop + (caret.Y * CellPixels), CellPixels, CellPixels),
                ActiveRing,
                filled: false,
                width: 3f);
        }

        var tokens = WithWalk(TokensFrom(encounter, _labels));

        if (commanded is not null && _pending == Pending.Nothing)
        {
            foreach (var enemy in encounter.EnemiesOf(commanded))
            {
                if (!enemy.IsDead && AttackChoice.BestFor(commanded, enemy, encounter.Combatants) is not null)
                {
                    DrawCircle(CentreOf(enemy.Position), (CellPixels / 2f) - 4, MonsterColour, filled: false, width: 2);
                }
            }
        }

        DrawTokens(tokens, active?.Id);
        DrawTurnOrder(tokens, active?.Id);
        DrawLog(encounter.Log, encounter.Log.Count, tokens.Count);

        // Who is up, and with what — class and level for a character, AC, hit points,
        // and the attacks they carry. TurnBanner composes it so the console client and
        // this screen cannot drift; the letter is this fight's label for the token.
        if (active is { } upNow)
        {
            var lines = TurnBanner.Lines(upNow);
            var colour = upNow.SideId == PregeneratedParty.SideId ? PartyColour : MonsterColour;

            DrawString(
                TextFont,
                new Vector2(GridLeft, BannerTop),
                Trim($"{_labels.Of(upNow)}  {lines[0]}", 90),
                fontSize: 13,
                modulate: colour);

            if (lines.Count > 1)
            {
                DrawString(
                    TextFont,
                    new Vector2(GridLeft, BannerTop + 18),
                    Trim(lines[1], 95),
                    fontSize: 12,
                    modulate: Dim);
            }
        }

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
            DrawAttackMenu(character);
            DrawSlotMenu(character);
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

    /// <summary>The between-fights screen: the run's own words, and a way onward.</summary>
    private void DrawInterlude()
    {
        if (_shopView && _run is { } shopping)
        {
            DrawShop(shopping);
            return;
        }

        var y = GridTop + 8f;

        foreach (var line in _interlude)
        {
            if (line.Length == 0)
            {
                y += 10;
                continue;
            }

            DrawString(TextFont, new Vector2(GridLeft, y), Trim(line, 100), fontSize: 14, modulate: Ink);
            y += 22;
        }

        if (_phase == Phase.Interlude)
        {
            _continueButton = new Rect2(GridLeft, y + 16, 110, 32);

            DrawRect(_continueButton, GridLine);
            DrawRect(_continueButton, Dim, filled: false, width: 1);
            DrawString(
                TextFont,
                new Vector2(_continueButton.Position.X + 18, _continueButton.Position.Y + 21),
                "Continue",
                fontSize: 14,
                modulate: Ink);

            if (_shopAvailable)
            {
                _shopButton = new Rect2(GridLeft + 126, y + 16, 110, 32);

                DrawRect(_shopButton, GridLine);
                DrawRect(_shopButton, Dim, filled: false, width: 1);
                DrawString(
                    TextFont,
                    new Vector2(_shopButton.Position.X + 30, _shopButton.Position.Y + 21),
                    "Shop",
                    fontSize: 14,
                    modulate: Ink);
            }
        }
    }

    /// <summary>
    /// The merchant's stall: every offer at its printed price, the purse in the
    /// header, the unaffordable dimmed the way the console stars them — a thing worth
    /// saving toward is worth seeing.
    /// </summary>
    private void DrawShop(GauntletRun run)
    {
        _shopRows.Clear();

        var offers = Shop.Offers(_content!, run.Party, run.States);
        var y = GridTop + 8f;

        DrawString(
            TextFont,
            new Vector2(GridLeft, y),
            $"A merchant is here. The purse holds {Shop.Price(run.GoldCopper)}. Click to buy.",
            fontSize: 14,
            modulate: Ink);

        y += 26;

        if (offers.Count == 0)
        {
            DrawString(
                TextFont,
                new Vector2(GridLeft, y),
                "Nothing here would improve anybody.",
                fontSize: 13,
                modulate: Dim);
            y += 22;
        }

        foreach (var offer in offers)
        {
            // What the price buys, under the price. The lines are the offer's own —
            // a shopper choosing between a suit of armor and a blade is comparing
            // rules, and rules are never this client's to compute.
            var effects = offer.Effect.Lines;
            var affordable = offer.CostCopper <= run.GoldCopper;
            var rect = new Rect2(GridLeft, y, ShopRowWidth, 19 + (effects.Count * 15));

            _shopRows.Add((rect, offer));

            DrawRect(rect, GridLine);
            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 14),
                offer.Description,
                fontSize: 12,
                modulate: affordable ? Ink : Dim);

            var line = rect.Position.Y + 28;

            foreach (var effect in effects)
            {
                DrawString(
                    TextFont,
                    new Vector2(rect.Position.X + 20, line),
                    effect,
                    fontSize: 11,
                    modulate: affordable ? Dim : new Color(Dim, 0.55f));
                line += 15;
            }

            y += rect.Size.Y + 4;
        }

        if (_shopNotice is { } notice)
        {
            y += 6;
            DrawString(TextFont, new Vector2(GridLeft, y + 12), Trim(notice, 78), fontSize: 13, modulate: MonsterColour);
            y += 18;
        }

        _shopBackButton = new Rect2(GridLeft, y + 12, 110, 32);

        DrawRect(_shopBackButton, GridLine);
        DrawRect(_shopBackButton, Dim, filled: false, width: 1);
        DrawString(
            TextFont,
            new Vector2(_shopBackButton.Position.X + 30, _shopBackButton.Position.Y + 21),
            "Back",
            fontSize: 14,
            modulate: Ink);
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

        if (character.Stats.Has(ClassFeature.ChannelDivinity))
        {
            parts.Add($"Channel Divinity ×{character.Features.ChannelDivinityRemaining}");
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

        DrawString(TextFont, new Vector2(GridLeft, top - 6), "SPELLS — click one, or arrows and Enter", fontSize: 12, modulate: Dim);

        var y = top + 6;

        // Only what could be cast this instant — the same rule that decides whether
        // the Cast button is there at all, so the list can never offer a row whose only
        // possible answer is a refusal.
        var castable = features.Spells
            .Where(spell => TurnOptions.CanCastNow(character, spell))
            .OrderBy(spell => spell.Level)
            .ThenBy(spell => spell.Name, StringComparer.Ordinal);

        foreach (var spell in castable)
        {
            var rect = new Rect2(GridLeft, y, 260, 20);
            _spellRows.Add((rect, spell));

            DrawRect(rect, GridLine);

            if (_spellRows.Count - 1 == _menuIndex)
            {
                DrawRect(rect, ActiveRing, filled: false, width: 2f);
            }

            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 15),
                spell.IsCantrip ? $"{spell.Name} — cantrip" : $"{spell.Name} — level {spell.Level}",
                fontSize: 12,
                modulate: Ink);

            y += 24;
        }
    }

    private void DrawAttackMenu(Combatant character)
    {
        _attackRows.Clear();

        if (!_attackMenuOpen)
        {
            return;
        }

        var top = ButtonRowTop + 36 + 28 + 54;

        DrawString(TextFont, new Vector2(GridLeft, top - 6), "ATTACKS — click one, or arrows and Enter", fontSize: 12, modulate: Dim);

        var y = top + 6;

        foreach (var attack in character.Stats.Attacks)
        {
            var rect = new Rect2(GridLeft, y, 300, 20);
            _attackRows.Add((rect, attack));

            if (_attackRows.Count - 1 == _menuIndex)
            {
                DrawRect(rect, ActiveRing, filled: false, width: 2f);
            }

            var dice = string.Join(" + ", attack.Damage.Select(damage => $"{damage.Amount} {damage.Type}"));
            var reach = attack.NormalRangeFeet is { } normal
                ? attack.LongRangeFeet is { } far ? $"{normal}/{far} ft." : $"{normal} ft."
                : $"reach {attack.ReachFeet ?? 5} ft.";

            DrawRect(rect, GridLine);
            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 15),
                $"{attack.Name} — {dice}, {reach}",
                fontSize: 12,
                modulate: Ink);

            y += 24;
        }
    }

    /// <summary>The slot levels this caster could burn on this spell, lowest first.</summary>
    private static List<int> SlotLevelsFor(Combatant caster, SpellDefinition spell)
    {
        if (spell.IsCantrip)
        {
            return [];
        }

        return Enumerable.Range(spell.Level, 10 - spell.Level)
            .Where(level => caster.Features.SpellSlotsRemaining.GetValueOrDefault(level) > 0)
            .ToList();
    }

    private void DrawSlotMenu(Combatant character)
    {
        _slotRows.Clear();

        if (!_slotMenuOpen || _pendingSpell is not { } spell)
        {
            return;
        }

        var top = ButtonRowTop + 36 + 28 + 54;

        DrawString(
            TextFont,
            new Vector2(GridLeft, top - 6),
            $"SLOT for {spell.Name} — click a level, or arrows and Enter",
            fontSize: 12,
            modulate: Dim);

        var y = top + 6;

        foreach (var level in SlotLevelsFor(character, spell))
        {
            var rect = new Rect2(GridLeft, y, 260, 20);
            _slotRows.Add((rect, level));

            if (_slotRows.Count - 1 == _menuIndex)
            {
                DrawRect(rect, ActiveRing, filled: false, width: 2f);
            }

            var left = character.Features.SpellSlotsRemaining.GetValueOrDefault(level);

            DrawRect(rect, GridLine);
            DrawString(
                TextFont,
                new Vector2(rect.Position.X + 8, rect.Position.Y + 15),
                $"level {level} slot — {left} left" + (level > spell.Level ? " (upcast)" : string.Empty),
                fontSize: 12,
                modulate: Ink);

            y += 24;
        }
    }

    private string StatusLine(Combatant? commanded)
    {
        if (_phase == Phase.RunOver)
        {
            return "the run is over — [esc] quit";
        }

        if (_phase == Phase.Interlude)
        {
            return "between fights — Continue when ready   [esc] quit";
        }

        if (_encounter is { IsComplete: true } encounter)
        {
            return encounter.WinningSide == PregeneratedParty.SideId
                ? "the party wins — [esc] quit"
                : "the party has fallen — [esc] quit";
        }

        if (_pending == Pending.SpellTarget && _pendingSpell is { } spell)
        {
            return _pendingSlot is { } slot
                ? $"choose a target for {spell.Name} (level {slot} slot) — click it, or arrows and Enter; Esc cancels"
                : $"choose a target for {spell.Name} — click it, or arrows and Enter; Esc cancels";
        }

        if (_pending == Pending.AttackTarget && _pendingAttack is { } attack)
        {
            return $"choose a target for {attack.Name} — click it, or arrows and Enter; Esc cancels";
        }

        if (_pending == Pending.PotionTarget)
        {
            return "choose who drinks the potion — click it, or arrows and Enter; Esc cancels";
        }

        if (commanded is { } active)
        {
            var turn = active.Turn;
            return $"{active.Name}'s turn — Action {Tick(turn.HasAction)}  Bonus {Tick(turn.HasBonusAction)}  " +
                   $"Move {turn.MovementFeet} ft — click, or arrows and Enter; keys are on the buttons   [esc] quit";
        }

        return "the other side is acting…   [esc] quit";
    }

    private static string Tick(bool available) => available ? "✓" : "✗";

    /// <summary>
    /// Drives the screen through the real input path and captures each result: the
    /// run's opening interlude, then commanded turns — a refusal on purpose, a walk, a
    /// swing, a feature, and when a caster's turn comes, the spell menu and a cast. How
    /// a change to this screen gets checked without a person clicking.
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
        if (_run is not null && _phase == Phase.Interlude)
        {
            await CaptureFrame(Path.Combine(directory, "run-0-interlude.png"));
            Click(_continueButton.GetCenter());
        }

        await NextCommandedTurn();
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

        // In a run, play the fight out through the same clicks — swing at the nearest
        // enemy, end the turn — to reach the other side of the fight: the post-fight
        // interlude with its level-ups, loot and save, or the defeat screen. Whichever
        // comes, the capture shows the run reporting it.
        if (_run is not null)
        {
            var safety = 0;

            while (_phase == Phase.Fighting && safety < 5000)
            {
                safety++;

                if (CommandedCombatant() is { } fighter)
                {
                    if (NearestEnemyOf(fighter) is { } foe)
                    {
                        Click(CentreOf(foe.Position));
                    }

                    ClickButton("End Turn");
                }

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            await CaptureFrame(Path.Combine(directory, "run-9-after-fight.png"));
        }

        GetTree().Quit();
    }

    private Combatant? NearestEnemyOf(Combatant active) =>
        _encounter?.EnemiesOf(active)
            .Where(enemy => !enemy.IsDead)
            .OrderBy(enemy => enemy.Position.DistanceFeetTo(active.Position))
            .FirstOrDefault();

    private async Task NextCommandedTurn()
    {
        var waited = 0;

        while (CommandedCombatant() is null && waited < 3000)
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
