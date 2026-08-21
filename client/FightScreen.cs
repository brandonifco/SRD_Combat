using Godot;
using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Viewer;

/// <summary>
/// What the two screens share: the palette, the layout, how a fight is built and drawn,
/// and the capture loop that verifies a change without a person watching.
/// </summary>
/// <remarks>
/// <b>Nothing here recomputes a rule.</b> Positions, hit points, conditions and the
/// narration all come off the engine; a screen decides only where to put them. That is
/// the same discipline the console client is held to, and it is what keeps the client
/// from becoming a second place the rules live.
/// </remarks>
public abstract partial class FightScreen : Node2D
{
    /// <summary>Where the fixed chrome — headings, menus, interludes — starts.</summary>
    protected const int UiLeft = 24;
    protected const int UiTop = 96;

    /// <summary>The initiative-and-log panel's width; its left edge follows the window.</summary>
    protected const int PanelWidth = 450;

    /// <summary>
    /// The window's real size, read from the viewport on every use. The chrome anchors
    /// to the actual edges — the panel to the right, the play screen's buttons to the
    /// bottom — so no window is too small or too large to show the controls; the field
    /// underneath pans and zooms to whatever ground is left. These were constants for a
    /// 1920×1080 design canvas once, and on any screen shorter than that the button row
    /// sat below the window's edge, invisible at every size the window could take.
    /// </summary>
    protected int ScreenWidth => (int)GetViewportRect().Size.X;
    protected int ScreenHeight => (int)GetViewportRect().Size.Y;
    protected int PanelLeft => ScreenWidth - PanelWidth;
    protected const double SecondsPerTurn = 0.6;

    /// <summary>
    /// Bounds on the camera's square. The ceiling keeps a huddle of two from drawing
    /// squares so large the figures look like portraits; the floor keeps a huge field
    /// legible rather than letting it vanish. The manual ceiling is higher on purpose:
    /// a player leaning in with the wheel has asked for portraits.
    /// </summary>
    private const int LargestCell = 128;
    private const int SmallestCell = 24;
    private const int LargestManualCell = 160;

    /// <summary>
    /// The stage: the part of the window the camera composes for. The field itself runs
    /// under every overlay — the panel, the heading, the play screen's bottom strip —
    /// but the camera aims the fight at the ground between them, so what matters is
    /// never parked under the log.
    /// </summary>
    private const float StageLeft = 0;
    private const float StageTop = 88;
    private float StageRight => PanelLeft - 16;
    private float StageBottom => ScreenHeight - 130;

    /// <summary>Empty ground the camera keeps around the fight, in squares.</summary>
    private const float CameraPaddingSquares = 2.5f;

    /// <summary>How quickly the camera chases its aim, per second. Bigger is snappier.</summary>
    private const double CameraChaseRate = 4;

    /// <summary>How far the wheel steps the zoom per notch.</summary>
    private const float WheelZoomFactor = 1.15f;

    /// <summary>
    /// The board square, in pixels — the camera's zoom, smoothed toward wherever
    /// <see cref="AimCamera"/> last pointed it. Only meaningful once a fight has been
    /// adopted; the default is what an empty screen draws its (nonexistent) grid at.
    /// </summary>
    protected float CellPixels { get; private set; } = 48;

    /// <summary>The point of the field, in squares, sitting at the stage's centre.</summary>
    private Vector2 _cameraCentre;

    /// <summary>Where the camera is heading: centre and zoom, smoothed toward.</summary>
    private Vector2 _cameraAim;
    private float _cameraCellAim = 48;

    /// <summary>
    /// True while the player has the camera — a wheel zoom or a drag holds it until the
    /// next act starts or the turn moves on, which is when the fight takes it back.
    /// </summary>
    private bool _cameraManual;

    /// <summary>Whose turn it was when the player took the camera — a change hands it back.</summary>
    private string? _manualForActiveId;

    private bool _cameraDragging;
    private Vector2 _dragLast;

    /// <summary>The active combatant most recently drawn — who the camera leans toward.</summary>
    private string? _lastActiveId;

    /// <summary>
    /// The board's top-left corner on screen, derived: the camera's centre sits at the
    /// stage's centre, and everything else is measured off it. A property rather than a
    /// stored number so no draw can read a stale origin.
    /// </summary>
    protected float GridLeft => ((StageLeft + StageRight) / 2f) - (_cameraCentre.X * CellPixels);
    protected float GridTop => ((StageTop + StageBottom) / 2f) - (_cameraCentre.Y * CellPixels);

    protected static readonly Color Background = new("16161d");
    protected static readonly Color GridLine = new("2c2c38");
    protected static readonly Color Difficult = new("2a2438");

    /// <summary>
    /// What difficult ground looks like over real terrain: dark enough to read as rough
    /// going, sheer enough to leave the tile underneath recognisable.
    /// </summary>
    protected static readonly Color DifficultWash = new(0.10f, 0.06f, 0.16f, 0.45f);
    protected static readonly Color Blocked = new("3a2a2a");
    protected static readonly Color LowObstacle = new("4a4032");
    protected static readonly Color PartyColour = new("5a9fd4");
    protected static readonly Color MonsterColour = new("c4614f");
    protected static readonly Color DeadColour = new("4a4a52");
    protected static readonly Color DownColour = new("8a6a4a");
    protected static readonly Color ActiveRing = new("e8d5a0");
    protected static readonly Color Ink = new("d8d8e0");
    protected static readonly Color Dim = new("8a8a96");

    /// <summary>
    /// How many frames a second every sprite animation advances at.
    /// </summary>
    /// <remarks>
    /// <b>One clock for the whole board</b> — idle, walk, swing, flinch and fall alike.
    /// Each used to keep its own: idle ticked at eight a second, a walk cycle at twenty,
    /// and a pose was fitted to a fixed duration whatever its length, so the Priest's
    /// fourteen-frame attack flickered past at thirty frames a second while the Goblin's
    /// five-frame one ambled at eleven. Pinning them all to one rate is what gives the
    /// board a single deliberate cadence, and it makes the tempo one number to change:
    /// everything below is derived from it, including how long a pose lasts and how fast
    /// the ground goes by.
    /// </remarks>
    protected const double AnimationFramesPerSecond = 10;

    /// <summary>
    /// Seconds a walking token spends crossing each square of its recorded path.
    /// </summary>
    /// <remarks>
    /// <b>Derived rather than chosen</b>, because a walk's speed and its stride are the
    /// same fact: a square costs the paces that cover it, and those paces take as long as
    /// the shared rate says. Picking the two apart is what makes legs skate. The frame
    /// count is the typical walk cycle across these packs rather than any one strip's,
    /// so every creature crosses the ground at the same speed however its art was drawn —
    /// a tactical board where a move's length depended on the sprite would be a board
    /// that lied about distance.
    /// </remarks>
    protected const double SecondsPerWalkSquare =
        TypicalWalkCycleFrames * WalkCyclesPerSquare / AnimationFramesPerSecond;

    /// <summary>Frames in a walk cycle in these packs — most are eight.</summary>
    private const double TypicalWalkCycleFrames = 8;

    /// <summary>
    /// One thing the board plays out before the next beat: a walk along a recorded
    /// route, or a pose struck at somebody. Queued in log order, so an Opportunity Attack
    /// plays where it interrupted the walk that provoked it.
    /// </summary>
    /// <param name="Step">
    /// The log entry this act is the picture of. It is what holds the narration back
    /// until the animation has played — see <see cref="RevealedLogCount"/>.
    /// </param>
    /// <param name="RevealThrough">
    /// How much of the log this act has earned the right to print when it finishes: its
    /// own line and everything that followed as a consequence of it, so a swing's last
    /// frame prints the roll, the damage and the death together.
    /// </param>
    private abstract record Act(int Step, int RevealThrough);

    private sealed record WalkAct(int Step, int RevealThrough, string WalkerId, IReadOnlyList<GridPosition> Path)
        : Act(Step, RevealThrough);

    /// <summary>Something crossing the board from a shooter to whatever it was aimed at.</summary>
    /// <param name="Spell">A bolt rather than an arrow — spell attacks throw light, not wood.</param>
    private sealed record ShotAct(
        int Step,
        int RevealThrough,
        GridPosition From,
        GridPosition To,
        bool Spell) : Act(Step, RevealThrough);

    /// <summary>A one-shot pose: a strip played once through, then done.</summary>
    /// <param name="FacesLeft">
    /// Which way it faces, read from where the two stood when it was queued. Null when
    /// they shared a column, or when facing is not the pose's business — the actor then
    /// keeps its side's default facing.
    /// </param>
    /// <param name="Frames">
    /// How many frames this pose plays — the strip's, except a fall, which stops where
    /// the body settles. It is what decides how long the pose lasts, at the board's
    /// shared rate.
    /// </param>
    private sealed record PoseAct(
        int Step,
        int RevealThrough,
        string ActorId,
        Pose Pose,
        int Frames,
        bool? FacesLeft = null) : Act(Step, RevealThrough);

    /// <summary>The one-shot animations, each with its own strip and its own tempo.</summary>
    protected enum Pose
    {
        None,

        /// <summary>The Attack strip, played at whatever it swings.</summary>
        Swing,

        /// <summary>
        /// The Cast strip, played on the engine's SpellCast announcement — the tome
        /// comes up before the bolt flies or anybody saves. Art most combatants lack;
        /// without it the step simply plays no pose, as every cast did before.
        /// </summary>
        Cast,

        /// <summary>The Hurt strip: a flinch as damage lands.</summary>
        Flinch,

        /// <summary>The Dead strip, played through as the body goes down.</summary>
        Fall,
    }

    private readonly Queue<Act> _pendingActs = new();

    /// <summary>
    /// Tokens shown as they were <em>before</em> a blow whose picture has not played yet,
    /// keyed by combatant id.
    /// </summary>
    /// <remarks>
    /// The engine resolves an action whole the instant it is asked, so by the time the
    /// monster's walk starts playing, its victim's live state already says 0 hit points
    /// and down — and a board drawn from live state showed the player falling over
    /// <em>before the monster had taken a step</em>. This is <see cref="WithWalk"/>'s
    /// idea applied to consequences: the walk defers the mover's position until the hop
    /// plays, and this defers the victim's hit points, posture and conditions until the
    /// flinch or fall that depicts them begins. Position deliberately stays live — a
    /// held token is a record of how someone <em>looked</em>, never of where they stand.
    /// </remarks>
    private readonly Dictionary<string, Token> _heldAppearances = [];

    /// <summary>The token list most recently drawn — the pre-action state a hold captures.</summary>
    private IReadOnlyList<Token>? _lastShownTokens;

    /// <summary>The act being played out, which is what the log is waiting on.</summary>
    private Act? _playing;

    private string? _walkerId;
    private IReadOnlyList<GridPosition>? _walkPath;
    private double _walkElapsed;
    private int _walkSquare;

    /// <summary>The shot in flight: where from, where to, how far along, and which art.</summary>
    private GridPosition? _shotFrom;
    private GridPosition _shotTo;
    private bool _shotIsSpell;
    private double _shotElapsed;
    private double _shotSeconds;

    /// <summary>
    /// How long a shot takes to cross one square, in seconds.
    /// </summary>
    /// <remarks>
    /// Quicker than a walk on purpose — an arrow ambling at walking pace reads as a
    /// lobbed pebble — but derived from the same board clock rather than picked, so one
    /// knob still governs the whole tempo. Floored at two frames' worth, because a shot
    /// at an adjacent enemy would otherwise be over before a frame of it drew.
    /// </remarks>
    private const double SecondsPerShotSquare = 1 / AnimationFramesPerSecond;

    /// <summary>The least time a shot may take, however short the distance.</summary>
    private const double MinimumShotSeconds = 2 / AnimationFramesPerSecond;

    /// <summary>
    /// Which way the walking token faces, from the last horizontal step it took. Null
    /// until the route turns horizontal — a purely vertical walk keeps the side's
    /// default facing.
    /// </summary>
    private bool? _walkerFacesLeft;

    private string? _poseActorId;
    private Pose _pose;
    private bool? _poseFacesLeft;
    private double _poseElapsed;
    private int _poseFrames;

    /// <summary>
    /// How much of the log the screen may show, so that the narration lands with the
    /// picture of it rather than ahead of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An attack resolves in the engine the instant it is asked for: the roll, the
    /// damage and the death are all in the log before a single frame of the swing has
    /// been drawn. Printing them straight away tells the reader the outcome while the
    /// weapon is still going up, which makes the animation decoration rather than the
    /// event. So each queued act remembers the log entry it is the picture of, and the
    /// log is held at that line until the act finishes — an attack's rolled result and
    /// the damage it dealt appear on the swing's last frame.
    /// </para>
    /// <para>
    /// It delays lines; it never reorders or drops them. Anything with no animation to
    /// wait for — a creature with no art, a Dodge, the whole log during a probe, where
    /// nothing is queued at all — appears at once, exactly as before.
    /// </para>
    /// </remarks>
    protected int RevealedLogCount { get; private set; } = ShowEveryLine;

    /// <summary>Nothing is being held back.</summary>
    private const int ShowEveryLine = int.MaxValue;

    /// <summary>The log length the queue is working towards: what shows once it drains.</summary>
    private int _revealTarget = ShowEveryLine;

    /// <summary>
    /// How long a pose lasts: as long as its own frames take at the shared rate. The
    /// time follows the strip rather than the strip being squeezed into a time, which is
    /// what keeps a long animation from flickering and a short one from crawling.
    /// </summary>
    private static double SecondsFor(int frames) => frames / AnimationFramesPerSecond;

    /// <summary>
    /// The least time a Swing holds, however few frames its strip has.
    /// </summary>
    /// <remarks>
    /// The drawn sets are one frame per pose, and one frame at the shared rate is a
    /// tenth of a second — the Fighter's attack was reported from play as never seen
    /// at all. A multi-frame pack is untouched (the shortest swing in the installed
    /// packs already runs half a second); a single drawing simply holds its pose long
    /// enough to be read. Swings only: a one-frame flinch reads as the jolt it is, and
    /// a fall settles into the body lying there, so neither is ever missed.
    /// </remarks>
    private const double MinimumSwingSeconds = 0.75;

    /// <summary>
    /// A pose's duration: its frames at the shared rate, floored for a Swing — and for
    /// a Cast, which is one drawn frame with a whole announcement to be the picture of.
    /// </summary>
    private static double SecondsForPose(Pose pose, int frames) =>
        pose is Pose.Swing or Pose.Cast
            ? Math.Max(SecondsFor(frames), MinimumSwingSeconds)
            : SecondsFor(frames);

    /// <summary>
    /// How much of a Walk strip one square costs. Half a cycle: a walk cycle is two
    /// paces, and a pace covers about half a five-foot square.
    /// </summary>
    /// <remarks>
    /// The cycle advances with the *distance covered*, not with the clock, which is what
    /// stops the legs skating — tie them to a timer and any change to how fast a token
    /// crosses the ground leaves them running on the spot or gliding with their feet
    /// still. This way the two can never disagree.
    /// </remarks>
    private const float WalkCyclesPerSquare = 0.5f;

    private SpriteLibrary _sprites = null!;
    private double _animationClock;
    private int _animationFrame;

    /// <summary>
    /// Off during a probe or a capture, exactly like the walk hop: a verification image
    /// read the instant after a click must not depend on when the frame was taken.
    /// </summary>
    private bool _animateSprites;

    protected Font TextFont { get; private set; } = null!;
    protected int GridWidth { get; private set; }
    protected int GridHeight { get; private set; }
    protected IReadOnlyCollection<GridPosition> BlockedSquares { get; private set; } = [];
    /// <summary>
    /// This battlefield's look, or null when no terrain art is installed.
    /// </summary>
    /// <remarks>
    /// <b>One theme for the whole fight, and it is the battlefield that picks it.</b>
    /// Derived from the field's own shape and its obstacles rather than from a counter,
    /// so the same fight always looks the same — a reload shows the ground it showed
    /// before — and consecutive fights differ because their fields do. Choosing it from
    /// the fight's dice would have been the obvious alternative and is worse: the client
    /// would have to be handed a number it has no other use for.
    /// </remarks>
    protected SpriteLibrary.GroundTheme? Theme { get; private set; }

    protected IReadOnlyCollection<GridPosition> DifficultSquares { get; private set; } = [];
    protected IReadOnlyCollection<GridPosition> LowObstacleSquares { get; private set; } = [];

    /// <summary>The heading the screen draws — what kind of screen this is.</summary>
    protected abstract string Title { get; }

    /// <summary>One combatant, as the screen draws it.</summary>
    /// <remarks>
    /// A copy rather than a reference on purpose: <see cref="Combatant"/> is mutable and
    /// the fight moves on, so a held reference would make every snapshot show the last
    /// frame. The play screen rebuilds tokens each draw for the same price.
    /// </remarks>
    protected sealed record Token(
        string Id,
        string Name,
        char Label,
        bool IsParty,
        int X,
        int Y,
        int HitPoints,
        int MaximumHitPoints,
        bool IsDead,
        bool IsDown,
        string Conditions,
        string? ClassName);

    public override void _Ready()
    {
        TextFont = ThemeDB.GetFallbackFont();

        // Pixel art scaled with linear filtering smears; nearest keeps the pixels.
        TextureFilter = TextureFilterEnum.Nearest;

        _sprites = SpriteLibrary.Load();
        _animateSprites = !HasArgument("probe") && ArgumentValue("capture") is null;

        // The chrome anchors to the window's real edges, so a resize moves everything
        // derived from them; subclasses re-seat whatever they cache (the play screen's
        // button rects). The floor keeps a shrunken window from folding the stage into
        // the chrome entirely.
        GetViewport().SizeChanged += OnResized;
        GetWindow().MinSize = new Vector2I(960, 540);

        OnReady();
    }

    /// <summary>The window changed size; everything anchored to its edges moved.</summary>
    protected virtual void OnResized() => QueueRedraw();

    /// <summary>
    /// Advances the idle and walk loops; true when a new frame means a redraw. Called
    /// from each screen's <c>_Process</c> — the base class deliberately has none.
    /// </summary>
    protected bool AdvanceSpriteAnimation(double delta)
    {
        if (!_animateSprites || _sprites.IsEmpty)
        {
            return false;
        }

        _animationClock += delta;
        var frame = (int)(_animationClock * AnimationFramesPerSecond);

        if (frame == _animationFrame)
        {
            return false;
        }

        _animationFrame = frame;
        return true;
    }

    /// <summary>The subclass's setup, run once the shared pieces exist.</summary>
    protected abstract void OnReady();

    /// <summary>The extracted content, found the way the console client finds it.</summary>
    protected static SrdContent LoadContent() => ContentLoader.Load(ContentDirectory());

    /// <summary>Builds the same fight the console client would, from the same content.</summary>
    protected static Fight ResolveFight(int seed)
    {
        var content = LoadContent();
        var party = PregeneratedParty.Build(content, level: 3);
        var random = new SeededRandomSource(seed);

        return EncounterFactory.Build(content, party, EncounterDifficulty.Moderate, random);
    }

    /// <summary>
    /// The seed to fight on. <c>--seed=&lt;n&gt;</c> wins; a capture or probe run falls
    /// back to a fixed seed, because a verification image must not change between runs;
    /// otherwise the seed is fresh, exactly as the console rolls one — and it is in the
    /// heading, so "it happened on seed 12345" stays a complete bug report.
    /// </summary>
    protected static int SeedArgument()
    {
        if (ArgumentValue("seed") is { } text && int.TryParse(text, out var parsed))
        {
            return parsed;
        }

        return HasArgument("probe") || ArgumentValue("capture") is not null
            ? 20250812
            : Random.Shared.Next();
    }

    /// <summary>"2 Animated Armors, Awakened Tree" — the fight's cast, for the heading.</summary>
    protected static string RosterOf(Fight fight) =>
        string.Join(", ", fight.Built.Monsters
            .GroupBy(monster => monster.Name)
            .Select(group => group.Count() > 1 ? $"{group.Count()} {group.Key}s" : group.Key));

    /// <summary>Takes the battlefield's shape so the grid can be drawn.</summary>
    /// <summary>
    /// The colours the log is read with, rebuilt per fight because the names are the
    /// fight's own. <see cref="LogHighlighter.None"/> until there is a fight.
    /// </summary>
    protected LogHighlighter Highlighter { get; private set; } = LogHighlighter.None;

    protected void AdoptBattlefield(Encounter encounter)
    {
        Highlighter = LogHighlighter.For(encounter, PregeneratedParty.SideId);

        GridWidth = encounter.Battlefield.Width;
        GridHeight = encounter.Battlefield.Height;
        BlockedSquares = encounter.Battlefield.Blocked;
        DifficultSquares = encounter.Battlefield.DifficultTerrain;
        LowObstacleSquares = encounter.Battlefield.LowObstacles;

        // The field's own shape picks its look, so a fight always draws the ground it
        // drew before and the next one — a different field — differs.
        Theme = _sprites.Themes.Count == 0
            ? null
            : _sprites.Themes[Math.Abs(
                (GridWidth * 31)
                + (GridHeight * 17)
                + (BlockedSquares.Count * 7)
                + DifficultSquares.Count) % _sprites.Themes.Count];

        // A fresh fight opens on the whole field, and the camera is handed back: the
        // first frames then glide in toward wherever the combatants actually are.
        _cameraCentre = new Vector2(GridWidth / 2f, GridHeight / 2f);
        _cameraAim = _cameraCentre;
        CellPixels = MathF.Min(FitFieldCell(), LargestCell);
        _cameraCellAim = CellPixels;
        _cameraManual = false;
        _cameraDragging = false;
    }

    /// <summary>The square size at which the whole field just fits the stage.</summary>
    private float FitFieldCell() => Math.Clamp(
        MathF.Min(
            (StageRight - StageLeft) / Math.Max(1, GridWidth),
            (StageBottom - StageTop) / Math.Max(1, GridHeight)),
        SmallestCell,
        LargestManualCell);


    /// <summary>
    /// Points the camera at the fight: zoomed to hold every living combatant with some
    /// ground around them, centred on them, leaned toward whoever is acting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Frame everyone, favour the actor.</b> The zoom is chosen first — the tightest
    /// square size that still shows the whole spread of living combatants, capped so a
    /// final duel does not become two portraits — and the centre starts on that spread.
    /// Whatever slack the cap left between the spread and the screen's edges is then
    /// spent leaning toward the active combatant, so the thing happening is nearest the
    /// middle without anyone being pushed off.
    /// </para>
    /// <para>
    /// A probe or capture run frames the whole field instead: a verification image must
    /// show the same board every time, not wherever that seed's fight had drifted to.
    /// The same framing serves an empty board and a fight with nobody left standing.
    /// </para>
    /// </remarks>
    private void AimCamera()
    {
        if (_cameraManual || GridWidth == 0)
        {
            return;
        }

        var living = _lastShownTokens?.Where(token => !token.IsDead).ToList();

        if (!_animateSprites || living is not { Count: > 0 })
        {
            _cameraAim = new Vector2(GridWidth / 2f, GridHeight / 2f);
            _cameraCellAim = MathF.Min(FitFieldCell(), LargestCell);
            return;
        }

        var minX = living.Min(token => token.X) + 0.5f - CameraPaddingSquares;
        var maxX = living.Max(token => token.X) + 0.5f + CameraPaddingSquares;
        var minY = living.Min(token => token.Y) + 0.5f - CameraPaddingSquares;
        var maxY = living.Max(token => token.Y) + 0.5f + CameraPaddingSquares;

        // Framing everyone outranks filling the window: the zoom goes as far out as
        // the fight's spread needs. It shipped with a floor that kept the field
        // filling the window — the reason was that the ground art ended at the field's
        // edge and anything past it was void — and the first split fight showed the
        // cost: pinned at that floor, the camera left combatants cut off at the
        // window's edge and behind the banner. The ground runs to the window's edges
        // now, so zooming out shows the field in its surroundings and costs nothing.
        var cell = Math.Clamp(
            MathF.Min((StageRight - StageLeft) / (maxX - minX), (StageBottom - StageTop) / (maxY - minY)),
            SmallestCell,
            LargestCell);

        var centre = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);

        if (_lastActiveId is { } activeId
            && FindToken(living, activeId) is { } actor)
        {
            var slackX = MathF.Max(0, ((StageRight - StageLeft) / cell) - (maxX - minX)) / 2f;
            var slackY = MathF.Max(0, ((StageBottom - StageTop) / cell) - (maxY - minY)) / 2f;

            centre.X += Math.Clamp(actor.X + 0.5f - centre.X, -slackX, slackX);
            centre.Y += Math.Clamp(actor.Y + 0.5f - centre.Y, -slackY, slackY);
        }

        _cameraAim = ClampToField(centre, cell);
        _cameraCellAim = cell;
    }

    /// <summary>
    /// Keeps the view near the field: the stage may overscan each field edge by
    /// <see cref="CameraOverscanSquares"/>, and a zoom the whole field cannot even
    /// reach at that allowance is centred instead.
    /// </summary>
    /// <remarks>
    /// The first version held the whole window on the field, and it was measurably
    /// wrong the first time a fight reached an edge: at the zoom floor that clamp pins
    /// the centre outright, so the camera stopped following and the edge rows played
    /// out underneath the banner strip — reported from play on 2026-08-18. The
    /// overscan is against the <i>stage</i> rather than the window because the stage
    /// already encodes every obstruction: a row on the field's bottom edge stops a
    /// full square clear of the buttons, and the last column stops clear of the log.
    /// What shows beyond the edge is the terrain simply continuing —
    /// <see cref="DrawGrid"/> lays ground to the window's edges — so following the
    /// fight costs nothing at all.
    /// </remarks>
    private Vector2 ClampToField(Vector2 centre, float cell) => new(
        ClampAxis(centre.X, GridWidth, (StageLeft + StageRight) / 2f, StageLeft, StageRight, cell),
        ClampAxis(centre.Y, GridHeight, (StageTop + StageBottom) / 2f, StageTop, StageBottom, cell));

    /// <summary>How far past a field edge the stage may scroll, in squares.</summary>
    private const float CameraOverscanSquares = 1f;

    private static float ClampAxis(
        float centre,
        int squares,
        float stageCentre,
        float stageLow,
        float stageHigh,
        float cell)
    {
        // The camera's centre maps to the stage's centre, so each bound solves "which
        // centres keep this stage edge within a square of the field edge".
        var low = ((stageCentre - stageLow) / cell) - CameraOverscanSquares;
        var high = squares + CameraOverscanSquares - ((stageHigh - stageCentre) / cell);

        return low <= high
            ? Math.Clamp(centre, low, high)
            : squares / 2f;
    }

    /// <summary>
    /// Glides the camera toward its aim; true when it moved and the screen should
    /// redraw. A probe or capture snaps instead, exactly like the walk hop: an image
    /// read the instant after a click must not depend on when the frame was taken.
    /// </summary>
    protected bool AdvanceCamera(double delta)
    {
        // The player's hold on the camera ends when the turn moves on.
        if (_cameraManual && _lastActiveId != _manualForActiveId)
        {
            _cameraManual = false;
        }

        AimCamera();

        var centreGap = _cameraAim - _cameraCentre;
        var cellGap = _cameraCellAim - CellPixels;

        // Within a hundredth of a pixel of the aim: settled, and holding still.
        if (centreGap.Length() * CellPixels < 0.01f && Math.Abs(cellGap) < 0.01f)
        {
            return false;
        }

        if (!_animateSprites)
        {
            _cameraCentre = _cameraAim;
            CellPixels = _cameraCellAim;
            return true;
        }

        var chase = (float)(1 - Math.Exp(-delta * CameraChaseRate));

        _cameraCentre += centreGap * chase;
        CellPixels += cellGap * chase;
        return true;
    }

    /// <summary>
    /// The player's hand on the camera: the wheel zooms about the pointer, a
    /// middle-button drag pans. True when the event was the camera's, so the caller
    /// stops routing it anywhere else; the hold lasts until the fight moves on, when
    /// <see cref="AdvanceCamera"/> hands the camera back to the automatic framing.
    /// </summary>
    protected bool HandleCameraInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheelIn:
                ZoomAt(wheelIn.Position, WheelZoomFactor);
                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheelOut:
                ZoomAt(wheelOut.Position, 1f / WheelZoomFactor);
                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _cameraDragging = middle.Pressed;
                _dragLast = middle.Position;

                if (middle.Pressed)
                {
                    TakeCamera();
                }

                return true;

            case InputEventMouseMotion motion when _cameraDragging:
                _cameraCentre = ClampToField(_cameraCentre - ((motion.Position - _dragLast) / CellPixels), CellPixels);
                _cameraAim = _cameraCentre;
                _dragLast = motion.Position;
                QueueRedraw();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Zooms in place: the square under the pointer stays under the pointer, which is
    /// what makes the wheel feel like leaning in rather than being teleported.
    /// </summary>
    private void ZoomAt(Vector2 pointer, float factor)
    {
        if (GridWidth == 0)
        {
            return;
        }

        TakeCamera();

        var cell = Math.Clamp(CellPixels * factor, SmallestCell, LargestManualCell);

        // The point of the field under the pointer, before and after: solving for the
        // centre that keeps them the same square.
        var world = new Vector2((pointer.X - GridLeft) / CellPixels, (pointer.Y - GridTop) / CellPixels);

        CellPixels = cell;
        _cameraCellAim = cell;
        _cameraCentre = ClampToField(
            world + ((new Vector2((StageLeft + StageRight) / 2f, (StageTop + StageBottom) / 2f) - pointer) / cell),
            cell);
        _cameraAim = _cameraCentre;
        QueueRedraw();
    }

    /// <summary>Marks the camera as the player's until this turn moves on.</summary>
    private void TakeCamera()
    {
        _cameraManual = true;
        _manualForActiveId = _lastActiveId;
    }

    protected static Token TokenFrom(Combatant combatant, Labels labels) => new(
        combatant.Id,
        combatant.Name,
        labels.Of(combatant),
        combatant.SideId == PregeneratedParty.SideId,
        combatant.Position.X,
        combatant.Position.Y,
        combatant.CurrentHitPoints,
        combatant.Stats.MaximumHitPoints,
        combatant.IsDead,
        !combatant.IsDead && combatant.CurrentHitPoints == 0,
        string.Join(", ", combatant.Conditions),
        combatant.Stats.Character?.ClassName);

    /// <summary>Initiative order, not build order: it is what a watcher actually tracks.</summary>
    protected static List<Token> TokensFrom(Encounter encounter, Labels labels) =>
        [.. encounter.TurnOrder.Select(combatant => TokenFrom(combatant, labels))];

    /// <summary>Whether a walk or a pose is playing or waiting to play.</summary>
    protected bool ActInProgress =>
        _walkPath is not null || _poseActorId is not null || _shotFrom is not null || _pendingActs.Count > 0;

    /// <summary>
    /// Queues what a slice of the log should play out on the board, in log order — so a
    /// blow lands after the walk that closed the distance, and an Opportunity Attack
    /// plays where it interrupted the walk that provoked it.
    /// </summary>
    /// <remarks>
    /// Four kinds of step have a body to them. A Move carries the engine's own record of
    /// the route, replayed and never recomputed. An attack swings, faced at its target.
    /// Damage makes its victim flinch. Dropping — dead, or merely down — plays the body
    /// going down instead of cutting straight to a corpse. Each is queued only when the
    /// actor's art actually holds that strip, because holding the beat for a token with
    /// nothing to show is dead time; and a flinch is skipped for a victim the same blow
    /// felled, whose fall says it better.
    /// </remarks>
    protected void QueueActs(IReadOnlyList<CombatStep> log, int from, int to, IReadOnlyList<Token> tokens)
    {
        var idle = !ActInProgress;
        var first = -1;

        for (var index = Math.Max(0, from); index < to && index < log.Count; index++)
        {
            var before = _pendingActs.Count;

            switch (log[index])
            {
                // One square is no journey: a walk cut short before it left its first
                // square would animate nothing, so it is not queued at all.
                case { Kind: CombatStepKind.Move, ActorId: { } walkerId, Path.Count: > 1 } step:
                    _pendingActs.Enqueue(new WalkAct(index, Consequences(log, index, to), walkerId, step.Path));
                    break;

                case
                {
                    Kind: CombatStepKind.Attack or CombatStepKind.OpportunityAttack,
                    ActorId: { } attackerId,
                    TargetId: { } strickenId,
                } attackStep:
                {
                    // The shot follows the swing and precedes the damage, which is the
                    // order the fight happens in: the bow comes up, the arrow crosses,
                    // and only then does anybody flinch. Whether it *was* a shot is the
                    // engine's answer, carried on the step beside the route a Move
                    // carries — nothing here infers it from the gap.
                    var shot = attackStep.Ranged is not RangedAttackKind.None
                        && ShotArt(attackStep.Ranged) is not null
                        && FindToken(tokens, attackerId) is not null
                        && FindToken(tokens, strickenId) is not null;

                    // With something in flight, the swing earns only its own line: the
                    // roll is settled when the bow twangs, but the damage is the picture
                    // of the arrow *landing*, so it waits for the shot. Without a shot
                    // the swing keeps its consequences, exactly as before.
                    QueuePose(
                        log,
                        index,
                        to,
                        tokens,
                        attackerId,
                        Pose.Swing,
                        facing: strickenId,
                        revealThrough: shot ? index + 1 : null);

                    if (shot
                        && FindToken(tokens, attackerId) is { } shooter
                        && FindToken(tokens, strickenId) is { } struck)
                    {
                        _pendingActs.Enqueue(new ShotAct(
                            index,
                            Consequences(log, index, to),
                            new GridPosition(shooter.X, shooter.Y),
                            new GridPosition(struck.X, struck.Y),
                            attackStep.Ranged is RangedAttackKind.Spell));
                    }

                    break;
                }

                case { Kind: CombatStepKind.SpellCast, ActorId: { } casterId }:
                    QueuePose(log, index, to, tokens, casterId, Pose.Cast);
                    break;

                case { Kind: CombatStepKind.Damage, TargetId: { } victimId }:
                    HoldAppearance(victimId);
                    QueuePose(log, index, to, tokens, victimId, Pose.Flinch);
                    break;

                case { Kind: CombatStepKind.Died or CombatStepKind.Downed, ActorId: { } fallenId }:
                    HoldAppearance(fallenId);
                    QueuePose(log, index, to, tokens, fallenId, Pose.Fall);
                    break;
            }

            if (first < 0 && _pendingActs.Count > before)
            {
                first = index;
            }
        }

        // Where the log gets to once everything queued has played. Always the newest
        // slice's end, so a click that lands mid-animation still has its lines waiting.
        _revealTarget = to;

        // Hold the narration at the first act's own line — that line is the outcome of
        // the animation about to play. Only when starting fresh: mid-chain, the acts
        // already queued are carrying the log forward and must not be wound back.
        if (idle && first >= 0)
        {
            RevealedLogCount = first;
        }
        else if (!ActInProgress)
        {
            // A slice with nothing to animate waits for nothing — and holds nothing:
            // with no act coming to release them, held appearances would freeze the
            // board on a moment already past.
            RevealedLogCount = to;
            _heldAppearances.Clear();
        }

        // Start immediately so the very next frame draws the walker on its starting
        // square — waiting for the first advance would flash it at the destination.
        if (_walkPath is null && _poseActorId is null)
        {
            StartNextAct();
        }
    }

    /// <summary>
    /// How far the log may be printed once the act at <paramref name="step"/> finishes:
    /// its own line, and every line after it that is the *outcome* of it rather than a
    /// new thing happening.
    /// </summary>
    /// <remarks>
    /// This is what puts the damage on the swing's last frame. An attack resolves into
    /// several lines — the roll, the damage, sometimes a death — and they are all one
    /// moment of the fight, so they print together when the blow lands. Anything else
    /// (the next creature's turn, a walk) is a new moment and waits for its own act.
    /// </remarks>
    private static int Consequences(IReadOnlyList<CombatStep> log, int step, int to)
    {
        var through = step + 1;

        while (through < to && through < log.Count && log[through].Kind is
                   CombatStepKind.Damage
                   or CombatStepKind.Died
                   or CombatStepKind.Downed
                   or CombatStepKind.Condition)
        {
            through++;
        }

        return through;
    }

    /// <summary>
    /// Queues one pose for one combatant, when that combatant's art can show it.
    /// </summary>
    private void QueuePose(
        IReadOnlyList<CombatStep> log,
        int step,
        int to,
        IReadOnlyList<Token> tokens,
        string actorId,
        Pose pose,
        string? facing = null,
        int? revealThrough = null)
    {
        if (FindToken(tokens, actorId) is not { } actor
            || _sprites.ForToken(actor.IsParty, actor.ClassName, actor.Name) is not { } art)
        {
            return;
        }

        var strip = pose switch
        {
            Pose.Swing => art.Attack,
            Pose.Cast => art.Cast,

            // A blow that felled its victim skips the flinch: the fall queued right
            // behind it is the better telling, and the token is already on the floor.
            Pose.Flinch => actor.IsDead || actor.IsDown ? null : art.Hurt,
            Pose.Fall => art.Dead,
            _ => null,
        };

        if (strip is null)
        {
            return;
        }

        var facesLeft = facing is not null && FindToken(tokens, facing) is { } other && other.X != actor.X
            ? other.X < actor.X
            : (bool?)null;

        // A fall plays only as far as the body settles; everything else plays whole.
        var frames = pose is Pose.Fall ? art.Repose + 1 : strip.FrameCount;

        _pendingActs.Enqueue(
            new PoseAct(
                step,
                revealThrough ?? Consequences(log, step, to),
                actorId,
                pose,
                frames,
                facesLeft));
    }

    /// <summary>
    /// Which way a token that is neither walking nor swinging should face: toward the
    /// nearest living enemy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be the token's *side* — monsters faced left, the party faced right —
    /// which is right only because the sides spawn in columns and stops being right the
    /// moment anybody walks past anybody. A creature standing east of the character it
    /// is about to bite was drawn looking away from them.
    /// </para>
    /// <para>
    /// The swing already faces its victim and a walk faces its last step, so this is the
    /// third case: standing still. Ties and a shared column keep the side's old default,
    /// because a figure drawn exactly edge-on has no better answer and flipping on a
    /// tie would make tokens twitch as others moved around them. The dead are not
    /// looked at — a corpse is not something to square up to — but the *downed* are,
    /// since they are still in the fight.
    /// </para>
    /// </remarks>
    private static bool RestingFacesLeft(IReadOnlyList<Token> tokens, Token token)
    {
        var nearest = int.MaxValue;
        var facesLeft = !token.IsParty;

        foreach (var other in tokens)
        {
            if (other.IsParty == token.IsParty || other.IsDead || other.X == token.X)
            {
                continue;
            }

            var distance = Math.Abs(other.X - token.X) + Math.Abs(other.Y - token.Y);

            if (distance < nearest)
            {
                nearest = distance;
                facesLeft = other.X < token.X;
            }
        }

        return facesLeft;
    }

    private static Token? FindToken(IReadOnlyList<Token> tokens, string id)
    {
        foreach (var token in tokens)
        {
            if (token.Id == id)
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// Remembers how a combatant looked before the blow whose picture is still queued.
    /// First capture wins: the earliest pre-state is the one every later act in the
    /// chain is deferring.
    /// </summary>
    private void HoldAppearance(string combatantId)
    {
        if (_heldAppearances.ContainsKey(combatantId) || _lastShownTokens is null)
        {
            return;
        }

        if (FindToken(_lastShownTokens, combatantId) is { } shown)
        {
            _heldAppearances[combatantId] = shown;
        }
    }

    /// <summary>
    /// The tokens with every not-yet-depicted consequence rolled back to how it looked
    /// when last shown: hit points, posture and conditions. Position stays live — where
    /// someone stands is <see cref="WithWalk"/>'s question, not this one's.
    /// </summary>
    protected IReadOnlyList<Token> WithHeldAppearances(IReadOnlyList<Token> tokens)
    {
        if (_heldAppearances.Count == 0)
        {
            return tokens;
        }

        return [.. tokens.Select(token =>
            _heldAppearances.TryGetValue(token.Id, out var held)
                ? token with
                {
                    HitPoints = held.HitPoints,
                    IsDead = held.IsDead,
                    IsDown = held.IsDown,
                    Conditions = held.Conditions,
                }
                : token)];
    }

    /// <summary>Forgets every queued act — for scrubbing, where snapping is the point.</summary>
    protected void ClearActs()
    {
        _pendingActs.Clear();
        _heldAppearances.Clear();
        _shotFrom = null;
        _walkPath = null;
        _walkerId = null;
        _poseActorId = null;
        _pose = Pose.None;
        _playing = null;

        // Nothing is playing, so nothing is owed a picture: the log is whole again.
        RevealedLogCount = ShowEveryLine;
        _revealTarget = ShowEveryLine;
    }

    /// <summary>
    /// Lets the log catch up with the act that has just finished — its own line and its
    /// consequences — or all the way, when it was the last thing owed a picture.
    /// </summary>
    private void ReleaseLogAfterAct(Act finished) =>
        RevealedLogCount = _pendingActs.Count > 0
            ? Math.Max(RevealedLogCount, finished.RevealThrough)
            : _revealTarget;

    /// <summary>Ends the playing act: the log catches up, and the next one begins.</summary>
    private void Finish()
    {
        if (_playing is { } finished)
        {
            ReleaseLogAfterAct(finished);
        }

        _playing = null;
        StartNextAct();
    }

    /// <summary>Advances the playing act; true when the screen should redraw.</summary>
    protected bool AdvanceActs(double delta)
    {
        if (_shotFrom is not null)
        {
            _shotElapsed += delta;

            if (_shotElapsed >= _shotSeconds)
            {
                _shotFrom = null;
                Finish();
            }

            // Every frame: the projectile's whole point is that it is between squares.
            return true;
        }

        if (_poseActorId is not null)
        {
            _poseElapsed += delta;

            if (_poseElapsed >= SecondsForPose(_pose, _poseFrames))
            {
                _poseActorId = null;
                _pose = Pose.None;
                Finish();
            }

            return true;
        }

        if (_walkPath is null)
        {
            return StartNextAct();
        }

        _walkElapsed += delta;
        var square = (int)(_walkElapsed / SecondsPerWalkSquare);

        if (square >= _walkPath.Count)
        {
            _walkPath = null;
            _walkerId = null;
            Finish();
            return true;
        }

        if (square != _walkSquare)
        {
            // The sprite faces the way it is going; a purely vertical step keeps the
            // last facing rather than snapping back to the side's default mid-walk.
            var dx = _walkPath[square].X - _walkPath[_walkSquare].X;

            if (dx != 0)
            {
                _walkerFacesLeft = dx < 0;
            }

            _walkSquare = square;
        }

        // A walk redraws every frame, not only on square boundaries: the token's pixel
        // position glides between squares, so every tick moves it.
        return true;
    }

    /// <summary>The tokens with the walking one drawn where its hop has reached.</summary>
    protected IReadOnlyList<Token> WithWalk(IReadOnlyList<Token> tokens)
    {
        if (_walkPath is not { } path || _walkerId is not { } walkerId)
        {
            return tokens;
        }

        var square = path[Math.Min(_walkSquare, path.Count - 1)];

        return [.. tokens.Select(token =>
            token.Id == walkerId ? token with { X = square.X, Y = square.Y } : token)];
    }

    private bool StartNextAct()
    {
        if (_pendingActs.Count == 0)
        {
            // Everything queued has played, so nothing held is owed a picture any more.
            // This also covers a victim whose flinch or fall was never queued because
            // its art lacks the strip: the consequence appears when the chain ends,
            // which is still after the blow rather than before the walk.
            _heldAppearances.Clear();
            return false;
        }

        var starting = _pendingActs.Dequeue();

        _playing = starting;

        // Something is happening: the fight takes the camera back to show it.
        _cameraManual = false;

        // Everything before this act's own line has already happened on screen, so it
        // can be read; the act's own line is the one being held for its last frame.
        RevealedLogCount = Math.Max(RevealedLogCount, starting.Step);

        switch (starting)
        {
            case ShotAct shot:
                _shotFrom = shot.From;
                _shotTo = shot.To;
                _shotIsSpell = shot.Spell;
                _shotElapsed = 0;
                _shotSeconds = Math.Max(
                    MinimumShotSeconds,
                    shot.From.DistanceFeetTo(shot.To) / (double)Battlefield.FeetPerSquare
                        * SecondsPerShotSquare);
                break;

            case PoseAct pose:
                // The flinch and the fall are the pictures the hold was waiting for:
                // releasing it now is what makes the hit points drop as the flinch
                // plays, and what lets the fall pose see a fallen combatant — the pose
                // is cancelled for anyone still standing.
                if (pose.Pose is Pose.Flinch or Pose.Fall)
                {
                    _heldAppearances.Remove(pose.ActorId);
                }

                _poseActorId = pose.ActorId;
                _pose = pose.Pose;
                _poseFacesLeft = pose.FacesLeft;
                _poseFrames = pose.Frames;
                _poseElapsed = 0;
                break;

            case WalkAct walk:
                _walkerId = walk.WalkerId;
                _walkPath = walk.Path;
                _walkElapsed = 0;
                _walkSquare = 0;
                _walkerFacesLeft = null;

                // Face the route's first horizontal leg from the very first frame, so a
                // walker does not set off looking the wrong way and turn mid-stride.
                for (var index = 1; index < walk.Path.Count; index++)
                {
                    var dx = walk.Path[index].X - walk.Path[index - 1].X;

                    if (dx != 0)
                    {
                        _walkerFacesLeft = dx < 0;
                        break;
                    }
                }

                break;
        }

        return true;
    }

    protected Vector2 CentreOf(GridPosition square) => new(
        GridLeft + (square.X * CellPixels) + (CellPixels / 2f),
        GridTop + (square.Y * CellPixels) + (CellPixels / 2f));

    /// <summary>The grid square under a pixel, or null when the pixel is off the grid.</summary>
    protected GridPosition? SquareAt(Vector2 pixel)
    {
        var x = (int)Math.Floor((pixel.X - GridLeft) / CellPixels);
        var y = (int)Math.Floor((pixel.Y - GridTop) / CellPixels);

        return pixel.X >= GridLeft && pixel.Y >= GridTop && x < GridWidth && y < GridHeight
            ? new GridPosition(x, y)
            : null;
    }

    protected void DrawChrome(string subtitle, string statusLine)
    {
        DrawBackdrop();
        DrawHeading(subtitle, statusLine);
    }

    /// <summary>The screen's clear colour. First, under everything.</summary>
    protected void DrawBackdrop() => DrawRect(new Rect2(0, 0, ScreenWidth, ScreenHeight), Background);

    /// <summary>The translucent wash the overlays share, so the field reads underneath.</summary>
    protected static readonly Color Veil = new(Background.R, Background.G, Background.B, 0.85f);

    /// <summary>
    /// The heading, floating over the field's top-left corner. Its backdrop is measured
    /// to its own text rather than a fixed strip, so it covers no more ground than the
    /// words need — the field runs everywhere now, and every covered pixel is paid for.
    /// </summary>
    protected void DrawHeading(string subtitle, string statusLine)
    {
        var width = MathF.Max(
            TextFont.GetStringSize(Title, fontSize: 20).X,
            MathF.Max(
                TextFont.GetStringSize(subtitle, fontSize: 13).X,
                TextFont.GetStringSize(statusLine, fontSize: 12).X)) + 32;

        DrawRect(new Rect2(8, 8, width, 80), Veil);
        DrawString(TextFont, new Vector2(UiLeft, 34), Title, fontSize: 20, modulate: Ink);
        DrawString(TextFont, new Vector2(UiLeft, 58), subtitle, fontSize: 13, modulate: Dim);
        DrawString(TextFont, new Vector2(UiLeft, 78), statusLine, fontSize: 12, modulate: Dim);
    }

    /// <summary>
    /// The ground, and what stands on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No grid lines.</b> They were how a bare board showed its squares, and with real
    /// ground under everything they were a mesh laid over a picture. Squares stay legible
    /// from what is on them — the cursor's ring, the reachable highlight, a token sitting
    /// centred in its cell — rather than from ruling every one of them.
    /// </para>
    /// <para>
    /// <b>The rules stay visible, which is what the colours were for.</b> Difficult ground
    /// says it with the theme's own drawing where one exists — brambles on the woodland,
    /// one clump per square — and keeps the wash over its tile where none does yet: art
    /// may not cost a player the one thing the square was telling them, so the wash is
    /// the floor, never traded away for a theme that lacks the picture. A wall and a low
    /// obstacle say it with a sprite the same way, since a tree
    /// filling a square and a bush sitting in one read as blocked and passable without
    /// anything being written down. With no art loaded every square falls back to the flat
    /// colours and the outline it always had.
    /// </para>
    /// </remarks>
    protected void DrawGrid()
    {
        var theme = Theme;

        // Every square the window can see, not just the field's own: the ground art
        // runs to the window's edges however the camera sits, so the view is always
        // full of battlefield and never of void — asked for from play on 2026-08-18,
        // the same session that moved the camera past the field's edge. The squares
        // beyond the field are scenery only: rule washes and obstacles never appear
        // on them, and a wash marks them as ground the fight cannot use.
        var xFrom = (int)Math.Floor(-GridLeft / CellPixels);
        var xTo = (int)Math.Ceiling((ScreenWidth - GridLeft) / CellPixels);
        var yFrom = (int)Math.Floor(-GridTop / CellPixels);
        var yTo = (int)Math.Ceiling((ScreenHeight - GridTop) / CellPixels);

        for (var x = xFrom; x < xTo; x++)
        {
            for (var y = yFrom; y < yTo; y++)
            {
                var square = new Rect2(GridLeft + (x * CellPixels), GridTop + (y * CellPixels), CellPixels, CellPixels);
                var position = new GridPosition(x, y);
                var inside = x >= 0 && x < GridWidth && y >= 0 && y < GridHeight;

                if (theme is null)
                {
                    // The bare fallback board keeps its old shape: flat colours inside
                    // the field, background beyond it.
                    if (!inside)
                    {
                        continue;
                    }

                    if (BlockedSquares.Contains(position))
                    {
                        DrawRect(square, Blocked);
                    }
                    else if (LowObstacleSquares.Contains(position))
                    {
                        DrawRect(square, LowObstacle);
                    }
                    else if (DifficultSquares.Contains(position))
                    {
                        DrawRect(square, Difficult);
                    }

                    DrawRect(square, GridLine, filled: false, width: 1);
                    continue;
                }

                DrawGroundUnder(theme.Ground, square, position);

                // Beyond the field the ground simply continues — Brandon asked for the
                // dark wash to go (2026-08-21): the boundary reads from the movement
                // highlight and the cursor, and a window full of one unbroken
                // battlefield beats a picture-in-picture. Rule washes and scenery still
                // never draw out here.
                if (!inside)
                {
                    continue;
                }

                // Difficult ground is a rule, not a decoration: it survives the art.
                // A theme with difficult drawings says it with them — brambles on the
                // woodland — and a theme still waiting on art keeps the wash, so the
                // rule never goes invisible for want of a picture.
                if (DifficultSquares.Contains(position))
                {
                    if (theme.Difficult.Count > 0)
                    {
                        DrawDifficultArt(theme.Difficult, square, position);
                    }
                    else
                    {
                        DrawRect(square, DifficultWash);
                    }
                }
            }
        }

        // Scenery last, over all the ground, one drawing per footprint — see
        // DrawScenery for why it is no longer one drawing per square. The bare
        // fallback board painted its flat rectangles in the loop and has no art.
        if (theme is not null)
        {
            DrawScenery(theme);
        }
    }

    /// <summary>
    /// Draws every obstacle's art, one drawing per footprint, southernmost last.
    /// </summary>
    /// <remarks>
    /// <b>An obstacle is a footprint now, and the art covers exactly what blocks.</b>
    /// The generator places walls as 2×4 blocks upright or 4×2 lying across the field,
    /// and low obstacles as 2×2 (Brandon's
    /// stated sizes for his drawings), never touching one another, so each footprint
    /// comes back out of the blocked squares as a connected component. Art is drawn
    /// across the footprint's short axis, aspect kept, feet on the footprint's bottom
    /// edge — repeated along the long axis when a shorter drawing (the 1:1 tree on
    /// either wall shape) must fill it,
    /// left to overdraw upward when a taller one (the 2×4 brush on a 2×2 base)
    /// stands higher than its base, which is what every standing sprite already does.
    /// A component that is not one of those whole rectangles — a hand-authored map's
    /// wall run, an old save — falls back to the per-square drawing this replaced.
    /// </remarks>
    private void DrawScenery(SpriteLibrary.GroundTheme theme)
    {
        var pieces = new List<(Rect2 Bounds, Texture2D Art, bool Fill)>();

        if (theme.Wall.Count > 0)
        {
            CollectScenery(BlockedSquares, theme.Wall, fill: true, pieces);
        }

        if (theme.Low.Count > 0)
        {
            CollectScenery(LowObstacleSquares, theme.Low, fill: false, pieces);
        }

        // Painter's order: what stands further south draws in front. A fill runs along
        // the footprint's long axis — a tall wall stacks copies upward as it always
        // has, a wide one lays them left to right, which is what turns a square tree
        // on a 4×2 footprint into a row of two trees rather than one giant.
        foreach (var (bounds, art, fillAxis) in pieces.OrderBy(piece => piece.Bounds.End.Y))
        {
            if (bounds.Size.X > bounds.Size.Y)
            {
                var width = art.GetWidth() * bounds.Size.Y / art.GetHeight();
                var copies = fillAxis ? Math.Max(1, (int)Math.Round(bounds.Size.X / width)) : 1;

                for (var copy = 0; copy < copies; copy++)
                {
                    DrawTextureRect(
                        art,
                        new Rect2(
                            bounds.Position.X + (copy * width),
                            bounds.End.Y - bounds.Size.Y,
                            width,
                            bounds.Size.Y),
                        tile: false);
                }

                continue;
            }

            var height = art.GetHeight() * bounds.Size.X / art.GetWidth();
            var copies2 = fillAxis ? Math.Max(1, (int)Math.Round(bounds.Size.Y / height)) : 1;

            for (var copy = 0; copy < copies2; copy++)
            {
                DrawTextureRect(
                    art,
                    new Rect2(
                        bounds.Position.X,
                        bounds.End.Y - ((copy + 1) * height),
                        bounds.Size.X,
                        height),
                    tile: false);
            }
        }
    }

    /// <summary>
    /// Splits one kind of obstacle square into footprints and queues their art:
    /// whole-rectangle components as one piece spanning the block, anything else as
    /// the old per-square standing sprites. Which variant a footprint wears is its
    /// anchor square's hash — deterministic, so a fight redraws the same pillars, and
    /// spatial, so twin footprints on one field need not be twins.
    /// </summary>
    private void CollectScenery(
        IReadOnlyCollection<GridPosition> squares,
        IReadOnlyList<Texture2D> variants,
        bool fill,
        List<(Rect2 Bounds, Texture2D Art, bool Fill)> pieces)
    {
        // A footprint prefers art drawn its way round: a 4×2 wall takes a landscape
        // variant, a 2×4 a portrait or square one, and either falls back to the whole
        // list when the theme has none the right way — scaled and repeated rather than
        // rotated, because turning a drawing sideways is not this code's call.
        Texture2D At(int x, int y, bool wide)
        {
            var pool = variants.Where(v => v.GetWidth() > v.GetHeight() == wide).ToArray();

            if (pool.Length == 0)
            {
                pool = [.. variants];
            }

            return pool[Math.Abs(((x * 89) ^ (y * 59)) + (x * y * 17)) % pool.Length];
        }

        var remaining = new HashSet<GridPosition>(squares);

        while (remaining.Count > 0)
        {
            var component = new List<GridPosition>();
            var frontier = new Queue<GridPosition>();
            var seed = remaining.First();
            remaining.Remove(seed);
            frontier.Enqueue(seed);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                component.Add(current);

                foreach (var next in current.Neighbours())
                {
                    if (remaining.Remove(next))
                    {
                        frontier.Enqueue(next);
                    }
                }
            }

            var minX = component.Min(square => square.X);
            var maxX = component.Max(square => square.X);
            var minY = component.Min(square => square.Y);
            var maxY = component.Max(square => square.Y);
            var width = maxX - minX + 1;
            var height = maxY - minY + 1;
            var isWholeRect = component.Count == width * height
                && ((width == 2 && (height == 2 || height == 4))
                    || (width == 4 && height == 2));

            if (isWholeRect)
            {
                pieces.Add((
                    new Rect2(
                        GridLeft + (minX * CellPixels),
                        GridTop + (minY * CellPixels),
                        width * CellPixels,
                        height * CellPixels),
                    At(minX, minY, wide: width > height),
                    fill));
            }
            else
            {
                foreach (var square in component)
                {
                    pieces.Add((
                        new Rect2(
                            GridLeft + (square.X * CellPixels),
                            GridTop + (square.Y * CellPixels),
                            CellPixels,
                            CellPixels),
                        At(square.X, square.Y, wide: false),
                        false));
                }
            }
        }
    }

    /// <summary>
    /// Lays one of the theme's ground tiles on a square, chosen by where the square is.
    /// </summary>
    /// <remarks>
    /// <b>Which tile is a function of the square, not of the draw.</b> The board is
    /// redrawn many times a second, so rolling for it would make the ground crawl; hashing
    /// the coordinates gives the same square the same tile for the whole fight while
    /// scattering the set across the field. The mix is what stops a single tile reading as
    /// a lattice of its own — the finer version of the problem grid lines had.
    /// </remarks>
    /// <summary>
    /// Lays the ground under one movement square.
    /// </summary>
    /// <remarks>
    /// <b>A ground tile is a movement square now, and the art carries what the layout
    /// used to.</b> The 16-pixel pack tiles were drawn three across per square, which
    /// halved their magnification and kept texture seams off the grid's rhythm. Brandon's
    /// hand-made 48-pixel tiles hold a whole square's detail at the same ~1.4x
    /// magnification the 3x3 layout achieved, so the resolution argument is moot; the
    /// rhythm risk — every seam now falls on a square boundary — is answered in the art
    /// instead: every variant edge-matches every variant, and frequency is weighted by
    /// repeating base tiles within the strip, so no seam or repeat marks the squares out.
    /// </remarks>
    private void DrawGroundUnder(SpriteLibrary.Strip ground, Rect2 square, GridPosition at)
    {
        var size = square.Size.X / GroundTilesPerSquare;

        for (var dx = 0; dx < GroundTilesPerSquare; dx++)
        {
            for (var dy = 0; dy < GroundTilesPerSquare; dy++)
            {
                DrawGroundTile(
                    ground,
                    new Rect2(
                        square.Position.X + (dx * size),
                        square.Position.Y + (dy * size),
                        size,
                        size),
                    // Hashed in ground-tile space, not square space, so neighbouring
                    // squares' tiles carry on the same scatter rather than repeating a
                    // block of four.
                    new GridPosition(
                        (at.X * GroundTilesPerSquare) + dx,
                        (at.Y * GroundTilesPerSquare) + dy));
            }
        }
    }

    /// <summary>How many ground tiles span one movement square, each way.</summary>
    private const int GroundTilesPerSquare = 1;

    /// <summary>
    /// Draws one square's Difficult Terrain art: square-wide, aspect kept, feet on the
    /// square's bottom edge, standing taller than its square when the drawing does —
    /// the 48×96 brambles rise a full two squares, the way every standing sprite
    /// already overdraws upward. It first shipped shrunk to fit inside the square and
    /// read as weeds rather than brush (Brandon, 2026-08-20); the square the rule
    /// covers is still exactly the one the art's feet stand on.
    /// </summary>
    private void DrawDifficultArt(IReadOnlyList<Texture2D> variants, Rect2 square, GridPosition at)
    {
        // The ground tiles' spatial hash, differently seeded so the variant scatter
        // does not correlate with the tile scatter underneath it.
        var art = variants[Math.Abs(((at.X * 97) ^ (at.Y * 41)) + (at.X * at.Y * 13)) % variants.Count];
        var size = art.GetSize();
        var height = size.Y * square.Size.X / size.X;

        DrawTextureRect(
            art,
            new Rect2(
                square.Position.X,
                square.Position.Y + square.Size.Y - height,
                square.Size.X,
                height),
            tile: false);
    }

    private void DrawGroundTile(SpriteLibrary.Strip ground, Rect2 square, GridPosition at)
    {
        // A cheap spatial hash. The multipliers are odd and unequal so neighbouring
        // squares land on different tiles instead of striping along a row or column.
        var pick = Math.Abs(((at.X * 73) ^ (at.Y * 151)) + (at.X * at.Y * 31)) % ground.FrameCount;

        // And a second, independent hash for which way up it goes. Four quarter turns
        // and a mirror give eight orientations from every tile, so the same stone never
        // lies the same way twice in a row — a set of four becomes thirty-two looks, and
        // the directional grain the artist drew stops running the same way across the
        // whole field. Kept apart from the tile hash on purpose: sharing one would tie
        // orientation to choice and put every instance of a tile the same way up again.
        var turns = Math.Abs((at.X * 17) ^ (at.Y * 89)) % 4;
        var mirrored = (Math.Abs((at.X * 41) + (at.Y * 103)) & 1) == 1;

        var centre = square.Position + (square.Size / 2);

        // The mirror rides the transform's scale rather than a negative width on the
        // destination rectangle: Godot draws nothing at all for a rect of negative
        // width, which showed up as a checkerboard of holes where half the squares
        // should have been.
        DrawSetTransform(centre, turns * Mathf.Pi / 2, new Vector2(mirrored ? -1 : 1, 1));

        // Drawn about the centre, so a quarter turn lands the tile back on its own square.
        DrawTextureRectRegion(
            ground.Texture,
            new Rect2(-square.Size.X / 2, -square.Size.Y / 2, square.Size.X, square.Size.Y),
            new Rect2(pick * ground.FrameSize, 0, ground.FrameSize, ground.FrameSize));

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    protected void DrawTokens(IReadOnlyList<Token> tokens, string? activeId)
    {
        // What the board shows is what a hold captures: the next blow's victim must be
        // rolled back to how it *looked*, and how it looked is this list, exactly.
        // The camera reads the same record — the fight as drawn, walker mid-hop
        // included, is exactly what it should be framing.
        _lastShownTokens = tokens;
        _lastActiveId = activeId;

        // Depth first: a token further down the board draws over one further up.
        //
        // The figures are taller than their squares — statures run to 92 pixels in a 66
        // pixel cell — so a creature's head reaches into the square above it, and which
        // of the two overlapping bodies won was whatever initiative had decided that
        // fight. That reads as sprites punching through each other. Sorting on the row
        // makes the board a shallow scene instead: nearer the viewer is nearer the
        // front, which is what every tile game does and what the eye expects without
        // being told.
        //
        // Then bodies under the living, which settles the *same* square rather than the
        // one above. Two combatants really can share one: MovementRules counts only
        // creatures that are not dead as occupying, so a corpse lies flat and is walked
        // over, and a fallen ally can now be stood on outright. Before this key existed
        // a character who stepped onto a fallen goblin was drawn behind it.
        //
        // Row is the outer key and the layer the inner one, so the two rules never
        // compete: they only ever apply to different pairs of tokens. OrderBy is stable,
        // so anything still tied draws in initiative order as it always did.
        foreach (var token in tokens
            .OrderBy(token => token.Y)
            .ThenBy(token => token.IsDead ? 0 : token.IsDown ? 1 : 2))
        {
            // The walker glides: its pixel position interpolates along the recorded
            // path rather than snapping square to square, which is what makes the walk
            // read as walking once a figure with legs is doing it.
            var centre = token.Id == _walkerId && _walkPath is { } path
                ? WalkingCentre(path)
                : CentreOf(new GridPosition(token.X, token.Y));

            // Three states worth telling apart at a glance: fighting, down but alive,
            // and dead. A character at 0 hit points is the one a watcher most needs to
            // notice — they are still in the fight if somebody reaches them.
            var colour = token.IsDead ? DeadColour : token.IsDown ? DownColour : token.IsParty ? PartyColour : MonsterColour;

            if (token.Id == activeId)
            {
                DrawCircle(centre, (CellPixels / 2f) - 2, ActiveRing, filled: false, width: 2);
            }

            if (_sprites.ForToken(token.IsParty, token.ClassName, token.Name) is { } art)
            {
                DrawSpriteToken(art, token, centre, colour, RestingFacesLeft(tokens, token));
            }
            else
            {
                DrawCircleToken(token, centre, colour);
            }

            // A hit point bar under each standing token: the one number a watcher tracks.
            if (token.IsDead || token.IsDown)
            {
                continue;
            }

            var fraction = token.MaximumHitPoints == 0
                ? 0f
                : Math.Clamp(token.HitPoints / (float)token.MaximumHitPoints, 0f, 1f);

            var barLeft = centre.X - (CellPixels / 2f) + 6;
            var barTop = centre.Y + (CellPixels / 2f) - 8;

            DrawRect(new Rect2(barLeft, barTop, CellPixels - 12, 3), GridLine);
            DrawRect(new Rect2(barLeft, barTop, (CellPixels - 12) * fraction, 3), colour);
        }

        // After the tokens, so a shot passes in front of what it flies over.
        DrawShot();
    }

    /// <summary>The art a ranged attack of this kind flies, or null when it is absent.</summary>
    private SpriteLibrary.Strip? ShotArt(RangedAttackKind kind) => kind switch
    {
        RangedAttackKind.Weapon => _sprites.Arrow,
        RangedAttackKind.Spell => _sprites.Bolt ?? _sprites.Arrow,
        _ => null,
    };

    /// <summary>
    /// Draws the shot in flight, rotated to point the way it is going.
    /// </summary>
    /// <remarks>
    /// Drawn after the tokens so it passes in front of whatever it flies over, and
    /// rotated because both sheets draw their projectile pointing right — the same
    /// convention the walk cycle's facing rests on. A spell bolt cycles its strip as it
    /// travels; the arrow is a single frame and simply flies.
    /// </remarks>
    private void DrawShot()
    {
        if (_shotFrom is not { } from
            || ShotArt(_shotIsSpell ? RangedAttackKind.Spell : RangedAttackKind.Weapon) is not { } strip)
        {
            return;
        }

        var start = CentreOf(from);
        var end = CentreOf(_shotTo);
        var progress = (float)Math.Clamp(_shotElapsed / _shotSeconds, 0, 1);
        var at = start.Lerp(end, progress);

        var frame = strip.FrameCount <= 1
            ? 0
            : (int)(_shotElapsed * AnimationFramesPerSecond) % strip.FrameCount;

        // Scaled to the square rather than drawn at source size: these sheets are far
        // larger than a 66-pixel cell, and a projectile wider than the board would be
        // the whole screen flashing rather than something crossing it.
        var scale = (CellPixels * 0.7f) / strip.FrameSize;
        var size = strip.FrameSize * scale;

        DrawSetTransform(at, (end - start).Angle(), Vector2.One);

        DrawTextureRectRegion(
            strip.Texture,
            new Rect2(-size / 2, -size / 2, size, size),
            new Rect2(frame * strip.FrameSize, 0, strip.FrameSize, strip.FrameSize));

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>How far along its route the walker has come, in squares.</summary>
    private double SquaresWalked() => _walkElapsed / SecondsPerWalkSquare;

    /// <summary>Where the walking token is drawn, part-way between two squares.</summary>
    private Vector2 WalkingCentre(IReadOnlyList<GridPosition> path)
    {
        var progress = Math.Min(SquaresWalked(), path.Count - 1);
        var index = Math.Min((int)progress, path.Count - 2);
        var fraction = (float)(progress - index);

        return CentreOf(path[index]).Lerp(CentreOf(path[index + 1]), fraction);
    }

    /// <summary>The token as it always drew: a filled or hollow circle with its letter.</summary>
    private void DrawCircleToken(Token token, Vector2 centre, Color colour)
    {
        if (token.IsDown)
        {
            // Hollow: down but not gone.
            DrawCircle(centre, (CellPixels / 2f) - 7, colour, filled: false, width: 2);
        }
        else
        {
            DrawCircle(centre, (CellPixels / 2f) - 7, colour);
        }

        DrawString(
            TextFont,
            centre + new Vector2(-5, 5),
            token.Label.ToString(),
            fontSize: 15,
            modulate: token.IsDead || token.IsDown ? Dim : Background);
    }

    /// <summary>
    /// The figure height, in source pixels, that a square is sized around. It is what a
    /// standing human is drawn at across every one of these packs, so measuring against
    /// it puts one pixel scale on the whole board: a goblin then reads shorter than an
    /// orc because the artist drew it shorter, and every pack keeps the same pixel size,
    /// which is what makes them look like one set of art rather than several.
    /// </summary>
    private const float NominalStature = 64f;

    /// <summary>How much of a square's height that nominal figure fills.</summary>
    private const float StatureFraction = 0.95f;

    /// <summary>
    /// How far past its square a figure may draw before it is shrunk to fit. Generous —
    /// a tall orc standing over its square looks right, and only something built on a
    /// different scale entirely (the dragon, half as tall as it is wide) is cut down.
    /// </summary>
    private const float WidestSpan = 1.6f;
    private const float TallestSpan = 1.45f;

    /// <summary>The token as animated art, with the letter kept in the cell's corner.</summary>
    /// <remarks>
    /// <para>
    /// The states map to the sheets: standing cycles Idle, the walking token cycles Walk
    /// as it glides its route, and the one-shot poses each play their strip once through
    /// — Attack faced at its target, Hurt as a blow lands, Dead as the body goes down.
    /// Once down it holds the Dead sheet's last frame, dimmed for the dead and ringed in
    /// <see cref="DownColour"/> for the downed so "still saveable" reads at a glance. A
    /// pack with no Dead sheet (the Priests) lies its idle frame on its back instead.
    /// </para>
    /// <para>
    /// <b>Every strip is drawn through one transform</b>, built from the figure's
    /// measurements rather than the strip's own — see <see cref="SpriteLibrary.Figure"/>
    /// for why. That is what keeps a character exactly one size whatever it is doing,
    /// and what lets the swing's drawn lunge actually carry the body forward.
    /// </para>
    /// </remarks>
    private void DrawSpriteToken(
        SpriteLibrary.CharacterArt art,
        Token token,
        Vector2 centre,
        Color colour,
        bool restingFacesLeft)
    {
        var fallen = token.IsDead || token.IsDown;
        var posing = token.Id == _poseActorId ? _pose : Pose.None;
        var walking = token.Id == _walkerId && !fallen;

        // A fall belongs to the moment of dropping; the flinch and swing belong to a
        // combatant still on its feet.
        if (posing is Pose.Fall ? !fallen : fallen)
        {
            posing = Pose.None;
        }

        var strip = posing switch
        {
            Pose.Swing => art.Attack,
            Pose.Cast => art.Cast,
            Pose.Flinch => art.Hurt,
            Pose.Fall => art.Dead,
            _ => fallen ? art.Dead : walking && art.Walk is not null ? art.Walk : art.Idle,
        };

        // The one gap in the packs: no Dead sheet means the fallen draw their idle
        // frame rotated onto its back rather than dropping back to a circle.
        var lying = fallen && art.Dead is null;

        if (lying)
        {
            strip = art.Idle;
        }

        if (strip is null)
        {
            DrawCircleToken(token, centre, colour);
            return;
        }

        var facesLeft = posing is Pose.Swing && _poseFacesLeft is { } toward ? toward
            : walking && _walkerFacesLeft is { } turned ? turned
            : restingFacesLeft;

        // A fall runs only as far as the body settles and stops there, rather than
        // playing on into the frames where the pack takes the body away.
        var last = posing is Pose.Fall || (fallen && !lying) ? art.Repose : strip.FrameCount - 1;

        // Each state keeps its own clock: a one-shot pose plays its strip exactly once
        // across its own duration whatever the frame count, a walk cycles at its own
        // quicker cadence, idling ticks the shared loop, and the fallen hold the frame
        // they came to rest on.
        var frame = posing is not Pose.None
            ? Math.Min((int)(_poseElapsed * AnimationFramesPerSecond), last)
            : fallen
                ? (lying ? 0 : last)
                : walking
                    ? (int)(SquaresWalked() * WalkCyclesPerSquare * strip.FrameCount) % strip.FrameCount
                    : _animationFrame % strip.FrameCount;

        var modulate = token.IsDead ? new Color(0.5f, 0.5f, 0.55f) : Colors.White;
        var figure = art.Figure;
        var scale = ScaleFor(figure);

        // The ground line was measured in the idle strip's canvas, and a pose sheet may
        // carry a different canvas height — the hand-drawn Fighter's thrust is 101 rows
        // to its idle's 76, which put the thrust's bottom edge (and the feet on it) a
        // third of a square through the floor, so the Fighter appeared to fall on every
        // swing. Feet sit on every canvas's bottom edge, so the line carries over as a
        // distance from the bottom, not from the top; for the packs, whose strips all
        // share one canvas, this is exactly GroundY.
        var ground = strip.FrameSize - ((art.Idle?.FrameSize ?? strip.FrameSize) - figure.GroundY);

        // Off the centre rather than the grid square, so a gliding walker's feet move
        // with it instead of stair-stepping a square behind.
        var anchor = new Vector2(centre.X, centre.Y + (CellPixels / 2f) - 2);

        if (lying)
        {
            // On its back: rotated a quarter turn about the cell's centre.
            DrawSetTransform(centre, Mathf.Pi / 2f, new Vector2(scale, scale));
            DrawTextureRectRegion(
                strip.Texture,
                new Rect2(-figure.CentreX, -(ground - (figure.Stature / 2f)), strip.FrameSize, strip.FrameSize),
                new Rect2(frame * strip.FrameSize, 0, strip.FrameSize, strip.FrameSize),
                modulate);
        }
        else
        {
            // The figure's own ground line on the square's floor and its own centre line
            // on the square's centre; the horizontal flip is the transform's negative
            // scale, which mirrors about that same centre line.
            DrawSetTransform(anchor, 0f, new Vector2(facesLeft ? -scale : scale, scale));
            DrawTextureRectRegion(
                strip.Texture,
                new Rect2(-figure.CentreX, -ground, strip.FrameSize, strip.FrameSize),
                new Rect2(frame * strip.FrameSize, 0, strip.FrameSize, strip.FrameSize),
                modulate);
        }

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        if (token.IsDown)
        {
            DrawCircle(centre, (CellPixels / 2f) - 7, DownColour, filled: false, width: 2);
        }

        // The letter stays — it is how the log names this combatant — tucked into the
        // cell's corner where it does not sit on the figure's face.
        DrawString(
            TextFont,
            new Vector2(centre.X - (CellPixels / 2f) + 3, centre.Y - (CellPixels / 2f) + 11),
            token.Label.ToString(),
            fontSize: 11,
            modulate: token.IsDead || token.IsDown ? Dim : colour);
    }

    /// <summary>
    /// How much to magnify one figure: the board's shared pixel scale, cut down only for
    /// a creature that would not fit its square at it.
    /// </summary>
    /// <remarks>
    /// The result is snapped to a quarter step. Pixel art enlarged by an arbitrary
    /// fraction gives its source pixels uneven sizes on screen — some one screen pixel,
    /// some two — which crawls as the frames cycle; a clean ratio keeps the grid of
    /// pixels even. At today's board a square comes out 66 pixels, which puts the shared
    /// scale on exactly 1.0: the art is drawn at its own resolution.
    /// </remarks>
    private float ScaleFor(SpriteLibrary.Figure figure)
    {
        var shared = StatureFraction * CellPixels / NominalStature;

        var fits = Math.Min(
            WidestSpan * CellPixels / figure.Breadth,
            TallestSpan * CellPixels / figure.Stature);

        return shared <= fits
            ? Math.Max(0.25f, MathF.Round(shared * 4f) / 4f)

            // Snapped *down* for an oversized figure: rounding to the nearest quarter
            // could round back up past the very bound being applied.
            : Math.Max(0.25f, MathF.Floor(fits * 4f) / 4f);
    }

    protected void DrawTurnOrder(
        IReadOnlyList<Token> tokens,
        string? activeId,
        IReadOnlySet<string>? unseen = null)
    {
        // The panel is an overlay now, not a reserved column: fullscreen gave the board
        // the whole width, so the initiative list and the log float over the field's
        // right edge on a translucent backdrop. Translucent rather than opaque so the
        // ground still reads as continuing underneath — the panel shares the space, it
        // does not take it back.
        DrawRect(
            new Rect2(PanelLeft - 16, 8, ScreenWidth - PanelLeft + 8, ScreenHeight - 16),
            Veil);

        DrawString(TextFont, new Vector2(PanelLeft, UiTop - 8), "INITIATIVE", fontSize: 12, modulate: Dim);

        var y = UiTop + 16;

        foreach (var token in tokens)
        {
            // A combatant the fog hides keeps its row — initiative order is knowledge
            // the party has from the fight itself — but its state is withheld, because
            // hit points read through a wall would be the panel scouting for free.
            var hidden = unseen?.Contains(token.Id) == true && !token.IsDead;

            var colour = hidden ? Dim
                : token.IsDead ? Dim
                : token.IsDown ? DownColour
                : token.IsParty ? PartyColour
                : MonsterColour;
            var marker = token.Id == activeId ? "▶ " : "  ";

            var state = token.IsDead
                ? "dead"
                : hidden
                    ? "unseen"
                    : $"{token.HitPoints}/{token.MaximumHitPoints} hp" +
                      (token.Conditions.Length > 0 ? $" — {token.Conditions}" : string.Empty);

            DrawString(
                TextFont,
                new Vector2(PanelLeft, y),
                $"{marker}{token.Label}  {token.Name,-22} {state}",
                fontSize: 13,
                modulate: colour);

            y += 19;
        }
    }

    protected void DrawLog(IReadOnlyList<CombatStep> log, int count, int tokenCount)
    {
        var top = UiTop + 16 + (tokenCount * 19) + 26;

        DrawString(TextFont, new Vector2(PanelLeft, top - 12), "COMBAT LOG", fontSize: 12, modulate: Dim);

        // The log appends and never replaces — the one thing GoldBox's got wrong and this
        // project committed to in Phase 3. The window shows the tail of what has happened
        // so far.
        //
        // Narration is *wrapped*, never trimmed. It used to be cut at 78 characters with
        // an ellipsis, and because whether an attack hit is the last word of the
        // sentence, the cut reliably removed the one thing the reader needed: "d20 11+5
        // = 16 vs AC 18 — …" said everything except the answer (#161). A client whose
        // whole job is to print what the engine explained cannot afford that.
        var room = Math.Max(0, (ScreenHeight - top - 20) / 17);

        // Held back to whatever the animation has actually shown, so the narration and
        // the picture of it land together.
        var wrapped = log
            .Take(Math.Min(count, RevealedLogCount))
            .SelectMany(step => Wrap(step.Narration, LogWidthCharacters)
                // A continuation is indented, so a wrapped entry still reads as one.
                .Select((line, index) => (Text: index == 0 ? line : "  " + line, step.Kind)))
            .ToArray();

        var y = top + 8;

        foreach (var (text, kind) in wrapped.TakeLast(room))
        {
            // What a line is *about* still tints it — a round beginning and a fight
            // ending are headings, and the roll that opens an attack is quieter than
            // its outcome. Within the line, the names and the outcome are picked out
            // by the highlighter, which is where the reader's eye actually goes.
            var colour = kind switch
            {
                CombatStepKind.RoundStarted or CombatStepKind.EncounterEnded => ActiveRing,
                CombatStepKind.Died or CombatStepKind.Downed => Ink,
                _ => Dim,
            };

            var x = (float)PanelLeft;

            foreach (var span in Highlighter.Spans(text, colour))
            {
                DrawString(TextFont, new Vector2(x, y), span.Text, fontSize: LogFontSize, modulate: span.Colour);
                x += TextFont.GetStringSize(span.Text, fontSize: LogFontSize).X;
            }

            y += 17;
        }
    }

    /// <summary>
    /// The log's type size. Named because the span-by-span drawing has to *measure* in
    /// the same size it draws in — one place for both, or coloured runs would drift out
    /// of step with the text they follow.
    /// </summary>
    private const int LogFontSize = 12;

    /// <summary>
    /// How many characters of narration fit across the log panel. Measured against the
    /// panel's real width rather than guessed — the fallback font at this size runs
    /// about 5.7 pixels to the character — and kept short of the edge, because the
    /// figure is an average and a line of wide letters must not run off the screen.
    /// </summary>
    // Sized to the overlay strip the log now lives in, not the old dedicated column —
    // a wrapped line that outruns its backdrop would spill onto the battlefield.
    private const int LogWidthCharacters = 56;

    protected static string Trim(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

    /// <summary>
    /// Breaks text into lines of at most <paramref name="width"/> characters, on word
    /// boundaries, keeping any newlines the text already had.
    /// </summary>
    protected static IReadOnlyList<string> Wrap(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = string.Empty;

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length + word.Length + 1 > width && line.Length > 0)
                {
                    lines.Add(line);
                    line = string.Empty;
                }

                line += (line.Length > 0 ? " " : string.Empty) + word;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>Renders one frame to a PNG. The verification loop for these screens.</summary>
    protected async Task CaptureFrame(string path)
    {
        QueueRedraw();

        // Two frames: the first arranges the scene, the second is drawn and readable.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);

        GD.Print(error == Error.Ok
            ? $"captured to {path}"
            : $"could not save {path}: {error}");
    }

    protected internal static string? ArgumentValue(string name)
    {
        foreach (var argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith($"--{name}=", StringComparison.Ordinal))
            {
                return argument[(name.Length + 3)..];
            }
        }

        return null;
    }

    protected internal static bool HasArgument(string name) =>
        OS.GetCmdlineUserArgs().Contains($"--{name}") || ArgumentValue(name) is not null;

    /// <summary>
    /// Walks up for <c>data/srd</c>, exactly as the console client does, so the viewer
    /// runs from wherever Godot was launched.
    /// </summary>
    private static string ContentDirectory()
    {
        var directory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "data", "srd");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No data/srd found above the project directory.");
    }
}
