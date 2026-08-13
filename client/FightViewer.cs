using Godot;
using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Viewer;

/// <summary>
/// Watches a fight. The first rung of Phase 7, and deliberately read-only: it drives the
/// tactics policy, never the player, and calls no action the engine could refuse.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole fight is resolved up front, into snapshots.</b> Scrubbing back and forth
/// then touches nothing but a list — the engine is run once, forwards, exactly as it
/// would be anywhere else. Replaying by re-running would be the alternative and it would
/// be wrong: <c>IRandomSource</c> is consumed as the fight goes, so a second pass is a
/// different fight.
/// </para>
/// <para>
/// <b>Nothing here recomputes a rule.</b> Positions, hit points, conditions and the
/// narration all come off the engine; the viewer decides only where to put them on the
/// screen. That is the same discipline the console client is held to, and it is what
/// keeps the client from becoming a second place the rules live.
/// </para>
/// </remarks>
public partial class FightViewer : Node2D
{
    private const int CellPixels = 42;
    private const int GridLeft = 24;
    private const int GridTop = 96;
    private const int PanelLeft = 610;
    private const double SecondsPerTurn = 0.6;

    private static readonly Color Background = new("16161d");
    private static readonly Color GridLine = new("2c2c38");
    private static readonly Color Difficult = new("2a2438");
    private static readonly Color Blocked = new("3a2a2a");
    private static readonly Color PartyColour = new("5a9fd4");
    private static readonly Color MonsterColour = new("c4614f");
    private static readonly Color DeadColour = new("4a4a52");
    private static readonly Color DownColour = new("8a6a4a");
    private static readonly Color ActiveRing = new("e8d5a0");
    private static readonly Color Ink = new("d8d8e0");
    private static readonly Color Dim = new("8a8a96");

    private readonly List<Snapshot> _snapshots = [];

    private Font _font = null!;
    private IReadOnlyList<CombatStep> _log = [];
    private int _index;
    private double _elapsed;
    private bool _playing = true;
    private string _heading = string.Empty;
    private int _gridWidth;
    private int _gridHeight;
    private IReadOnlyCollection<GridPosition> _blocked = [];
    private IReadOnlyCollection<GridPosition> _difficult = [];

    /// <summary>One combatant, as it stood at the end of a turn.</summary>
    /// <remarks>
    /// A copy rather than a reference on purpose: <see cref="Combatant"/> is mutable and
    /// the fight moves on, so holding one would make every snapshot show the last frame.
    /// </remarks>
    private sealed record Token(
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

    /// <summary>The fight at the end of one turn, and how much of the log had been written.</summary>
    private sealed record Snapshot(int Round, string? ActiveId, IReadOnlyList<Token> Tokens, int LogCount);

    public override void _Ready()
    {
        _font = ThemeDB.GetFallbackFont();

        var seed = ArgumentValue("seed") is { } text && int.TryParse(text, out var parsed) ? parsed : 20250812;

        Resolve(seed);

        // A capture run renders one frame and leaves, which is how a change to this
        // screen gets checked without a person watching it.
        if (ArgumentValue("capture") is { } path)
        {
            _playing = false;
            _index = ArgumentValue("at") is { } at && int.TryParse(at, out var wanted)
                ? Math.Clamp(wanted, 0, _snapshots.Count - 1)
                : _snapshots.Count - 1;

            CaptureAndQuit(path);
        }
    }

    /// <summary>
    /// Runs the whole fight, keeping a snapshot after every turn.
    /// </summary>
    /// <remarks>
    /// The round limit mirrors <c>SimpleTacticsPolicy.RunToCompletion</c>'s: two
    /// creatures that can never reach each other would otherwise loop forever, and a
    /// viewer that hangs on startup is worse than one that shows a stalled fight.
    /// </remarks>
    private void Resolve(int seed)
    {
        var content = ContentLoader.Load(ContentDirectory());
        var party = PregeneratedParty.Build(content, level: 3);
        var random = new SeededRandomSource(seed);
        var fight = EncounterFactory.Build(content, party, EncounterDifficulty.Moderate, random);

        var encounter = fight.Encounter;
        var labels = Labels.For(encounter.Combatants);

        _log = encounter.Log;
        _gridWidth = encounter.Battlefield.Width;
        _gridHeight = encounter.Battlefield.Height;
        _blocked = encounter.Battlefield.Blocked;
        _difficult = encounter.Battlefield.DifficultTerrain;

        var roster = string.Join(", ", fight.Built.Monsters
            .GroupBy(monster => monster.Name)
            .Select(group => group.Count() > 1 ? $"{group.Count()} {group.Key}s" : group.Key));

        _heading = $"seed {seed} — the party against {roster}";

        Capture(encounter, labels);

        while (!encounter.IsComplete && encounter.Round <= 50)
        {
            SimpleTacticsPolicy.TakeTurn(encounter);
            Capture(encounter, labels);
        }
    }

    private void Capture(Encounter encounter, Labels labels)
    {
        // Initiative order, not the order they were built in: it is what a watcher is
        // actually tracking, and the engine already keeps it.
        var tokens = encounter.TurnOrder
            .Select(combatant => new Token(
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
                string.Join(", ", combatant.Conditions)))
            .ToArray();

        _snapshots.Add(new Snapshot(
            Math.Max(1, encounter.Round),
            encounter.ActiveCombatant?.Id,
            tokens,
            encounter.Log.Count));
    }

    public override void _Process(double delta)
    {
        if (!_playing || _index >= _snapshots.Count - 1)
        {
            return;
        }

        _elapsed += delta;

        if (_elapsed < SecondsPerTurn)
        {
            return;
        }

        _elapsed = 0;
        _index++;
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Space:
                _playing = !_playing;
                break;
            case Key.Right:
                _playing = false;
                _index = Math.Min(_snapshots.Count - 1, _index + 1);
                break;
            case Key.Left:
                _playing = false;
                _index = Math.Max(0, _index - 1);
                break;
            case Key.Home:
                _playing = false;
                _index = 0;
                break;
            case Key.End:
                _playing = false;
                _index = _snapshots.Count - 1;
                break;
            case Key.Escape:
                GetTree().Quit();
                return;
            default:
                return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_snapshots.Count == 0)
        {
            return;
        }

        var snapshot = _snapshots[_index];

        DrawRect(new Rect2(0, 0, 1280, 760), Background);

        DrawString(_font, new Vector2(GridLeft, 34), "SRD_Combat — watching a fight", fontSize: 20, modulate: Ink);
        DrawString(_font, new Vector2(GridLeft, 58), _heading, fontSize: 13, modulate: Dim);
        DrawString(
            _font,
            new Vector2(GridLeft, 78),
            $"round {snapshot.Round}   turn {_index} of {_snapshots.Count - 1}   " +
            (_playing ? "playing" : "paused") + "   [space] play/pause  [←/→] step  [esc] quit",
            fontSize: 12,
            modulate: Dim);

        DrawGrid();
        DrawTokens(snapshot);
        DrawTurnOrder(snapshot);
        DrawLog(snapshot);
    }

    private void DrawGrid()
    {
        for (var x = 0; x < _gridWidth; x++)
        {
            for (var y = 0; y < _gridHeight; y++)
            {
                var square = new Rect2(GridLeft + (x * CellPixels), GridTop + (y * CellPixels), CellPixels, CellPixels);
                var position = new GridPosition(x, y);

                if (_blocked.Contains(position))
                {
                    DrawRect(square, Blocked);
                }
                else if (_difficult.Contains(position))
                {
                    DrawRect(square, Difficult);
                }

                DrawRect(square, GridLine, filled: false, width: 1);
            }
        }
    }

    private void DrawTokens(Snapshot snapshot)
    {
        foreach (var token in snapshot.Tokens)
        {
            var centre = new Vector2(
                GridLeft + (token.X * CellPixels) + (CellPixels / 2f),
                GridTop + (token.Y * CellPixels) + (CellPixels / 2f));

            // Three states worth telling apart at a glance: fighting, down but alive,
            // and dead. A character at 0 hit points is the one a watcher most needs to
            // notice — they are still in the fight if somebody reaches them.
            var colour = token.IsDead ? DeadColour : token.IsDown ? DownColour : token.IsParty ? PartyColour : MonsterColour;

            if (token.Id == snapshot.ActiveId)
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
                _font,
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

    private void DrawTurnOrder(Snapshot snapshot)
    {
        DrawString(_font, new Vector2(PanelLeft, GridTop - 8), "INITIATIVE", fontSize: 12, modulate: Dim);

        var y = GridTop + 16;

        foreach (var token in snapshot.Tokens)
        {
            var colour = token.IsDead ? Dim : token.IsDown ? DownColour : token.IsParty ? PartyColour : MonsterColour;
            var marker = token.Id == snapshot.ActiveId ? "▶ " : "  ";

            var state = token.IsDead
                ? "dead"
                : $"{token.HitPoints}/{token.MaximumHitPoints} hp" +
                  (token.Conditions.Length > 0 ? $" — {token.Conditions}" : string.Empty);

            DrawString(
                _font,
                new Vector2(PanelLeft, y),
                $"{marker}{token.Label}  {token.Name,-22} {state}",
                fontSize: 13,
                modulate: colour);

            y += 19;
        }
    }

    private void DrawLog(Snapshot snapshot)
    {
        var top = GridTop + 16 + (_snapshots[0].Tokens.Count * 19) + 26;

        DrawString(_font, new Vector2(PanelLeft, top - 12), "COMBAT LOG", fontSize: 12, modulate: Dim);

        // The log appends and never replaces — the one thing GoldBox's got wrong and this
        // project committed to in Phase 3. The window shows the tail of what has happened
        // so far, so scrubbing back really does show less.
        var lines = Math.Max(0, (760 - top - 20) / 17);
        var written = _log.Take(snapshot.LogCount).ToArray();

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

            DrawString(_font, new Vector2(PanelLeft, y), Trim(step.Narration, 78), fontSize: 12, modulate: colour);
            y += 17;
        }
    }

    private static string Trim(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

    /// <summary>Renders one frame to a PNG and leaves. The verification loop for this screen.</summary>
    private async void CaptureAndQuit(string path)
    {
        QueueRedraw();

        // Two frames: the first arranges the scene, the second is drawn and readable.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);

        GD.Print(error == Error.Ok
            ? $"captured turn {_index} of {_snapshots.Count - 1} to {path}"
            : $"could not save {path}: {error}");

        GetTree().Quit();
    }

    private static string? ArgumentValue(string name)
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
