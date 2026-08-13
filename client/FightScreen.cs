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
        string Conditions);

    public override void _Ready()
    {
        TextFont = ThemeDB.GetFallbackFont();
        OnReady();
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
        string.Join(", ", combatant.Conditions));

    /// <summary>Initiative order, not build order: it is what a watcher actually tracks.</summary>
    protected static List<Token> TokensFrom(Encounter encounter, Labels labels) =>
        [.. encounter.TurnOrder.Select(combatant => TokenFrom(combatant, labels))];

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
        var lines = Math.Max(0, (ScreenHeight - top - 20) / 17);
        var written = log.Take(count).ToArray();

        var y = top + 8;

        foreach (var step in written.TakeLast(lines))
        {
            var colour = step.Kind switch
            {
                CombatStepKind.Damage or CombatStepKind.Died or CombatStepKind.Downed => MonsterColour,
                CombatStepKind.Feature or CombatStepKind.Spell or CombatStepKind.Item => PartyColour,
                CombatStepKind.RoundStarted or CombatStepKind.EncounterEnded => ActiveRing,
                _ => Dim,
            };

            DrawString(TextFont, new Vector2(PanelLeft, y), Trim(step.Narration, 78), fontSize: 12, modulate: colour);
            y += 17;
        }
    }

    protected static string Trim(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

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
