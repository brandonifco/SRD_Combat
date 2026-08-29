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

    private Encounter? _encounter;
    private Labels _labels = null!;
    private string _subtitle = string.Empty;
    private int _seed;
    private bool _isNewRun;
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

    /// <summary>
    /// The rows of whichever menu is open, and what taking each one does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One list where there were three</b> (#505). The spell, attack and slot menus each
    /// used to keep their own rows in a field of their own, one payload type apiece — a
    /// spell, an attack, a slot level — so a row's meaning depended on cross-referencing
    /// <c>_focus.Top</c> against whichever field happened to hold real rectangles this
    /// frame. Only one of the three menus is ever open at once, so at most one of the three
    /// fields ever held anything live; the other two were dead weight a reader had to rule
    /// out by hand. <see cref="Action"/> closes over the payload at the point each
    /// <c>Draw*Menu</c> method fills the row, so <em>taking</em> a row no longer means
    /// re-deriving which field and which member it came from — routing unifies even though
    /// drawing does not (three methods still produce three different sets of pixels).
    /// </para>
    /// <para>
    /// <b>Collapsing the three typed lists into one untyped <c>Action</c> list threw away a
    /// guard the type system used to give for free</b> (qc review round, #505): a spell row
    /// could not physically hold an attack's closure while there were three fields, and it
    /// could once there was one — draw fills this list, input reads it, and between a
    /// <c>ToggleMenu</c> swap and the next <c>_Draw</c> those two events could disagree for
    /// one input. <see cref="MenuRowList"/> is the replacement guard: every row is stamped
    /// with the exact layer instance that added it, and reading it back refuses unless the
    /// caller's current top layer is that same instance, by reference — an asserted
    /// invariant rather than a coincidence of draw timing. <c>MenuRowListTests</c> drives
    /// the exact window this closes.
    /// </para>
    /// </remarks>
    private readonly MenuRowList _menuRows = new();
    private string? _buttonsFor;
    /// <summary>
    /// What has the player's attention: the board, a menu over it, or an armed action
    /// waiting for a target.
    /// </summary>
    /// <remarks>
    /// <b>One field where there were seven</b> (#500). Three menu booleans, a
    /// <c>Pending</c> enum and three loose payload fields beside it all said
    /// something about the same fact, and nothing held them consistent: every combination
    /// was representable, including several the screen had no drawing for — two menus open
    /// at once, or a payload left behind by a menu that had closed. A stack is
    /// single-valued by construction, so those states no longer exist to be reached.
    /// </remarks>
    private readonly FocusStack<PlayFocus> _focus = new(new PlayFocus.Board());
    private string? _notice;
    private bool _probeStarted;

    /// <summary>True while the quit card is asking whether Esc really meant it.</summary>

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
    private readonly List<(Rect2 Rect, ShopOffer Offer)> _shopRows = [];
    private bool _fightEndHandled;

    protected override string Title => "SRD_Combat — playing";

    /// <summary>
    /// Baseline of the active-combatant banner. A fixed strip at the window's bottom
    /// rather than a line under the grid: the field fills the whole window now, so the
    /// banner, the buttons, the equipment line and the notice float over it on the
    /// shared veil. 18px taller than before #534 added the equipment line — every
    /// other row keeps its old spacing, shifted up to make room, so the notice keeps
    /// the same distance from the strip's bottom edge it always had.
    /// </summary>
    private float BannerTop => ScreenHeight - 136f;

    private float ButtonRowTop => BannerTop + 42f;

    /// <summary>The translucent strip the banner, buttons, equipment line and notice sit on.</summary>
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

        // The gauntlet loop below never calls ResolveFight — it draws its own roster
        // every fight — so --spawn here would silently do nothing (#463). Refuse it
        // the same way a bad roster refuses, rather than starting a run that quietly
        // ignored what was asked for. HasArgument("spawn") is the same presence
        // predicate ResolveFight's own spawn branch keys on (FightScreen.cs) — a bare
        // --spawn or the console's space form counts as "given" in both places, so
        // this gate and that branch never disagree about whether the flag was passed
        // (#470, M2 — they used to: this gate on HasArgument, that branch on
        // ArgumentValue, which let a valueless --spawn slip past both silently).
        if (HasArgument("spawn") && !HasArgument("one-fight"))
        {
            _phase = Phase.RunOver;
            _interlude.Add(
                "--spawn refused: the gauntlet does not read it — it draws its own roster " +
                "every fight. Pass --one-fight (or run with --watch) to field a chosen cast.");
            _subtitle = $"seed {_seed}";
            return;
        }

        // --scenario is the same shape one flag over (#476): the gauntlet loop below
        // never calls ResolveFight either, so a scenario named here would be silently
        // unplayed rather than refused — exactly the hole #463 closed for --spawn.
        if (HasArgument("scenario") && !HasArgument("one-fight"))
        {
            _phase = Phase.RunOver;
            _interlude.Add(
                "--scenario refused: the gauntlet does not read it — it draws its own roster " +
                "every fight. Pass --one-fight (or run with --watch) to play it.");
            _subtitle = $"seed {_seed}";
            return;
        }

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
            Fight fight;
            IReadOnlyList<string> notices;

            try
            {
                fight = ResolveFight(_seed, out notices);
            }
            catch (ScenarioRefusedException refusal)
            {
                // The same screen a bad save gets: the reason, on screen, and nothing
                // started — a refusal printed only to a console nobody launched from
                // is a refusal nobody sees.
                _phase = Phase.RunOver;
                _interlude.Add(refusal.Message);
                _subtitle = $"seed {_seed}";
                return;
            }

            _fight = null;
            _encounter = fight.Encounter;
            _labels = Labels.For(_encounter.Combatants);
            AdoptBattlefield(_encounter);
            _subtitle = $"one fight — seed {_seed} — the party against {RosterOf(fight)}"
                + NoticeSuffix(notices);
            _walkStepsSeen = 0;

            RefreshAfterAction(null);
            return;
        }

        var content = LoadContent();
        _content = content;

        // _dice is not seeded here: EnterInterlude reseeds it once per fight, from
        // the run's own seed and how many fights are cleared — the one reseed point,
        // per RunDice's remarks — so anything set here would only be overwritten
        // before it was ever read.
        _savePath = ArgumentValue("save") ?? "srdcombat-save.json";

        // Collected rather than appended straight to _interlude: EnterInterlude below
        // starts every screen with _interlude.Clear(), so anything added before that
        // call would be wiped before the first frame ever showed it.
        var startupNotices = new List<string>();

        if (HasArgument("continue"))
        {
            // Falls back to the .bak automatically when the primary is missing or
            // unreadable — silently beginning a fresh run here would overwrite the file
            // being asked about, so a genuine failure still stops rather than proceeds.
            var loaded = SaveFile.LoadRun(_savePath);

            if (loaded.Saved is null)
            {
                _phase = Phase.RunOver;
                _interlude.Add(SaveFile.DescribeUnloadable(_savePath, loaded)
                    ?? $"No save at '{_savePath}'. Pass --save=<path> or start a new run.");
                _subtitle = $"seed {_seed}";
                return;
            }

            if (loaded.UsedBackup)
            {
                startupNotices.Add($"'{_savePath}' was missing or unreadable; loaded the backup instead.");
            }

            // A save written before #287 carries no content version to compare in
            // bulk; GauntletRun.Resume falls through to checking every id it resolves
            // one at a time instead, exactly as it always has for a same-version edge
            // case.
            if (loaded.Saved.ContentVersion is null)
            {
                startupNotices.Add(
                    "This save carries no content version; everything it names is checked " +
                    "against the loaded content piece by piece instead.");
            }

            // GauntletRun.Resume refuses drift three ways: a present content version
            // that disagrees with what is loaded and ContentDrift.Require's per-id
            // checks both throw InvalidDataException; CharacterResolver's own weapon,
            // armor and magic item checks throw ArgumentException instead — a
            // Core-level convention this Game-level catch has to know about too, or
            // exactly this drift crashes instead of refusing. Either way this is a
            // printed message, never a crash, and the file itself is never touched.
            try
            {
                _run = GauntletRun.Resume(content, loaded.Saved);
            }
            catch (Exception failure) when (failure is InvalidDataException or ArgumentException)
            {
                _phase = Phase.RunOver;
                _interlude.Add($"Cannot resume '{_savePath}': {failure.Message}");
                _subtitle = $"seed {_seed}";
                return;
            }

            // A save written before #286 carries no seed at all. There is an honest
            // thing to do here that there is not for a content-version mismatch: roll
            // one, once, tell the player — GauntletRun.AdoptSeed's own remarks say why
            // nowhere else may call it — and let it write the save immediately, so
            // quitting before the next cleared fight does not lose the roll (#361).
            if (loaded.Saved.Seed is null)
            {
                var rolled = Random.Shared.Next();
                _run.AdoptSeed(rolled, _savePath);
                startupNotices.Add(
                    $"This save predates run seeds; rolled {rolled} for the rest of the run and saved it.");
            }

            _seed = _run.Seed;
            _isNewRun = false;
            _subtitle = $"continuing after fight {_run.Cleared} of {_run.Ladder.Count} — seed {_run.Seed}";
        }
        else
        {
            var level = ArgumentValue("level") is { } text && int.TryParse(text, out var parsed)
                ? Math.Clamp(parsed, 1, 5)
                : 1;

            _run = CreatedDrafts is not null
                ? GauntletRun.Start(content, CreatedDrafts, seed: _seed)
                : GauntletRun.Start(content, GauntletLadder.Default(), level, _seed);
            _isNewRun = true;
            _subtitle = $"a gauntlet of {_run.Ladder.Count} fights — seed {_seed}";
        }

        // A save written before creation asked for a level-4 Ability Score Improvement
        // plan can arrive here already past level 4; GauntletRun.Resume defaults it
        // rather than forfeiting it, and this is where that default first becomes
        // visible — LevelUps is empty on a fresh Start, so this only adds anything on
        // a resumed save.
        startupNotices.AddRange(_run.LevelUps.Select(notice => notice + "!"));

        EnterInterlude(startupNotices);
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
            // The one reseed point: everything from here through the fight this
            // interlude sets up and the loot it pays out draws from this one segment
            // — see RunDice's remarks for why a fight always plays the same dice
            // regardless of how an earlier attempt at it went.
            _dice = new SeededRandomSource(RunDice.SeedFor(run.Seed, run.Cleared));

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
            _focus.PopToRoot();

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

        // Hold on the outcome first, for the probe exactly as for a person: the
        // Outcome card is one of the focuses #327 moves, so it earns the same real
        // input path everything else in the probe takes. This used to short-circuit
        // straight to CompleteAndReport under --probe, from before the probe could
        // press a key at all (#499) — RunProbe now dismisses the card itself.
        _focus.Push(new PlayFocus.Outcome());
        QueueRedraw();
    }

    /// <summary>Finishes the fight the card was announcing: rewards, save, interlude.</summary>
    private void CompleteAndReport()
    {
        _focus.PopToRoot();

        if (_run is not { } run || _fight is not { } fight || _encounter is not { } encounter)
        {
            return;
        }

        var levelUpsBefore = run.LevelUps.Count;
        var lootBefore = run.LootFound.Count;
        var magicItemFindersBefore = run.MagicItemFinders.Count;

        run.CompleteFight(fight, _dice);

        var after = new List<string>
        {
            encounter.WinningSide == PregeneratedParty.SideId
                ? $"Fight {run.Cleared} cleared!"
                : "The party falls.",
        };

        after.AddRange(run.LevelUps.Skip(levelUpsBefore).Select(line => line + "!"));
        after.AddRange(run.LootFound.Skip(lootBefore).Select(line => line + "!"));

        // Right where the award lands, not one screen later (#534): a permanent
        // item's resolved effect is stated here, next to the "finds ..." line that
        // named it, rather than only surfacing the next time this character's panel
        // draws.
        foreach (var finder in run.MagicItemFinders.Skip(magicItemFindersBefore))
        {
            var sheet = run.Party[finder].Sheet;
            var announcement = MagicItemReadout.Announce(
                run.Party[finder].Draft.Name,
                sheet.MagicItemNames,
                sheet.SpellAttackItemBonus,
                sheet.IgnoresHalfCoverOnSpellAttacks);

            if (announcement.Length > 0)
            {
                after.Add(announcement);
            }
        }

        if (run.Outcome != RunOutcome.Defeated)
        {
            if (_isNewRun)
            {
                SaveFile.BeginNewRun(_savePath, RunSave.ToJson(run));
                _isNewRun = false;
            }
            else
            {
                SaveFile.ContinueWrite(_savePath, RunSave.ToJson(run));
            }
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
        int? slot = null)
    {
        // Targeting stacks over the menu that chose it rather than replacing it, so Esc
        // hands that menu back (#509). The menu stays on the stack but stops drawing, since
        // every menu draws only while it is on top — which is why the screen looks exactly
        // as it did when targeting replaced it outright.
        _focus.Push(new PlayFocus.Targeting(kind, attack, spell, slot));

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
            if (PlayTurnFlow.NothingLeftButEndTurn(
                    _focus,
                    TurnOptions.For(encounter, commanded)))
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

        if (commanded is not null && Armed is null)
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

            // A separate line from ResourceLine on purpose (#534): that method's
            // contract is "what this character has left to spend", and every entry in
            // it is expendable — slots, uses, potions. A passive item spends nothing,
            // so it gets its own row rather than corrupting that grammar.
            var equipment = EquipmentLine(character);

            if (equipment.Length > 0)
            {
                DrawString(
                    TextFont,
                    new Vector2(UiLeft, ButtonRowTop + 28 + 34),
                    Trim(equipment, 95),
                    fontSize: 12,
                    modulate: Dim);
            }
        }

        if (_notice is { } notice)
        {
            DrawString(
                TextFont,
                new Vector2(UiLeft, ButtonRowTop + 28 + 56),
                Trim(notice, 78),
                fontSize: 13,
                modulate: MonsterColour);
        }

        // Clearing and drawing have separate lifecycles (S5, #504 round 3). The row list
        // (one now, not three — #505) is emptied unconditionally, every frame, regardless of
        // phase, of whether anyone is commanded, or of what the stack holds — a *stronger*
        // form of HitTest's invariant than the three DrawXMenu methods used to give it
        // themselves before #505 (a closed menu's rows are gone before the traversal below
        // even runs, not merely "cleared by whichever method used to own that list"). Only
        // then does the traversal decide whether it gets repopulated.
        ClearMenuRows();

        // Which card is showing is the focus stack's answer, not four conditions written
        // out by hand — that was the third and last copy of the modal order, and it is
        // gone. What this is *not*, deliberately: a z-order mechanism.
        //
        // S5 first shipped this as a `foreach (layer in _focus.BottomUp)` dispatch, on the
        // reading that draw order should follow stack order. Review knocked that out by
        // reversing the traversal: every capture stayed byte-identical, because **no two of
        // these cards can draw in the same frame**. A row menu draws only when it is
        // _focus.Top, so at most one of the three; and the outcome card only exists once
        // the fight is complete, which is exactly when CommandedCombatant() returns null
        // ("_encounter is { IsComplete: false }") and every menu case is dead. An ordering
        // loop whose order provably cannot matter is a mechanism that looks like it decides
        // something and does not, which is the shape this project keeps having to catch.
        // So the loop is not here, and FocusStack.BottomUp is not described as draw order.
        //
        // Two cards genuinely can be up at once — Esc during the closing animation leaves
        // QuitConfirm open and _Process then pushes Outcome above it — and that pair's
        // order is still hand-written below, by name, for the reason on DrawQuitCard.
        // When a second pair of stack-traversed cards can coexist, the loop earns its place
        // and this comment is the note that says so.
        //
        // PlayFocus.Board and PlayFocus.Targeting draw no card and appear here at all: that
        // is correct, not an omission — Targeting changes how the *board* draws, which the
        // board has read off the stack since S1.
        switch (_focus.Top)
        {
            case PlayFocus.SpellMenu when commanded is { } spellCaster:
                DrawSpellMenu(spellCaster);
                break;

            case PlayFocus.AttackMenu when commanded is { } attacker:
                DrawAttackMenu(attacker);
                break;

            case PlayFocus.SlotMenu { Spell: { } spell } when commanded is { } slotCaster:
                DrawSlotMenu(slotCaster, spell);
                break;
        }

        // Not switched on Top with the menus above: the outcome card draws while it is
        // anywhere in the stack, including underneath QuitConfirm in the Esc-during-the-
        // closing-animation state. Holds, not Top — the pre-S5 reading, kept because it is
        // the correct one and Top would silently blank the card under the quit question.
        if (_focus.Holds<PlayFocus.Outcome>())
        {
            DrawOutcomeCard();
        }

        // Last, so it sits over everything it might explain.
        DrawHint();

        // The one card not drawn off the stack's order (see DrawQuitCard's remarks):
        // it stays named here, after the hint, rather than folding into a loop that would
        // put it under a tooltip that can be raised while it is up.
        DrawQuitCard();
    }

    /// <summary>
    /// The card that asks whether Esc really meant to leave. It says what quitting
    /// costs — the save keeps the state after the last <em>cleared</em> fight, so a
    /// fight in progress restarts — because that cost is exactly what an accidental
    /// exit was paying without asking.
    /// </summary>
    /// <remarks>
    /// <b>Drawn last, by name, after <see cref="DrawHint"/> — not decided by
    /// <c>_focus.Top</c> the way the other cards are (S5, #504).</b>
    /// <see cref="PlayFocus.QuitConfirm"/> is a layer like any other, but a tooltip must
    /// never occlude the question that closes the game, and the hint genuinely can be
    /// raised while this card is up: <c>AdvanceHover</c> runs from <c>_Process</c> in
    /// <see cref="Phase.Fighting"/> regardless of quit state, so a hint from before Esc was
    /// pressed can still appear after it. Making <c>QuitConfirm</c> draw in its stack
    /// position would put it under the hint and change a pixel. One documented exception is
    /// cheaper than a sixth trait on <see cref="PlayFocus"/> for a single case.
    /// </remarks>
    private void DrawQuitCard()
    {
        if (!_focus.Holds<PlayFocus.QuitConfirm>())
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
    /// <para>
    /// It says *why* as well as what, because an objective rung can end with enemies
    /// still standing and a bare "you win" over a field of live goblins is the confusing
    /// part rather than the satisfying one.
    /// </para>
    /// <para>
    /// <b>Called only from <c>_Draw</c>'s traversal, when the layer it is visiting is
    /// <see cref="PlayFocus.Outcome"/> (S5, #504 round 3)</b> — no guard of its own is
    /// needed here, unlike the row menus: nothing is ever pushed above
    /// <see cref="PlayFocus.Outcome"/> (qc's #504 review checked every <c>Push</c> site; its
    /// own <c>Escape</c> is <c>Commit</c>, not <c>AskToQuit</c>, so even the quit card cannot
    /// land on top of it), so the traversal encountering this layer at all is already the
    /// whole answer.
    /// </para>
    /// </remarks>
    private void DrawOutcomeCard()
    {
        if (_encounter is not { } encounter)
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
        if (Shopping is not null && _run is { } shopping)
        {
            DrawShop(shopping);
            return;
        }

        var y = UiTop + 8f;

        // Wrapped, not Trim()'d: a refusal names both the problem and the remedy, and
        // a hard 100-character cut can (and did — qc's #470 review, measured) sever the
        // remedy from a message compiled with both in one string. Every _interlude
        // entry goes through this, not just the one qc measured, because the next
        // multi-flag refusal is one string concatenation away from the same cut.
        foreach (var line in _interlude)
        {
            if (line.Length == 0)
            {
                y += 10;
                continue;
            }

            foreach (var wrapped in Wrap(line, 100))
            {
                DrawString(TextFont, new Vector2(UiLeft, y), wrapped, fontSize: 14, modulate: Ink);
                y += 22;
            }
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

        if (Shopping?.Notice is { } notice)
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

    /// <summary>
    /// What this character carries and what it resolves to (#534) — separate from
    /// <see cref="ResourceLine"/> because a passive item spends nothing, so it does not
    /// belong in a line whose every other entry is a use counting down. Empty for a
    /// character with nothing equipped.
    /// </summary>
    private static string EquipmentLine(Combatant character)
    {
        if (character.Stats.Character is not { } features || features.MagicItemNames.Count == 0)
        {
            return string.Empty;
        }

        return "Equipment: " + MagicItemReadout.Describe(
            features.MagicItemNames,
            features.SpellAttackItemBonus,
            character.Stats.IgnoresHalfCoverOnSpellAttacks,
            features.SpellAttackBonus);
    }

    /// <summary>
    /// Empties the row-menu list. Called once, unconditionally, before <c>_Draw</c>'s
    /// traversal decides whether it gets repopulated (S5, #504 round 3).
    /// </summary>
    /// <remarks>
    /// This is what makes <c>HitTest</c>'s invariant hold now — "a closed menu holds no
    /// rectangles". Before #505 this cleared three separate lists, one per menu, because
    /// each of <see cref="DrawSpellMenu"/>, <see cref="DrawAttackMenu"/> and
    /// <see cref="DrawSlotMenu"/> filled its own; now there is one list and one traversal
    /// repopulates it for whichever menu <c>_focus.Top</c> names. A menu that was just
    /// popped is no longer in <c>_focus.BottomUp</c> at all, so a traversal keyed on
    /// presence could never have cleared it — clearing first, then walking, is what keeps
    /// that from being a stale-rectangle regression.
    /// </remarks>
    private void ClearMenuRows()
    {
        _menuRows.Clear();
    }

    /// <summary>
    /// The spells this character could cast this instant, in the order the spell menu
    /// lists them — the same rule that decides whether the Cast button is there at all, so
    /// the list can never offer a row whose only possible answer is a refusal.
    /// </summary>
    /// <remarks>
    /// Pulled out on its own (#505) so <see cref="RunSlotMenuProbe"/> can find the same row
    /// <see cref="DrawSpellMenu"/> will draw for a given spell without reading
    /// <c>_menuRows</c>'s contents — the unified list carries only a rectangle and an
    /// <see cref="Action"/> now, not the spell that closed over it, so the probe recomputes
    /// the ordering instead of reaching into the row for a payload it no longer has.
    /// </remarks>
    private static IEnumerable<SpellDefinition> CastableSpells(Combatant character) =>
        character.Stats.Character is not { } features
            ? []
            : features.Spells
                .Where(spell => TurnOptions.CanCastNow(character, spell))
                .OrderBy(spell => spell.Level)
                .ThenBy(spell => spell.Name, StringComparer.Ordinal);

    /// <summary>
    /// The spell list overlay. Called only from <c>_Draw</c>'s traversal, when
    /// <see cref="PlayFocus.SpellMenu"/> is both the layer being visited and
    /// <c>_focus.Top</c> — this method itself no longer checks either (S5, #504 round 3).
    /// <c>_menuRows</c> is <em>not</em> cleared here any more: <c>ClearMenuRows</c> empties
    /// it, unconditionally, before the traversal runs at all, whether or not this method
    /// gets called this frame. The unguarded <c>(PlayFocus.RowMenu)_focus.Top</c> cast below
    /// relies on that same invariant — it is what every row this call adds is stamped with
    /// (<see cref="MenuRowList"/>, #505).
    /// </summary>
    private void DrawSpellMenu(Combatant character)
    {
        if (character.Stats.Character is null)
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

        var castable = CastableSpells(character);
        var menu = (PlayFocus.RowMenu)_focus.Top;

        foreach (var spell in castable)
        {
            var rect = new Rect2(UiLeft, y, 260, 20);
            _menuRows.Add(menu, rect, () => ChooseSpell(spell));

            DrawRect(rect, GridLine);

            if (_menuRows.CountFor(menu) - 1 == menu.MenuIndex)
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

    /// <summary>
    /// The attack list overlay. See <see cref="DrawSpellMenu"/>'s remarks: called only when
    /// <see cref="PlayFocus.AttackMenu"/> is both the layer being visited and
    /// <c>_focus.Top</c>, and <c>_menuRows</c> is cleared by <c>ClearMenuRows</c> before
    /// the traversal runs, not by this method.
    /// </summary>
    private void DrawAttackMenu(Combatant character)
    {
        // Over the board, as an overlay. These lists used to live under the second
        // button row; fullscreen gave that ground to the board, and every row below
        // already paints its own filled backdrop, so only the header needs one.
        var top = UiTop + 28;

        DrawRect(
            new Rect2(UiLeft - 8, top - 24, 470, 30),
            new Color(Background.R, Background.G, Background.B, 0.9f));

        DrawString(TextFont, new Vector2(UiLeft, top - 6), "ATTACKS — click one, or arrows and Enter", fontSize: 12, modulate: Dim);

        var y = top + 6;

        var menu = (PlayFocus.RowMenu)_focus.Top;

        foreach (var attack in character.Stats.Attacks)
        {
            var rect = new Rect2(UiLeft, y, 300, 20);
            _menuRows.Add(menu, rect, () => ChooseAttack(attack));

            if (_menuRows.CountFor(menu) - 1 == menu.MenuIndex)
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

    /// <summary>
    /// The slot-level overlay. See <see cref="DrawSpellMenu"/>'s remarks: called only when
    /// <see cref="PlayFocus.SlotMenu"/> is both the layer being visited and
    /// <c>_focus.Top</c>, with <paramref name="spell"/> the very layer's own
    /// <see cref="PlayFocus.SlotMenu.Spell"/> rather than re-derived here. <c>_menuRows</c>
    /// is cleared by <c>ClearMenuRows</c> before the traversal runs, not by this method.
    /// </summary>
    private void DrawSlotMenu(Combatant character, SpellDefinition spell)
    {
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

        var menu = (PlayFocus.RowMenu)_focus.Top;

        foreach (var level in SlotLevelsFor(character, spell))
        {
            var rect = new Rect2(UiLeft, y, 260, 20);
            _menuRows.Add(menu, rect, () => ChooseSlot(level));

            if (_menuRows.CountFor(menu) - 1 == menu.MenuIndex)
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
            return _focus.Holds<PlayFocus.Outcome>()
                ? "the fight is over — any key or click for the results"
                : encounter.WinningSide == PregeneratedParty.SideId
                    ? "the party wins — [esc] quit"
                    : "the party has fallen — [esc] quit";
        }

        if (Armed is { Kind: TargetKind.Spell, Spell: { } spell } castingAt)
        {
            return castingAt.Slot is { } slot
                ? $"choose a target for {spell.Name} (level {slot} slot) — click it, Tab cycles, Enter takes it; Esc cancels"
                : $"choose a target for {spell.Name} — click it, Tab cycles, Enter takes it; Esc cancels";
        }

        if (Armed is { Kind: TargetKind.Attack } swingingAt)
        {
            return swingingAt.Attack is { } attack
                ? $"choose a target for {attack.Name} — click it, Tab cycles, Enter takes it; Esc cancels"
                : "choose a target — Tab cycles, Enter attacks with the best weapon; Esc cancels";
        }

        if (Armed is { Kind: TargetKind.Potion })
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
        await CaptureFrame(Path.Combine(directory, "play-1-turn-ready.png"));

        // The quit confirm, from a cold board: Esc asks, and anything but a second
        // Esc — a key here — backs out unharmed, so the rest of the probe starts
        // from the same clean turn it always did.
        Press(Key.Escape);
        await CaptureFrame(Path.Combine(directory, "play-1b-quit-confirm.png"));
        Press(Key.Space);

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
        // The button's real caption carries its hotkey ("X · Cunning Dash"), same as
        // ClickButton's own match — a bare-string == here found nothing (#499).
        var feature = _buttons.FirstOrDefault(button => button.Caption.EndsWith(" · Cunning Dash", StringComparison.Ordinal)).Caption
            ?? _buttons.FirstOrDefault(button => button.Rect.Position.Y > ButtonRowTop + 1).Caption;

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

            if (_menuRows.Count > 0)
            {
                Click(_menuRows[0].GetCenter());
                Click(CentreOf(victim.Position));
                await CaptureFrame(Path.Combine(directory, "play-8-cast.png"));
            }
            else
            {
                ReportSkip(directory, "play-8-cast", "the caster's spell menu offered no castable rows");
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

            await CaptureFrame(Path.Combine(directory, "run-9-after-fight.png"));

            // The merchant reaches the party only at a Long Rest — the opening cycle's
            // own rungs after the first (GauntletLadder), which is exactly the
            // interlude fight 1 clearing on seed 1 lands on.
            if (_phase == Phase.Interlude && _shopAvailable)
            {
                Click(_shopButton.GetCenter());
                await CaptureFrame(Path.Combine(directory, "run-10-shop.png"));
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
    /// something a person has to notice by counting.
    /// </summary>
    private static void ReportSkip(string directory, string name, string reason)
    {
        GD.Print($"probe: skipped {name} — {reason}");
        File.WriteAllText(Path.Combine(directory, name + ".skipped.txt"), reason + "\n");
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
