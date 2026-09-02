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
        try
        {
            _seed = SeedArgument();
        }
        catch (ScenarioRefusedException refusal)
        {
            // No seed was ever settled, so there is nothing honest to put after "seed"
            // in the subtitle the way every other refusal here does — the message names
            // the bad value itself.
            _phase = Phase.RunOver;
            _interlude.Add(refusal.Message);
            _subtitle = "seed refused";
            return;
        }

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

        // --level only ever means one thing here: where a *new* run begins. Decided once,
        // before either branch below, because a resumed run has nothing for it to apply
        // to (GauntletRun.Resume re-resolves at the level the save's own experience has
        // earned) and letting it through silently there would be exactly the shape #488
        // exists to close, just for --continue instead of a bad number.
        if (!TryResolveGauntletLevel(
                HasArgument("continue"), HasArgument("level"), ArgumentValue("level"), out var level, out var levelError))
        {
            _phase = Phase.RunOver;
            _interlude.Add(levelError!);
            _subtitle = $"seed {_seed}";
            return;
        }

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
            // GauntletRun.Start's created-drafts overload takes the same startingLevel a
            // pregenerated party's does — a created party is always drafted at level 1
            // (CreateMode) and resolved up to whatever level the run begins at, exactly
            // like a resumed save re-resolving at the level its experience earned
            // (ResolveMember's own ASI-default-notice handling covers both). The level
            // parsed above used to be computed and then silently discarded on this branch
            // (#488) — --create --level=4 started at 1 with nothing said — so it is passed
            // through here now, the same as the pregenerated branch below it.
            _run = CreatedDrafts is not null
                ? GauntletRun.Start(content, CreatedDrafts, seed: _seed, startingLevel: level)
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
    /// The pure half of the gauntlet-start <c>--level</c> (#488): given whether
    /// <c>--continue</c> and <c>--level</c> were passed and the latter's value, decides
    /// the level a fresh run begins at, or refuses. Split out of <see cref="OnReady"/> the
    /// same way <see cref="FightScreen.ScenarioFromFile"/> is split from reading
    /// <c>--scenario</c> (#476) — <c>HasArgument</c>/<c>ArgumentValue</c> reach into
    /// Godot's <c>OS</c> singleton and cannot run under a plain xUnit test, while
    /// everything below this line is ordinary rules over plain values, closing part of
    /// #490's stated gap that nothing pins this screen's argv wiring.
    /// </summary>
    /// <remarks>
    /// <paramref name="levelGiven"/> and <paramref name="continuing"/> together decide
    /// three cases. Both true: <c>--level</c> has nothing to apply to on a resumed
    /// run — <see cref="GauntletRun.Resume"/> re-resolves at the level the save's
    /// own experience earned — so this refuses rather than silently dropping the flag,
    /// the same shape #463 closed for <c>--spawn</c> against the gauntlet loop.
    /// <paramref name="continuing"/> true and <paramref name="levelGiven"/> false: the
    /// caller ignores the returned level entirely, so it is set to the harmless default
    /// below rather than left undefined. Neither: a fresh run keeps its old default of
    /// level 1 when <c>--level</c> is absent — not <see cref="ScenarioArguments"/>'s own
    /// default of 3, which is spawn mode's own budgeted-fight concern (its remarks say
    /// so) — and reuses <see cref="ScenarioArguments.TryParseLevel"/> for the actual
    /// parse and range check by forcing <c>present: true</c>, so this method's own
    /// absent-means-1 case never touches that helper's absent-means-3 branch.
    /// </remarks>
    internal static bool TryResolveGauntletLevel(
        bool continuing, bool levelGiven, string? levelText, out int level, out string? error)
    {
        if (continuing && levelGiven)
        {
            level = default;
            error = "--level refused: --continue resumes at the level the save's own " +
                "experience has earned; --level does not apply here. Start a new run to choose one.";
            return false;
        }

        if (!levelGiven)
        {
            level = 1;
            error = null;
            return true;
        }

        if (!ScenarioArguments.TryParseLevel(levelText, present: true, out var parsed, out var levelError))
        {
            level = default;
            error = $"--level refused: {levelError}";
            return false;
        }

        level = parsed;
        error = null;
        return true;
    }

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
}
