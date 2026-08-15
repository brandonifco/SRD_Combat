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
    protected const int GridLeft = 24;
    protected const int GridTop = 96;
    protected const int PanelLeft = 664;
    protected const int ScreenWidth = 1600;
    protected const int ScreenHeight = 950;
    protected const double SecondsPerTurn = 0.6;

    /// <summary>
    /// The room the board has. The square size is derived to fill it rather than fixed,
    /// so a battlefield of any shape uses the whole area — and a future field wider than
    /// today's nine squares shrinks its squares instead of growing into the side panel.
    /// </summary>
    private const int BoardWidth = 616;
    private const int BoardHeight = 470;

    /// <summary>
    /// Bounds on the derived square. The ceiling keeps a small skirmish from drawing
    /// squares so large the figures look like portraits; the floor keeps a huge field
    /// legible rather than letting it vanish.
    /// </summary>
    private const int LargestCell = 72;
    private const int SmallestCell = 24;

    /// <summary>
    /// The board square, in pixels. Set from the battlefield's shape, so it is only
    /// meaningful once a fight has been adopted; the default is what an empty screen
    /// draws its (nonexistent) grid at.
    /// </summary>
    protected int CellPixels { get; private set; } = 48;

    protected static readonly Color Background = new("16161d");
    protected static readonly Color GridLine = new("2c2c38");
    protected static readonly Color Difficult = new("2a2438");
    protected static readonly Color Blocked = new("3a2a2a");
    protected static readonly Color LowObstacle = new("4a4032");
    protected static readonly Color PartyColour = new("5a9fd4");
    protected static readonly Color MonsterColour = new("c4614f");
    protected static readonly Color DeadColour = new("4a4a52");
    protected static readonly Color DownColour = new("8a6a4a");
    protected static readonly Color ActiveRing = new("e8d5a0");
    protected static readonly Color Ink = new("d8d8e0");
    protected static readonly Color Dim = new("8a8a96");

    /// <summary>Seconds a walking token spends on each square of its recorded path.</summary>
    protected const double SecondsPerWalkSquare = 0.1;

    /// <summary>
    /// One thing the board plays out before the next beat: a walk along a recorded
    /// route, or a swing at a target. Queued in log order, so an Opportunity Attack
    /// plays where it interrupted the walk that provoked it.
    /// </summary>
    private abstract record Act;

    private sealed record WalkAct(string WalkerId, IReadOnlyList<GridPosition> Path) : Act;

    /// <summary>A one-shot pose: a strip played once through, then done.</summary>
    /// <param name="FacesLeft">
    /// Which way it faces, read from where the two stood when it was queued. Null when
    /// they shared a column, or when facing is not the pose's business — the actor then
    /// keeps its side's default facing.
    /// </param>
    private sealed record PoseAct(string ActorId, Pose Pose, bool? FacesLeft = null) : Act;

    /// <summary>The one-shot animations, each with its own strip and its own tempo.</summary>
    protected enum Pose
    {
        None,

        /// <summary>The Attack strip, played at whatever it swings.</summary>
        Swing,

        /// <summary>The Hurt strip: a flinch as damage lands.</summary>
        Flinch,

        /// <summary>The Dead strip, played through as the body goes down.</summary>
        Fall,
    }

    private readonly Queue<Act> _pendingActs = new();
    private string? _walkerId;
    private IReadOnlyList<GridPosition>? _walkPath;
    private double _walkElapsed;
    private int _walkSquare;

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

    /// <summary>
    /// How long each pose takes, whatever its strip's frame count — the strip is fitted
    /// to the time rather than the time to the strip, so a fourteen-frame Priest attack
    /// and a five-frame Goblin one take the same beat. A flinch is brief because it
    /// interrupts nothing; a fall is slow because it is the last thing a creature does.
    /// </summary>
    private static double SecondsFor(Pose pose) => pose switch
    {
        Pose.Swing => 0.45,
        Pose.Flinch => 0.22,
        Pose.Fall => 0.6,
        _ => 0,
    };

    /// <summary>Seconds per Walk-strip frame — quicker than idle, so legs visibly move.</summary>
    private const double SecondsPerWalkFrame = 0.07;

    /// <summary>Seconds each frame of an idle or walk loop is shown.</summary>
    private const double SecondsPerAnimationFrame = 0.12;

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

        OnReady();
    }

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
        var frame = (int)(_animationClock / SecondsPerAnimationFrame);

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
    protected void AdoptBattlefield(Encounter encounter)
    {
        GridWidth = encounter.Battlefield.Width;
        GridHeight = encounter.Battlefield.Height;
        BlockedSquares = encounter.Battlefield.Blocked;
        DifficultSquares = encounter.Battlefield.DifficultTerrain;
        LowObstacleSquares = encounter.Battlefield.LowObstacles;

        CellPixels = Math.Clamp(
            Math.Min(BoardWidth / Math.Max(1, GridWidth), BoardHeight / Math.Max(1, GridHeight)),
            SmallestCell,
            LargestCell);
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
    protected bool ActInProgress => _walkPath is not null || _poseActorId is not null || _pendingActs.Count > 0;

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
        for (var index = Math.Max(0, from); index < to && index < log.Count; index++)
        {
            switch (log[index])
            {
                // One square is no journey: a walk cut short before it left its first
                // square would animate nothing, so it is not queued at all.
                case { Kind: CombatStepKind.Move, ActorId: { } walkerId, Path.Count: > 1 } step:
                    _pendingActs.Enqueue(new WalkAct(walkerId, step.Path));
                    break;

                case
                {
                    Kind: CombatStepKind.Attack or CombatStepKind.OpportunityAttack,
                    ActorId: { } attackerId,
                    TargetId: { } strickenId,
                }:
                    QueuePose(tokens, attackerId, Pose.Swing, facing: strickenId);
                    break;

                case { Kind: CombatStepKind.Damage, TargetId: { } victimId }:
                    QueuePose(tokens, victimId, Pose.Flinch);
                    break;

                case { Kind: CombatStepKind.Died or CombatStepKind.Downed, ActorId: { } fallenId }:
                    QueuePose(tokens, fallenId, Pose.Fall);
                    break;
            }
        }

        // Start immediately so the very next frame draws the walker on its starting
        // square — waiting for the first advance would flash it at the destination.
        if (_walkPath is null && _poseActorId is null)
        {
            StartNextAct();
        }
    }

    /// <summary>
    /// Queues one pose for one combatant, when that combatant's art can show it.
    /// </summary>
    private void QueuePose(IReadOnlyList<Token> tokens, string actorId, Pose pose, string? facing = null)
    {
        if (FindToken(tokens, actorId) is not { } actor
            || _sprites.ForToken(actor.IsParty, actor.ClassName, actor.Name) is not { } art)
        {
            return;
        }

        var strip = pose switch
        {
            Pose.Swing => art.Attack,

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

        _pendingActs.Enqueue(new PoseAct(actorId, pose, facesLeft));
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

    /// <summary>Forgets every queued act — for scrubbing, where snapping is the point.</summary>
    protected void ClearActs()
    {
        _pendingActs.Clear();
        _walkPath = null;
        _walkerId = null;
        _poseActorId = null;
        _pose = Pose.None;
    }

    /// <summary>Advances the playing act; true when the screen should redraw.</summary>
    protected bool AdvanceActs(double delta)
    {
        if (_poseActorId is not null)
        {
            _poseElapsed += delta;

            if (_poseElapsed >= SecondsFor(_pose))
            {
                _poseActorId = null;
                _pose = Pose.None;
                StartNextAct();
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
            StartNextAct();
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
            return false;
        }

        switch (_pendingActs.Dequeue())
        {
            case PoseAct pose:
                _poseActorId = pose.ActorId;
                _pose = pose.Pose;
                _poseFacesLeft = pose.FacesLeft;
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
        DrawRect(new Rect2(0, 0, ScreenWidth, ScreenHeight), Background);
        DrawString(TextFont, new Vector2(GridLeft, 34), Title, fontSize: 20, modulate: Ink);
        DrawString(TextFont, new Vector2(GridLeft, 58), subtitle, fontSize: 13, modulate: Dim);
        DrawString(TextFont, new Vector2(GridLeft, 78), statusLine, fontSize: 12, modulate: Dim);
    }

    protected void DrawGrid()
    {
        for (var x = 0; x < GridWidth; x++)
        {
            for (var y = 0; y < GridHeight; y++)
            {
                var square = new Rect2(GridLeft + (x * CellPixels), GridTop + (y * CellPixels), CellPixels, CellPixels);
                var position = new GridPosition(x, y);

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
            }
        }
    }

    protected void DrawTokens(IReadOnlyList<Token> tokens, string? activeId)
    {
        foreach (var token in tokens)
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
                DrawSpriteToken(art, token, centre, colour);
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
    }

    /// <summary>Where the walking token is drawn, part-way between two squares.</summary>
    private Vector2 WalkingCentre(IReadOnlyList<GridPosition> path)
    {
        var progress = Math.Min(_walkElapsed / SecondsPerWalkSquare, path.Count - 1);
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
    private void DrawSpriteToken(SpriteLibrary.CharacterArt art, Token token, Vector2 centre, Color colour)
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
            : !token.IsParty;

        // A fall runs only as far as the body settles and stops there, rather than
        // playing on into the frames where the pack takes the body away.
        var last = posing is Pose.Fall || (fallen && !lying) ? art.Repose : strip.FrameCount - 1;

        // Each state keeps its own clock: a one-shot pose plays its strip exactly once
        // across its own duration whatever the frame count, a walk cycles at its own
        // quicker cadence, idling ticks the shared loop, and the fallen hold the frame
        // they came to rest on.
        var frame = posing is not Pose.None
            ? Math.Min((int)(_poseElapsed / SecondsFor(posing) * (last + 1)), last)
            : fallen
                ? (lying ? 0 : last)
                : walking
                    ? (int)(_walkElapsed / SecondsPerWalkFrame) % strip.FrameCount
                    : _animationFrame % strip.FrameCount;

        var modulate = token.IsDead ? new Color(0.5f, 0.5f, 0.55f) : Colors.White;
        var figure = art.Figure;
        var scale = ScaleFor(figure);

        // Off the centre rather than the grid square, so a gliding walker's feet move
        // with it instead of stair-stepping a square behind.
        var anchor = new Vector2(centre.X, centre.Y + (CellPixels / 2f) - 2);

        if (lying)
        {
            // On its back: rotated a quarter turn about the cell's centre.
            DrawSetTransform(centre, Mathf.Pi / 2f, new Vector2(scale, scale));
            DrawTextureRectRegion(
                strip.Texture,
                new Rect2(-figure.CentreX, -(figure.GroundY - (figure.Stature / 2f)), strip.FrameSize, strip.FrameSize),
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
                new Rect2(-figure.CentreX, -figure.GroundY, strip.FrameSize, strip.FrameSize),
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

    protected void DrawTurnOrder(IReadOnlyList<Token> tokens, string? activeId)
    {
        DrawString(TextFont, new Vector2(PanelLeft, GridTop - 8), "INITIATIVE", fontSize: 12, modulate: Dim);

        var y = GridTop + 16;

        foreach (var token in tokens)
        {
            var colour = token.IsDead ? Dim : token.IsDown ? DownColour : token.IsParty ? PartyColour : MonsterColour;
            var marker = token.Id == activeId ? "▶ " : "  ";

            var state = token.IsDead
                ? "dead"
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
        var top = GridTop + 16 + (tokenCount * 19) + 26;

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

        var wrapped = log
            .Take(count)
            .SelectMany(step => Wrap(step.Narration, LogWidthCharacters)
                // A continuation is indented, so a wrapped entry still reads as one.
                .Select((line, index) => (Text: index == 0 ? line : "  " + line, step.Kind)))
            .ToArray();

        var y = top + 8;

        foreach (var (text, kind) in wrapped.TakeLast(room))
        {
            var colour = kind switch
            {
                CombatStepKind.Damage or CombatStepKind.Died or CombatStepKind.Downed => MonsterColour,
                CombatStepKind.Feature or CombatStepKind.Spell or CombatStepKind.Item => PartyColour,
                CombatStepKind.RoundStarted or CombatStepKind.EncounterEnded => ActiveRing,
                _ => Dim,
            };

            DrawString(TextFont, new Vector2(PanelLeft, y), text, fontSize: 12, modulate: colour);
            y += 17;
        }
    }

    /// <summary>
    /// How many characters of narration fit across the log panel. Measured against the
    /// panel's real width rather than guessed — the fallback font at this size runs
    /// about 5.7 pixels to the character — and kept short of the edge, because the
    /// figure is an average and a line of wide letters must not run off the screen.
    /// </summary>
    private const int LogWidthCharacters = 120;

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
