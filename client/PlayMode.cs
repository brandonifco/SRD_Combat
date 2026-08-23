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

    /// <summary>
    /// The card that holds after a fight, until the player says go on.
    /// </summary>
    /// <remarks>
    /// <b>A fight used to end straight into the results.</b> The last blow landed and the
    /// screen was already listing experience and loot, which reads as the game bailing out
    /// — worst of all on an objective rung, where a fight can end with enemies still on
    /// their feet and nothing on screen saying why. The card names the outcome and waits,
    /// and only when it is dismissed is the fight actually completed: the experience, the
    /// loot and the autosave all happen on the far side of it, so the player sees the
    /// result before the reckoning.
    /// </remarks>
    private bool _outcomeCard;

    private Encounter? _encounter;
    private Labels _labels = null!;
    private string _subtitle = string.Empty;
    private int _seed;
    private double _elapsed;
    private double _pace = SecondsPerTurn;
    private readonly HashSet<GridPosition> _reachable = [];

    /// <summary>Squares nobody in the party can see — the fog of war, <c>PartyVision</c>'s answer.</summary>
    private readonly HashSet<GridPosition> _unseen = [];

    /// <summary>
    /// The fog rendered smooth: <see cref="_unseen"/> painted one pixel per square and
    /// upscaled bilinearly, so its edge feathers across a square instead of stepping.
    /// </summary>
    private ImageTexture? _fogTexture;

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

    /// <summary>
    /// What each button explains about itself when the pointer rests on it, by caption.
    /// </summary>
    /// <remarks>
    /// Kept beside the buttons rather than inside them because a caption is what the
    /// click path already matches on, and the probe drives buttons by caption too.
    /// </remarks>
    private readonly Dictionary<string, string> _buttonHints = [];

    /// <summary>Where the pointer is, and how long it has rested there.</summary>
    /// <remarks>
    /// <b>Hints wait, deliberately.</b> A tooltip that appears the instant the pointer
    /// crosses something turns a glance across the row into a flicker of popups; a pause
    /// is the player asking. Movement past <see cref="HoverJitterPixels"/> restarts the
    /// clock, so a hand that never quite stops still settles.
    /// </remarks>
    private Vector2 _pointer;
    private double _hoverElapsed;
    private string? _hint;

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

    /// <summary>True while the quit card is asking whether Esc really meant it.</summary>
    private bool _quitAsked;

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

    /// <summary>
    /// Baseline of the active-combatant banner. A fixed strip at the window's bottom
    /// rather than a line under the grid: the field fills the whole window now, so the
    /// banner, the buttons and the notice float over it on the shared veil.
    /// </summary>
    private float BannerTop => ScreenHeight - 118f;

    private float ButtonRowTop => BannerTop + 42f;

    /// <summary>The translucent strip the banner, buttons and notice sit on.</summary>
    private Rect2 BottomStrip => new(8, BannerTop - 26, PanelLeft - 40, ScreenHeight - (BannerTop - 26) - 8);

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
            // Falls back to the .bak automatically when the primary is missing or
            // unreadable — silently beginning a fresh run here would overwrite the file
            // being asked about, so a genuine failure still stops rather than proceeds.
            var loaded = SaveFile.LoadRun(_savePath);

            if (loaded.Saved is null)
            {
                _phase = Phase.RunOver;
                _interlude.Add(loaded.PrimaryFailureReason is { } reason
                    ? $"Cannot load '{_savePath}' or its backup: {reason}"
                    : $"No save at '{_savePath}'. Pass --save=<path> or start a new run.");
                _subtitle = $"seed {_seed}";
                return;
            }

            if (loaded.UsedBackup)
            {
                _interlude.Add($"'{_savePath}' was missing or unreadable; loaded the backup instead.");
            }

            _run = GauntletRun.Resume(content, loaded.Saved);

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
        // The objective rides the subtitle when the rung is not a plain deathmatch: it is
        // the one line already on screen for the whole fight, and a goal shown once before
        // the first turn is a goal forgotten by round three. The wording comes from the
        // encounter so the two clients cannot word it differently.
        _subtitle = $"fight {run.Cleared + 1} of {run.Ladder.Count} — seed {_seed} — "
            + (_encounter.Objective.Kind == ObjectiveKind.Defeat
                ? $"the party against {RosterOf(fight)}"
                : _encounter.ObjectiveDescription);
        _phase = Phase.Fighting;
        _fightEndHandled = false;
        _buttonsFor = null;

        // A fresh fight is a fresh log: nothing has been scanned for acts, and any
        // walk or swing the last fight left half-played dies with it.
        _walkStepsSeen = 0;
        ClearActs();

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

        // Hold on the outcome first. The probe has nobody to press the key, so it takes
        // the old path straight through rather than stalling forever on a card.
        if (!HasArgument("probe"))
        {
            _outcomeCard = true;
            QueueRedraw();
            return;
        }

        CompleteAndReport();
    }

    /// <summary>Finishes the fight the card was announcing: rewards, save, interlude.</summary>
    private void CompleteAndReport()
    {
        _outcomeCard = false;

        if (_run is not { } run || _fight is not { } fight || _encounter is not { } encounter)
        {
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
            SaveFile.Write(_savePath, RunSave.ToJson(run));
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
                _spellMenuOpen = false;
                _menuIndex = 0;

                // With one attack there is nothing to choose, so it arms targeting
                // straight away; the menu is for characters carrying a choice.
                if (CommandedCombatant() is { } swinging && swinging.Stats.Attacks.Count == 1)
                {
                    _attackMenuOpen = false;
                    _pendingAttack = swinging.Stats.Attacks[0];
                    ArmTargeting(Pending.AttackTarget);
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

            case TurnAction.GivePotion: ArmTargeting(Pending.PotionTarget); return null;
            case TurnAction.DivineSparkHeal: ArmTargeting(Pending.SparkHealTarget); return null;
            case TurnAction.DivineSparkHarm: ArmTargeting(Pending.SparkHarmTarget); return null;

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
    private void ArmTargeting(Pending pending)
    {
        _pending = pending;

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

        var offered = _pending switch
        {
            Pending.AttackTarget =>
                TargetChoice.For(encounter, actor, TargetKind.Attack, attack: _pendingAttack),
            Pending.SpellTarget =>
                TargetChoice.For(encounter, actor, TargetKind.Spell, spell: _pendingSpell),
            Pending.PotionTarget => TargetChoice.For(encounter, actor, TargetKind.Potion),
            Pending.SparkHealTarget => TargetChoice.For(encounter, actor, TargetKind.SparkHeal),
            Pending.SparkHarmTarget => TargetChoice.For(encounter, actor, TargetKind.SparkHarm),
            _ => [],
        };

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
            ArmTargeting(Pending.SpellTarget);
        }

        QueueRedraw();
    }

    private void ChooseSlot(int level)
    {
        _slotMenuOpen = false;
        _pendingSlot = level;
        ArmTargeting(Pending.SpellTarget);
        QueueRedraw();
    }

    private void ChooseAttack(CombatAttack attack)
    {
        _attackMenuOpen = false;
        _pendingAttack = attack;
        ArmTargeting(Pending.AttackTarget);
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

    public override void _Process(double delta)
    {
        if (_phase != Phase.Fighting || _encounter is not { } encounter)
        {
            RunProbeIfAsked();
            return;
        }

        // The idle and walk loops tick whatever else the beat is doing — a fight where
        // nothing is happening is still a fight where everyone is breathing.
        // The hover clock runs whatever else the board is doing — a player reading the
        // row while the monsters take their turns is exactly who a hint is for.
        AdvanceHover(delta);

        if (AdvanceSpriteAnimation(delta))
        {
            QueueRedraw();
        }

        // A walk or a swing plays out before anything else happens: the token glides
        // its route or lands its blow, and the next beat — the policy's turn, the
        // fight's end — waits for it.
        if (AdvanceActs(delta))
        {
            QueueRedraw();
        }

        // The camera glides after whatever the board is doing, never gating it.
        if (AdvanceCamera(delta))
        {
            QueueRedraw();
        }

        if (ActInProgress)
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

        if (CommandedCombatant() is { } commanded)
        {
            // A turn with nothing in it but the way out of it ends itself. Asking a
            // player to click End Turn when the row holds only End Turn is asking them
            // to confirm a decision they were never offered — and it happens most to the
            // character having the worst time of it, whose Action, Bonus Action and
            // movement are all spent. Paced like anyone else's turn rather than snapped
            // through, so the board can be read on the way past; and gated behind
            // ActInProgress above, so the last blow's animation always finishes first.
            if (NothingLeftButEndTurn(commanded))
            {
                _elapsed += delta;

                if (_elapsed < _pace)
                {
                    return;
                }

                _elapsed = 0;
                encounter.EndTurn();
                RefreshAfterAction(null);
                return;
            }

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
        // The quit card owns every input while it is up: Esc again really quits,
        // anything else pressed or clicked stays. Reported from play on 2026-08-18
        // after two accidental exits — Esc is also the key that backs out of an armed
        // action, so one press past the last thing to cancel used to be the whole game
        // gone mid-fight, with the run rolled back to the last cleared fight.
        if (_quitAsked)
        {
            if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                GetTree().Quit();
                return;
            }

            if (@event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true })
            {
                _quitAsked = false;
                QueueRedraw();
                return;
            }

            return;
        }

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

            if (_outcomeCard)
            {
                CompleteAndReport();
                QueueRedraw();
                return;
            }

            if (_pending != Pending.Nothing || _spellMenuOpen || _attackMenuOpen || _slotMenuOpen)
            {
                ClearPending();
                QueueRedraw();
                return;
            }

            _quitAsked = true;
            QueueRedraw();
            return;
        }

        if (@event is InputEventKey { Pressed: true } && _outcomeCard)
        {
            // Any key moves on: the card asks nothing of the player but acknowledgement,
            // so hunting for the right one would be its own small annoyance.
            CompleteAndReport();
            QueueRedraw();
            return;
        }

        if (@event is InputEventKey { Pressed: true } key && _phase == Phase.Fighting && !_shopView)
        {
            // While an act is playing out, the keyboard commands nothing — the engine
            // resolves instantly, so without this gate a key pressed mid-swing started
            // the next action before the first had visibly happened (asked for from
            // play, 2026-08-21). Esc stays live above: quitting must not wait on an
            // animation. The buttons grey themselves over the same window.
            if (ActInProgress)
            {
                return;
            }

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

            // Tab walks the ring of things the armed action could be used on — and with
            // nothing armed it arms the attack first (asked for from play, 2026-08-19),
            // so one key reaches "aim at somebody" from a cold turn. Arming names no
            // attack: the ring is every living enemy, and Enter picks the best attack
            // for whoever it lands on, the same answer a bare click on an enemy has
            // always taken. Gated the way every keypress is — only while the row
            // actually offers Attacks — so Tab can never reach an action the row hides.
            if (key.Keycode == Key.Tab)
            {
                if (_pending != Pending.Nothing)
                {
                    CycleTarget();
                }
                else if (OpenMenuLength == 0
                    && _encounter is { } fight
                    && CommandedCombatant() is { } swinger
                    && TurnOptions.For(fight, swinger).Contains(TurnAction.Attacks))
                {
                    _pendingAttack = null;
                    ArmTargeting(Pending.AttackTarget);
                }

                return;
            }

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

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _outcomeCard)
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
    /// Whether this character has no choice left to make but ending the turn.
    /// </summary>
    /// <remarks>
    /// <b>The row is the whole question, and leftover movement is deliberately not part
    /// of it.</b> This first shipped also requiring <c>_reachable</c> to be empty, on the
    /// reasoning that walking is not a button so a row holding only End Turn says nothing
    /// about whether the character can still reposition. That reasoning is sound and the
    /// behaviour was wrong: <b>attacking spends the Action, never the movement</b>, so a
    /// character who swings from where they stand keeps a full Speed and every such turn
    /// still had to be dismissed by hand — which is nearly every turn, and exactly the
    /// friction this exists to remove.
    /// <para>
    /// The cost is stated rather than hidden: a character who attacks *before* moving no
    /// longer gets to step away afterwards. That is the XCOM convention — acting ends
    /// your turn — and it is predictable, which beats a rule that sometimes ends the turn
    /// and sometimes does not depending on a number the row never showed. Move first,
    /// then act.
    /// </para>
    /// <para>
    /// Anything the player has half-started — an armed attack, an open menu — counts as
    /// a choice in progress and holds the turn open, so the screen never closes over
    /// something somebody was in the middle of.
    /// </para>
    /// </remarks>
    private bool NothingLeftButEndTurn(Combatant commanded) =>
        _pending == Pending.Nothing
        && !_spellMenuOpen
        && !_attackMenuOpen
        && !_slotMenuOpen
        && _encounter is { } encounter
        && TurnOptions.For(encounter, commanded) is [TurnAction.EndTurn];

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
        if (_phase != Phase.Fighting || _shopView)
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

        // The mouse waits with the keyboard: while an act's animation is playing, a
        // click on a button, a menu or the board commands nothing, so an action's
        // effects are seen before the next one can be asked for. Nothing is armed
        // during the window — arming itself takes an input this gate swallows.
        if (ActInProgress)
        {
            return;
        }

        // An armed click resolves first: the next token is the target, anywhere else
        // backs out. Cancelling must never cost anything, so nothing is spent until the
        // engine call itself. A click on an overlay is "anywhere else" — the field runs
        // under the log now, and a cancel aimed at the panel must not land on whatever
        // square happens to sit beneath it.
        if (_pending != Pending.Nothing)
        {
            ActivateSquare(OverOverlay(pixel) ? null : SquareAt(pixel));
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

        if (OverOverlay(pixel))
        {
            return;
        }

        ActivateSquare(SquareAt(pixel));
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

        if (_pending == Pending.AttackTarget)
        {
            var chosen = _pendingAttack;
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

    /// <summary>How dark the fog is where nothing can be seen.</summary>
    private const float FogOpacity = 0.55f;

    /// <summary>Fog texture pixels per battlefield square — the feathering's resolution.</summary>
    private const int FogPixelsPerSquare = 8;

    /// <summary>
    /// Renders the fog one pixel per square and upscales it bilinearly, so the interior
    /// stays a solid shadow while the boundary ramps smoothly over about a square.
    /// </summary>
    private ImageTexture? BuildFogTexture(Battlefield field)
    {
        if (_unseen.Count == 0)
        {
            return null;
        }

        var image = Image.CreateEmpty(field.Width, field.Height, false, Image.Format.Rgba8);

        foreach (var square in _unseen)
        {
            image.SetPixel(square.X, square.Y, new Color(0f, 0f, 0f, FogOpacity));
        }

        image.Resize(
            field.Width * FogPixelsPerSquare,
            field.Height * FogPixelsPerSquare,
            Image.Interpolation.Bilinear);

        return ImageTexture.CreateFromImage(image);
    }

    public override void _Draw()
    {
        if (_phase != Phase.Fighting || _encounter is not { } encounter)
        {
            DrawChrome(_subtitle, StatusLine(null));
            DrawInterlude();
            DrawQuitCard();
            return;
        }

        var active = encounter.ActiveCombatant;
        var commanded = CommandedCombatant();

        // The field first, floor to ceiling; every piece of chrome floats over it.
        DrawBackdrop();
        DrawGrid();

        // Advice under the tokens: where a walk could end, and who a click would attack.
        foreach (var square in _reachable)
        {
            DrawRect(
                new Rect2(GridLeft + (square.X * CellPixels), GridTop + (square.Y * CellPixels), CellPixels, CellPixels),
                new Color(PartyColour, 0.16f));
        }

        // The fog of war, drawn smooth: the per-square set is painted into a small
        // image and upscaled bilinearly (BuildFogTexture), so the shadow's edge
        // feathers across a square instead of stepping — the blockiness was the other
        // half of the 2026-08-21 request. One texture draw whatever the fog's shape.
        if (_fogTexture is { } fog)
        {
            DrawTextureRect(
                fog,
                new Rect2(GridLeft, GridTop, GridWidth * CellPixels, GridHeight * CellPixels),
                false);
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

        // Holds first, then the walk: a held token is how somebody *looked* before a
        // blow whose picture has not played, and where anybody stands is the walk's own
        // question. Together they make the screen tell the fight in order — the walk,
        // the swing, the damage on its last frame, and only then the fall — where live
        // state alone showed the victim on the floor before the monster took a step.
        var tokens = WithWalk(WithHeldAppearances(TokensFrom(encounter, _labels)));

        // What the fog withholds: a monster standing where nobody in the party can see
        // draws no token and earns no ring — the fog would otherwise be a tint over a
        // perfectly visible figure. The panel keeps its row (initiative order is
        // knowledge the party has from the fight itself) with its state withheld.
        var unseenIds = tokens
            .Where(token => !token.IsParty && _unseen.Contains(new GridPosition(token.X, token.Y)))
            .Select(token => token.Id)
            .ToHashSet();
        var shown = tokens.Where(token => !unseenIds.Contains(token.Id)).ToList();

        if (commanded is not null && _pending == Pending.Nothing)
        {
            foreach (var enemy in encounter.EnemiesOf(commanded))
            {
                if (!enemy.IsDead
                    && !_unseen.Contains(enemy.Position)
                    && AttackChoice.BestFor(commanded, enemy, encounter.Combatants) is not null)
                {
                    DrawCircle(CentreOf(enemy.Position), (CellPixels / 2f) - 4, MonsterColour, filled: false, width: 2);
                }
            }
        }

        DrawTokens(shown, active?.Id);
        DrawHeading(_subtitle, StatusLine(commanded));
        DrawTurnOrder(tokens, active?.Id, unseenIds);
        DrawLog(encounter.Log, encounter.Log.Count, tokens.Count);

        // The bottom strip's own veil, before anything is written on it.
        if (active is not null || commanded is not null || _notice is not null)
        {
            DrawRect(BottomStrip, Veil);
        }

        // Who is up, and with what — class and level for a character, AC, hit points,
        // and the attacks they carry. TurnBanner composes it so the console client and
        // this screen cannot drift; the letter is this fight's label for the token.
        if (active is { } upNow)
        {
            var lines = TurnBanner.Lines(upNow);
            var colour = upNow.SideId == PregeneratedParty.SideId ? PartyColour : MonsterColour;

            DrawString(
                TextFont,
                new Vector2(UiLeft, BannerTop),
                Trim($"{_labels.Of(upNow)}  {lines[0]}", 90),
                fontSize: 13,
                modulate: colour);

            if (lines.Count > 1)
            {
                DrawString(
                    TextFont,
                    new Vector2(UiLeft, BannerTop + 18),
                    Trim(lines[1], 95),
                    fontSize: 12,
                    modulate: Dim);
            }
        }

        if (commanded is { } character)
        {
            // Greyed while an act plays out: the input gates above make the row inert
            // over that window, and a button that looks pressable while it is not
            // would be the display lying about it.
            var inkNow = ActInProgress ? Dim : Ink;

            foreach (var (rect, caption, _) in _buttons)
            {
                DrawRect(rect, GridLine);
                DrawRect(rect, Dim, filled: false, width: 1);
                DrawString(
                    TextFont,
                    new Vector2(rect.Position.X + 11, rect.Position.Y + 19),
                    caption,
                    fontSize: 13,
                    modulate: inkNow);
            }

            DrawString(
                TextFont,
                new Vector2(UiLeft, ButtonRowTop + 28 + 16),
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
                new Vector2(UiLeft, ButtonRowTop + 28 + 38),
                Trim(notice, 78),
                fontSize: 13,
                modulate: MonsterColour);
        }

        // Over the board, under nothing: the fight is finished and this is the only
        // thing being asked.
        DrawOutcomeCard();

        // Last, so it sits over everything it might explain.
        DrawHint();

        DrawQuitCard();
    }

    /// <summary>
    /// The card that asks whether Esc really meant to leave. It says what quitting
    /// costs — the save keeps the state after the last <em>cleared</em> fight, so a
    /// fight in progress restarts — because that cost is exactly what an accidental
    /// exit was paying without asking.
    /// </summary>
    private void DrawQuitCard()
    {
        if (!_quitAsked)
        {
            return;
        }

        const int width = 470;
        const int height = 118;
        var left = (ScreenWidth - width) / 2f;
        var top = (ScreenHeight - height) / 2f;
        var card = new Rect2(left, top, width, height);

        DrawRect(card, Background);
        DrawRect(card, ActiveRing, filled: false, width: 2);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 42),
            "LEAVE THE GAME?",
            fontSize: 26,
            modulate: ActiveRing);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 70),
            "The run is saved after each cleared fight; a fight in progress restarts.",
            fontSize: 12,
            modulate: Ink);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 96),
            "[esc] again quits — any other key or click stays",
            fontSize: 12,
            modulate: Dim);
    }

    /// <summary>
    /// The hint the pointer has earned, in a panel beside it.
    /// </summary>
    /// <remarks>
    /// Placed against the pointer rather than in a fixed corner — a tip you have to look
    /// away to read is a tip you stop reading — and nudged back inside the window rather
    /// than being allowed off the edge, since the row it explains runs to the screen's
    /// bottom right where a naive panel would fall straight off.
    /// </remarks>
    private void DrawHint()
    {
        if (_hint is not { } hint)
        {
            return;
        }

        var lines = hint.Split('\n')
            .SelectMany(line => Wrap(line, HintWidthCharacters))
            .ToArray();

        var width = lines.Max(line => TextFont.GetStringSize(line, fontSize: 12).X) + 20;
        var height = (lines.Length * 17) + 14;

        var x = Math.Min(_pointer.X + 16, ScreenWidth - width - 8);
        var y = _pointer.Y + 22 + height > ScreenHeight
            ? _pointer.Y - height - 10
            : _pointer.Y + 22;

        var panel = new Rect2(Math.Max(8, x), Math.Max(8, y), width, height);

        DrawRect(panel, Background);
        DrawRect(panel, ActiveRing, filled: false, width: 1);

        for (var index = 0; index < lines.Length; index++)
        {
            DrawString(
                TextFont,
                new Vector2(panel.Position.X + 10, panel.Position.Y + 18 + (index * 17)),
                lines[index],
                fontSize: 12,
                modulate: Ink);
        }
    }

    /// <summary>How wide a hint runs before it wraps, in characters.</summary>
    private const int HintWidthCharacters = 52;

    /// <summary>
    /// The card that names how the fight ended and waits to be dismissed.
    /// </summary>
    /// <remarks>
    /// It says *why* as well as what, because an objective rung can end with enemies
    /// still standing and a bare "you win" over a field of live goblins is the confusing
    /// part rather than the satisfying one.
    /// </remarks>
    private void DrawOutcomeCard()
    {
        if (!_outcomeCard || _encounter is not { } encounter)
        {
            return;
        }

        var won = encounter.WinningSide == PregeneratedParty.SideId;
        var heading = won ? "BATTLE WON" : "BATTLE LOST";

        var why = encounter.Objective.Kind switch
        {
            ObjectiveKind.SurviveRounds when won =>
                $"The party held out for {encounter.Objective.Rounds} rounds.",
            ObjectiveKind.KillLeader when won =>
                "The leader is down — the rest break off.",
            _ => won ? "Every enemy is down." : "The party has fallen.",
        };

        // Centred on the window, not the field: the camera may have carried the field
        // anywhere, and the card is being said to the player, not to a square.
        const int width = 460;
        const int height = 132;
        var left = (ScreenWidth - width) / 2f;
        var top = (ScreenHeight - height) / 2f;
        var card = new Rect2(left, top, width, height);

        DrawRect(card, Background);
        DrawRect(card, won ? ActiveRing : MonsterColour, filled: false, width: 2);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 46),
            heading,
            fontSize: 30,
            modulate: won ? ActiveRing : MonsterColour);

        DrawString(TextFont, new Vector2(left + 24, top + 78), why, fontSize: 13, modulate: Ink);

        DrawString(
            TextFont,
            new Vector2(left + 24, top + 106),
            "any key or click for the results",
            fontSize: 12,
            modulate: Dim);
    }

    /// <summary>The between-fights screen: the run's own words, and a way onward.</summary>
    private void DrawInterlude()
    {
        if (_shopView && _run is { } shopping)
        {
            DrawShop(shopping);
            return;
        }

        var y = UiTop + 8f;

        foreach (var line in _interlude)
        {
            if (line.Length == 0)
            {
                y += 10;
                continue;
            }

            DrawString(TextFont, new Vector2(UiLeft, y), Trim(line, 100), fontSize: 14, modulate: Ink);
            y += 22;
        }

        if (_phase == Phase.Interlude)
        {
            _continueButton = new Rect2(UiLeft, y + 16, 110, 32);

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
                _shopButton = new Rect2(UiLeft + 126, y + 16, 110, 32);

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
        var y = UiTop + 8f;

        DrawString(
            TextFont,
            new Vector2(UiLeft, y),
            $"A merchant is here. The purse holds {Shop.Price(run.GoldCopper)}. Click to buy.",
            fontSize: 14,
            modulate: Ink);

        y += 26;

        if (offers.Count == 0)
        {
            DrawString(
                TextFont,
                new Vector2(UiLeft, y),
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
            var rect = new Rect2(UiLeft, y, ShopRowWidth, 19 + (effects.Count * 15));

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
            DrawString(TextFont, new Vector2(UiLeft, y + 12), Trim(notice, 78), fontSize: 13, modulate: MonsterColour);
            y += 18;
        }

        _shopBackButton = new Rect2(UiLeft, y + 12, 110, 32);

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

        // Over the board, as an overlay. These lists used to live under the second
        // button row; fullscreen gave that ground to the board, and every row below
        // already paints its own filled backdrop, so only the header needs one.
        var top = UiTop + 28;

        DrawRect(
            new Rect2(UiLeft - 8, top - 24, 470, 30),
            new Color(Background.R, Background.G, Background.B, 0.9f));

        DrawString(TextFont, new Vector2(UiLeft, top - 6), "SPELLS — click one, or arrows and Enter", fontSize: 12, modulate: Dim);

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
            var rect = new Rect2(UiLeft, y, 260, 20);
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

        // Over the board, as an overlay. These lists used to live under the second
        // button row; fullscreen gave that ground to the board, and every row below
        // already paints its own filled backdrop, so only the header needs one.
        var top = UiTop + 28;

        DrawRect(
            new Rect2(UiLeft - 8, top - 24, 470, 30),
            new Color(Background.R, Background.G, Background.B, 0.9f));

        DrawString(TextFont, new Vector2(UiLeft, top - 6), "ATTACKS — click one, or arrows and Enter", fontSize: 12, modulate: Dim);

        var y = top + 6;

        foreach (var attack in character.Stats.Attacks)
        {
            var rect = new Rect2(UiLeft, y, 300, 20);
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

        // Over the board, as an overlay. These lists used to live under the second
        // button row; fullscreen gave that ground to the board, and every row below
        // already paints its own filled backdrop, so only the header needs one.
        var top = UiTop + 28;

        DrawRect(
            new Rect2(UiLeft - 8, top - 24, 470, 30),
            new Color(Background.R, Background.G, Background.B, 0.9f));

        DrawString(
            TextFont,
            new Vector2(UiLeft, top - 6),
            $"SLOT for {spell.Name} — click a level, or arrows and Enter",
            fontSize: 12,
            modulate: Dim);

        var y = top + 6;

        foreach (var level in SlotLevelsFor(character, spell))
        {
            var rect = new Rect2(UiLeft, y, 260, 20);
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
            return _outcomeCard
                ? "the fight is over — any key or click for the results"
                : encounter.WinningSide == PregeneratedParty.SideId
                    ? "the party wins — [esc] quit"
                    : "the party has fallen — [esc] quit";
        }

        if (_pending == Pending.SpellTarget && _pendingSpell is { } spell)
        {
            return _pendingSlot is { } slot
                ? $"choose a target for {spell.Name} (level {slot} slot) — click it, Tab cycles, Enter takes it; Esc cancels"
                : $"choose a target for {spell.Name} — click it, Tab cycles, Enter takes it; Esc cancels";
        }

        if (_pending == Pending.AttackTarget)
        {
            return _pendingAttack is { } attack
                ? $"choose a target for {attack.Name} — click it, Tab cycles, Enter takes it; Esc cancels"
                : "choose a target — Tab cycles, Enter attacks with the best weapon; Esc cancels";
        }

        if (_pending == Pending.PotionTarget)
        {
            return "choose who drinks the potion — click it, Tab cycles, Enter takes it; Esc cancels";
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
    /// run's opening interlude, then commanded turns — a refusal on purpose, Tab arming
    /// and cycling from a cold turn, a walk, a swing, a feature, and when a caster's
    /// turn comes, the spell menu and a cast. How a change to this screen gets checked
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

        await HoverFirstButton();
        await CaptureFrame(Path.Combine(directory, "play-2b-hint.png"));

        // Tab from a cold turn: the first press arms the attack and aims at the
        // nearest enemy, the second walks the ring — then Esc backs out, so the rest
        // of the probe starts from the same clean turn it always did.
        Press(Key.Tab);
        Press(Key.Tab);
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
            return;
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
