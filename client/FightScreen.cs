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

            if (_poseElapsed >= SecondsFor(_poseFrames))
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
        DrawRect(new Rect2(0, 0, ScreenWidth, ScreenHeight), Background);
        DrawString(TextFont, new Vector2(GridLeft, 34), Title, fontSize: 20, modulate: Ink);
        DrawString(TextFont, new Vector2(GridLeft, 58), subtitle, fontSize: 13, modulate: Dim);
        DrawString(TextFont, new Vector2(GridLeft, 78), statusLine, fontSize: 12, modulate: Dim);
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
    /// keeps a wash over its tile: art may not cost a player the one thing the square was
    /// telling them. A wall and a low obstacle say it with a sprite instead, since a tree
    /// filling a square and a bush sitting in one read as blocked and passable without
    /// anything being written down. With no art loaded every square falls back to the flat
    /// colours and the outline it always had.
    /// </para>
    /// </remarks>
    protected void DrawGrid()
    {
        var theme = Theme;

        for (var x = 0; x < GridWidth; x++)
        {
            for (var y = 0; y < GridHeight; y++)
            {
                var square = new Rect2(GridLeft + (x * CellPixels), GridTop + (y * CellPixels), CellPixels, CellPixels);
                var position = new GridPosition(x, y);
                var blocked = BlockedSquares.Contains(position);
                var low = LowObstacleSquares.Contains(position);

                if (theme is null)
                {
                    if (blocked)
                    {
                        DrawRect(square, Blocked);
                    }
                    else if (low)
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

                DrawTextureRect(theme.Ground, square, tile: false);

                // Difficult ground is a rule, not a decoration: it survives the art.
                if (DifficultSquares.Contains(position))
                {
                    DrawRect(square, DifficultWash);
                }

                if (blocked && theme.Wall is { } wall)
                {
                    DrawStanding(wall, square, WallScale);
                }
                else if (low && theme.Low is { } bush)
                {
                    DrawStanding(bush, square, LowScale);
                }
            }
        }
    }

    /// <summary>
    /// Draws a piece of scenery standing on a square: sized to the cell and set on its
    /// floor, so a tree grows up out of its square rather than floating in the middle.
    /// </summary>
    private void DrawStanding(Texture2D texture, Rect2 square, float fraction)
    {
        var width = square.Size.X * fraction;
        var height = texture.GetHeight() * width / texture.GetWidth();

        DrawTextureRect(
            texture,
            new Rect2(
                square.Position.X + ((square.Size.X - width) / 2),
                square.Position.Y + square.Size.Y - height,
                width,
                height),
            tile: false);
    }

    /// <summary>How much of a square a wall's scenery fills, and a low obstacle's.</summary>
    private const float WallScale = 1.0f;
    private const float LowScale = 0.6f;

    protected void DrawTokens(IReadOnlyList<Token> tokens, string? activeId)
    {
        // What the board shows is what a hold captures: the next blow's victim must be
        // rolled back to how it *looked*, and how it looked is this list, exactly.
        _lastShownTokens = tokens;

        // Bodies first, then the living over them. Two combatants really can share a
        // square: MovementRules counts only creatures that are *not dead* as occupying,
        // so a corpse lies flat and is walked over. The list arrives in initiative
        // order, so which of them landed on top was whatever the dice had decided that
        // fight — a character who stepped onto a fallen goblin was drawn behind it.
        // A gliding walker overlaps squares it merely passes through, so this settles
        // that case too. OrderBy is stable, so everything within a layer still draws in
        // initiative order.
        foreach (var token in tokens.OrderBy(token => token.IsDead ? 0 : token.IsDown ? 1 : 2))
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
            ? Math.Min((int)(_poseElapsed * AnimationFramesPerSecond), last)
            : fallen
                ? (lying ? 0 : last)
                : walking
                    ? (int)(SquaresWalked() * WalkCyclesPerSquare * strip.FrameCount) % strip.FrameCount
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
