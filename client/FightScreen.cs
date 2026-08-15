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
    protected const int CellPixels = 42;
    protected const int GridLeft = 24;
    protected const int GridTop = 96;
    protected const int PanelLeft = 610;
    protected const int ScreenWidth = 1280;
    protected const int ScreenHeight = 760;
    protected const double SecondsPerTurn = 0.6;

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

    private readonly Queue<(string WalkerId, IReadOnlyList<GridPosition> Path)> _pendingWalks = new();
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

    /// <summary>Whether a walk is playing or waiting to play.</summary>
    protected bool WalkInProgress => _walkPath is not null || _pendingWalks.Count > 0;

    /// <summary>
    /// Queues the walk carried by each new Move step in a slice of the log, so the token
    /// can hop square to square instead of teleporting. The path is the engine's own
    /// record of the route — the screen replays it, never recomputes it.
    /// </summary>
    protected void QueueWalks(IReadOnlyList<CombatStep> log, int from, int to)
    {
        for (var index = Math.Max(0, from); index < to && index < log.Count; index++)
        {
            // One square is no journey: a walk cut short before it left its first
            // square would animate nothing, so it is not queued at all.
            if (log[index] is { Kind: CombatStepKind.Move, ActorId: { } walkerId, Path.Count: > 1 } step)
            {
                _pendingWalks.Enqueue((walkerId, step.Path));
            }
        }

        // Start immediately so the very next frame draws the walker on its starting
        // square — waiting for the first advance would flash it at the destination.
        if (_walkPath is null)
        {
            StartNextWalk();
        }
    }

    /// <summary>Forgets every walk — for scrubbing, where snapping is the point.</summary>
    protected void ClearWalks()
    {
        _pendingWalks.Clear();
        _walkPath = null;
        _walkerId = null;
    }

    /// <summary>Advances the playing walk; true when the screen should redraw.</summary>
    protected bool AdvanceWalks(double delta)
    {
        if (_walkPath is null)
        {
            return StartNextWalk();
        }

        _walkElapsed += delta;
        var square = (int)(_walkElapsed / SecondsPerWalkSquare);

        if (square >= _walkPath.Count)
        {
            _walkPath = null;
            _walkerId = null;
            return true;
        }

        if (square == _walkSquare)
        {
            return false;
        }

        // The sprite faces the way it is going; a purely vertical step keeps the last
        // facing rather than snapping back to the side's default mid-walk.
        var dx = _walkPath[square].X - _walkPath[_walkSquare].X;

        if (dx != 0)
        {
            _walkerFacesLeft = dx < 0;
        }

        _walkSquare = square;
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

    private bool StartNextWalk()
    {
        if (_pendingWalks.Count == 0)
        {
            return false;
        }

        (_walkerId, _walkPath) = _pendingWalks.Dequeue();
        _walkElapsed = 0;
        _walkSquare = 0;
        _walkerFacesLeft = null;

        // Face the route's first horizontal leg from the very first frame, so a walker
        // does not set off looking the wrong way and turn mid-stride.
        for (var index = 1; index < _walkPath.Count; index++)
        {
            var dx = _walkPath[index].X - _walkPath[index - 1].X;

            if (dx != 0)
            {
                _walkerFacesLeft = dx < 0;
                break;
            }
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
            var centre = CentreOf(new GridPosition(token.X, token.Y));

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

            var barLeft = GridLeft + (token.X * CellPixels) + 6;
            var barTop = GridTop + (token.Y * CellPixels) + CellPixels - 8;

            DrawRect(new Rect2(barLeft, barTop, CellPixels - 12, 3), GridLine);
            DrawRect(new Rect2(barLeft, barTop, (CellPixels - 12) * fraction, 3), colour);
        }
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

    /// <summary>How much taller than its cell a standing figure may draw.</summary>
    private const float SpriteOverflow = 10f;

    /// <summary>The token as animated art, with the letter kept in the cell's corner.</summary>
    /// <remarks>
    /// The states map to the sheets: standing cycles Idle, the walking token cycles Walk
    /// along its hop, and dead or down holds the Dead sheet's last frame — a body on the
    /// ground — dimmed for the dead, ringed in <see cref="DownColour"/> for the downed so
    /// "still saveable" stays visible at a glance. A pack with no Dead sheet (the
    /// Priests) lies its idle frame on its back instead.
    /// </remarks>
    private void DrawSpriteToken(SpriteLibrary.CharacterArt art, Token token, Vector2 centre, Color colour)
    {
        var walking = token.Id == _walkerId && !token.IsDead && !token.IsDown;
        var facesLeft = walking && _walkerFacesLeft is { } turned ? turned : !token.IsParty;

        var strip = token.IsDead || token.IsDown
            ? art.Dead
            : walking && art.Walk is not null ? art.Walk : art.Idle;

        // The one gap in the packs: no Dead sheet means the fallen draw their idle
        // frame rotated onto its back rather than dropping back to a circle.
        var lying = (token.IsDead || token.IsDown) && art.Dead is null;

        if (lying)
        {
            strip = art.Idle;
        }

        if (strip is null)
        {
            DrawCircleToken(token, centre, colour);
            return;
        }

        var frame = token.IsDead || token.IsDown
            ? (lying ? 0 : strip.FrameCount - 1)
            : _animationFrame % strip.FrameCount;

        var modulate = token.IsDead
            ? new Color(0.5f, 0.5f, 0.55f)
            : Colors.White;

        var bounds = strip.Bounds;

        // Scale by what the frame actually holds: the figure fills the cell, may stand
        // a little taller than it, and is capped sideways so a dragon does not blanket
        // its neighbours.
        var scale = Math.Min(
            (CellPixels + SpriteOverflow) / bounds.Size.Y,
            (CellPixels * 1.5f) / bounds.Size.X);

        var anchor = new Vector2(centre.X, GridTop + ((token.Y + 1) * CellPixels) - 2);
        var boundsCentreX = bounds.Position.X + (bounds.Size.X / 2f);

        if (lying)
        {
            // On its back: rotated a quarter turn about the cell's centre.
            DrawSetTransform(centre, Mathf.Pi / 2f, new Vector2(scale, scale));
            DrawTextureRectRegion(
                strip.Texture,
                new Rect2(-boundsCentreX, -(bounds.Position.Y + (bounds.Size.Y / 2f)), strip.FrameSize, strip.FrameSize),
                new Rect2(frame * strip.FrameSize, 0, strip.FrameSize, strip.FrameSize),
                token.IsDead ? modulate : Colors.White);
        }
        else
        {
            // Feet planted on the cell's bottom edge; the horizontal flip happens in
            // the transform's scale, about the figure's own centre line.
            DrawSetTransform(anchor, 0f, new Vector2(facesLeft ? -scale : scale, scale));
            DrawTextureRectRegion(
                strip.Texture,
                new Rect2(-boundsCentreX, -bounds.End.Y, strip.FrameSize, strip.FrameSize),
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

    /// <summary>How many characters of narration fit across the log panel.</summary>
    private const int LogWidthCharacters = 78;

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
