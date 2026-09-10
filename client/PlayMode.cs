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

    /// <summary>
    /// The route a move would actually walk if committed to whichever reachable square
    /// the pointer is over right now (#303) — <see cref="MovementRules.FindPath"/>'s own
    /// answer, so what is drawn can never diverge from what a click on that square would
    /// do. Empty whenever nothing is hovered, nobody is commanded, or the hovered square
    /// is not one <see cref="_reachable"/> already offers. See
    /// <see cref="HoverPreviewPath"/> for the computation and its fog rule.
    /// </summary>
    private readonly List<GridPosition> _previewPath = [];

    /// <summary>
    /// Which square <see cref="_previewPath"/> was last computed for, or null (#303,
    /// PR #731 round 1 review). <see cref="PreviewSquareChanged"/> compares this
    /// against the pointer's current square on every raw motion sample — comparing
    /// pixel distance instead was the original bug (<see cref="HoverJitterPixels"/>
    /// exists for the tooltip's own reasons, unrelated to which square a route
    /// should point at) — so this is a square, never a pixel.
    /// </summary>
    private GridPosition? _previewSquare;

    /// <summary>
    /// Which of <see cref="_previewPath"/>'s squares provoke an Opportunity Attack from
    /// an enemy the party can currently see, if the walk is actually taken (#301) —
    /// <see cref="ThreatenedSteps"/>'s own answer, computed alongside <see
    /// cref="_previewPath"/> in <see cref="UpdatePreviewPath"/> from the same call, never
    /// a second guess drawn independently of it. Empty under exactly the conditions
    /// <see cref="_previewPath"/> itself is empty (nobody commanded, nothing hovered, the
    /// preview gated off by <see cref="PreviewMayShow"/>), since a threat mark on a route
    /// that is not itself shown would be advice about a walk the screen never offered.
    /// </summary>
    private readonly List<GridPosition> _threatenedSteps = [];

    /// <summary>
    /// The normal-range band of an armed attack's or non-area spell's envelope (#302)
    /// — every square a target could stand in for the click that is about to happen to
    /// actually reach. Computed in <see cref="UpdateTargetingPreview"/>, the targeting
    /// family's own counterpart to the movement family's <see cref="_previewPath"/>:
    /// the two are never populated in the same frame, since <see cref="Armed"/> being
    /// an attack or a spell is exactly the condition under which
    /// <see cref="PreviewMayShow"/> already answers false for the route preview.
    /// </summary>
    private readonly List<GridPosition> _rangeNormal = [];

    /// <summary>
    /// The long-range band of an armed ranged attack (#302) — beyond its own printed
    /// normal range but still reachable at Disadvantage
    /// (<see cref="SRDCombat.Core.Combat.CombatAttack.IsAtLongRange"/>). Always empty
    /// for a melee attack, a spell, or an attack with no chosen weapon (Tab's cold arm
    /// reads several attacks generously rather than picking one to show a long band
    /// for — see <see cref="AttackRangeEnvelope"/>'s own remarks).
    /// </summary>
    private readonly List<GridPosition> _rangeLong = [];

    /// <summary>
    /// Exactly what an armed area spell would cover for the hovered origin (#302) —
    /// <c>AreaTargeting.Cover</c>'s own answer for the caster's square and the hovered
    /// square, fog-trimmed the same way <see cref="_previewPath"/> is. Recomputed on
    /// every hover, unlike <see cref="_rangeNormal"/>/<see cref="_rangeLong"/>, whose
    /// envelope depends only on the actor and the weapon — never a second guess at
    /// what the aim point would actually cover.
    /// </summary>
    private readonly List<GridPosition> _areaCoverage = [];

    /// <summary>
    /// The ids of the creatures <see cref="_areaCoverage"/> would actually catch —
    /// visible ones only, the same fog standard a hidden creature's token, ring and
    /// hover hint are already held to (#732's leak shape, the other side of it here).
    /// </summary>
    private readonly HashSet<string> _areaCaughtIds = [];

    /// <summary>
    /// True when the square or creature under the pointer is outside the armed
    /// attack's or spell's own range (#302) — the same fact the click would be refused
    /// for, shown before the click rather than after it. <see
    /// cref="_targetingOutOfRangeCode"/> names which of the click's own refusal codes
    /// applies.
    /// </summary>
    private bool _targetingOutOfRange;

    /// <summary>
    /// The refusal code the click would actually produce right now, whenever <see
    /// cref="_targetingOutOfRange"/> is true — <c>attack.out_of_range</c> when a named
    /// weapon is armed, <c>client.no_attack</c> for Tab's cold arm (this client's own
    /// fallback when no carried attack reaches — see <see cref="ActivateSquare"/>), or
    /// <c>spell.out_of_range</c> for a spell. Never invented: each is the literal code
    /// the engine or this screen's own click path already uses.
    /// </summary>
    private string? _targetingOutOfRangeCode;

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

    /// <summary>
    /// Where the pointer actually is right now — updated on every raw motion sample,
    /// unconditionally (#303, PR #731 round 3 review). Every refresh that recomputes
    /// the path preview from a remembered pixel (a routed keyboard action, a camera
    /// change, <see cref="RefreshAfterAction"/>) reads this one, so it must never lag
    /// behind an in-square move: a pointer that drifts from A to B in a two-pixel
    /// sample well under <see cref="HoverJitterPixels"/> is looking at B's square the
    /// instant it happens, and a refresh that read a stale A here would restore A's
    /// route for a click that is about to walk to B. <see cref="_hintAnchor"/> is the
    /// tooltip's own separate, deliberately jitter-filtered pixel — the two used to be
    /// the same field, which is exactly how this bug happened.
    /// </summary>
    private Vector2 _pointer;

    /// <summary>
    /// The pixel the hover hint last considered "settled" — <see cref="_pointer"/>
    /// before round 3, kept only for the tooltip's own reasons now that
    /// <see cref="_pointer"/> itself updates unconditionally. A tooltip that appeared
    /// the instant the pointer crossed something would turn a glance across the row
    /// into a flicker of popups; movement past <see cref="HoverJitterPixels"/> from
    /// here restarts <see cref="_hoverElapsed"/> and moves this anchor to match, so a
    /// hand that never quite stops still settles, and the tooltip itself draws beside
    /// this pixel rather than chasing every sub-pixel twitch of <see cref="_pointer"/>.
    /// </summary>
    private Vector2 _hintAnchor;

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

    /// <summary>
    /// True once the probe's <c>play-2e-threat-preview</c> step (#301) has found and
    /// captured a real Opportunity-Attack threat mark — searched fresh across the
    /// opening commanded turn and every later one in the fight-1 play-out loop, never
    /// twice, and never again once one lands. See <c>PlayMode.Probe.cs</c>'s
    /// <c>TryCaptureThreatPreview</c>.
    /// </summary>
    private bool _threatPreviewCaptured;

    /// <summary>
    /// The commanded combatant <c>TryCaptureThreatPreview</c> last searched for a
    /// threatened square, so a turn spanning many probe frames (animations, the
    /// pace delay) is searched once rather than once per frame — cheap on its own,
    /// but the play-out loop calls it every frame for however long a turn takes.
    /// </summary>
    private string? _threatPreviewLastAttemptedId;

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

    /// <summary>
    /// How many offers the stall's window last drew (#704) — <see cref="ShopLayout.Fit"/>'s
    /// own answer, kept so a Page Up/Down press can jump by a whole page rather than the
    /// wheel's single row. Zero before the stall has drawn once, which
    /// <see cref="PlayMode.ScrollShop"/> reads as "at least one row" rather than a no-op.
    /// </summary>
    private int _shopVisibleCount;
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

        // The whole post-seed launch decision, resolved once before any mode branch
        // below: the save path (a bare --save, present with no value, refused rather
        // than silently defaulting — #654), the gauntlet-start gates
        // (--spawn/--scenario/--difficulty without --one-fight all do nothing the
        // gauntlet loop reads, and --continue with --level or a bad --level has nothing
        // honest to fall back to — #463, #476, #488, #443), and which of the three
        // launch modes this is. PlayModeLaunch.TryResolve (#491) folds
        // SavePathArgument.TryResolve and GauntletStart.Resolve — in that order, so a
        // bad --save wins over a bad gauntlet flag exactly as it did when the two ran as
        // separate blocks here — plus the one-fight/continue/fresh mode selection into
        // one result, so the composition of those decisions is pinned by a plain xUnit
        // test (SRDCombat.Game.Tests.PlayModeLaunchTests) rather than re-expressed in
        // this live node. The seed above stays this node's own first step: its default
        // roll is ambient and probe/capture-aware, so it cannot move into a pure Game
        // function (PlayModeLaunch's own remarks say why), which is why a seed refusal
        // reads "seed refused" with no number and every refusal below reads
        // $"seed {_seed}". The same HasArgument presence predicates ResolveFight's own
        // spawn/scenario branch keys on (FightScreen.cs) feed this, so this gate and
        // that branch never disagree about whether a flag was passed (#470, M2).
        if (!PlayModeLaunch.TryResolve(
                save: HasArgument("save") ? FlagValue.Of(ArgumentValue("save")) : FlagValue.Absent,
                spawn: HasArgument("spawn") ? FlagValue.Of(ArgumentValue("spawn")) : FlagValue.Absent,
                scenario: HasArgument("scenario") ? FlagValue.Of(ArgumentValue("scenario")) : FlagValue.Absent,
                oneFight: HasArgument("one-fight"),
                continuing: HasArgument("continue"),
                level: HasArgument("level") ? FlagValue.Of(ArgumentValue("level")) : FlagValue.Absent,
                difficulty: HasArgument("difficulty") ? FlagValue.Of(ArgumentValue("difficulty")) : FlagValue.Absent,
                out var launch, out var launchError))
        {
            _phase = Phase.RunOver;
            _interlude.Add(launchError!);
            _subtitle = $"seed {_seed}";
            return;
        }

        _savePath = launch.SavePath;

        // A probe run drives the screen through its own input path — synthesized clicks
        // through the viewport — and captures what each one produced. Monsters hurry so
        // the probe spends its time on the party's turns, the part being verified.
        if (HasArgument("probe"))
        {
            _pace = 0.05;
            _animateWalks = false;
        }

        if (launch.Mode == PlayModeLaunchMode.OneFight)
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
        // before it was ever read. _savePath is already resolved above, before the
        // --one-fight branch.

        // Collected rather than appended straight to _interlude: EnterInterlude below
        // starts every screen with _interlude.Clear(), so anything added before that
        // call would be wiped before the first frame ever showed it.
        var startupNotices = new List<string>();

        // --level only ever means one thing here: where a *new* run begins. Resolved
        // once above by PlayModeLaunch.TryResolve, before either branch below, because a
        // resumed run has nothing for it to apply to (GauntletRun.Resume re-resolves at
        // the level the save's own experience has earned) and letting it through
        // silently there would be exactly the shape #488 exists to close, just for
        // --continue instead of a bad number.
        var level = launch.Level;

        if (launch.Mode == PlayModeLaunchMode.Continue)
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

            // A present content version that disagrees with what is loaded is no
            // longer refused here (#355) — GauntletRun.Resume folds it into LevelUps
            // as a notice instead and resolves anyway, so an F4 content addition does
            // not orphan every save whose ids still resolve. What still refuses drift
            // is per-id resolution: ContentDrift.Require's checks throw
            // InvalidDataException; CharacterResolver's own weapon, armor and magic
            // item checks throw ArgumentException instead — a Core-level convention
            // this Game-level catch has to know about too, or exactly this drift
            // crashes instead of refusing. Either way this is a printed message,
            // never a crash, and the file itself is never touched.
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
        // visible — alongside a content-version notice (#355) if this save's
        // fingerprint disagreed with what just loaded. LevelUps is empty on a fresh
        // Start, so this only adds anything on a resumed save.
        startupNotices.AddRange(_run.LevelUps.Select(notice => notice + "!"));

        EnterInterlude(startupNotices);
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
            // The camera's own automatic glide moves GridLeft/GridTop/CellPixels under
            // a pointer that may not have moved a single pixel — a turn's opening
            // re-centre on the new active combatant, say (#303 defect #3, PR #731
            // round 1 review). UpdatePreviewPath re-maps the tracked pointer against
            // whatever the camera answers right now, the same as the manual branches
            // in _UnhandledInput already do for a drag or a zoom.
            UpdatePreviewPath(_pointer);
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
