using Godot;
using SRDCombat.Core.Combat;
using SRDCombat.Game;

namespace SRDCombat.Viewer;

/// <summary>
/// Watches a fight, read-only: it drives the tactics policy, never the player, and calls
/// no action the engine could refuse.
/// </summary>
/// <remarks>
/// <b>The whole fight is resolved up front, into snapshots.</b> Scrubbing back and forth
/// then touches nothing but a list — the engine is run once, forwards, exactly as it
/// would be anywhere else. Replaying by re-running would be the alternative and it would
/// be wrong: <c>IRandomSource</c> is consumed as the fight goes, so a second pass is a
/// different fight.
/// </remarks>
public partial class WatchMode : FightScreen
{
    private readonly List<Snapshot> _snapshots = [];

    private IReadOnlyList<CombatStep> _log = [];
    private int _index;
    private double _elapsed;
    private bool _playing = true;
    private string _subtitle = string.Empty;

    protected override string Title => "SRD_Combat — watching a fight";

    /// <summary>The fight at the end of one turn, and how much of the log had been written.</summary>
    private sealed record Snapshot(int Round, string? ActiveId, IReadOnlyList<Token> Tokens, int LogCount);

    protected override void OnReady()
    {
        int seed;

        try
        {
            seed = SeedArgument();
        }
        catch (ScenarioRefusedException refusal)
        {
            // The same shape Resolve's own catch (below) leaves for a bad --spawn or
            // --level: no snapshots, the reason in _subtitle, and _Draw's own empty-list
            // case draws the heading alone rather than a blank window (#486).
            _subtitle = refusal.Message;
            return;
        }

        Resolve(seed);

        // A capture run renders one frame and leaves, which is how a change to this
        // screen gets checked without a person watching it.
        if (ArgumentValue("capture") is { } path)
        {
            _playing = false;

            if (_snapshots.Count == 0)
            {
                // The fight never ran — a refused --seed (OnReady, above), or a refused
                // --spawn/--level (Resolve, below, catches it and leaves _snapshots
                // empty). There is nothing to capture, so say why on stdout and exit
                // non-zero rather than either of the two things this used to do: throw
                // out of Math.Clamp(wanted, 0, -1) when --at was given, or fall through
                // to CaptureAndQuit and write a blank PNG while printing "turn -1 of -1"
                // and reporting success (#486).
                GD.Print(_subtitle);
                GetTree().Quit(1);
                return;
            }

            if (!TryParseAt(ArgumentValue("at"), _snapshots.Count - 1, out _index, out var atError))
            {
                // Same shape as the "nothing to capture" refusal above: the reason on
                // stdout, exit non-zero, no PNG written — never the silent "last turn"
                // fallback or the silent clamp into range this used to do (#489).
                GD.Print(atError);
                GetTree().Quit(1);
                return;
            }

            CaptureAndQuit(path);
        }
    }

    /// <summary>
    /// The pure half of <c>--at</c>: given its already-read value (or null when absent)
    /// and the highest snapshot index this run resolved, decides which turn to capture or
    /// refuses. Split from <see cref="OnReady"/> the same way
    /// <see cref="FightScreen.TryParseSeed"/> is split from reading <c>--seed</c> (#476's
    /// pattern): <c>ArgumentValue</c> reaches into Godot's <c>OS</c> singleton and cannot
    /// run under a plain xUnit test, while everything below this line is ordinary
    /// <c>int.TryParse</c> and a range check.
    /// </summary>
    internal static bool TryParseAt(string? text, int maxIndex, out int index, out string? error)
    {
        if (text is null)
        {
            index = maxIndex;
            error = null;
            return true;
        }

        if (!int.TryParse(text, out var wanted))
        {
            index = default;
            error = $"--at=\"{text}\": not a whole number (0-{maxIndex})";
            return false;
        }

        if (wanted < 0 || wanted > maxIndex)
        {
            index = default;
            error = $"--at={wanted}: out of range (0-{maxIndex})";
            return false;
        }

        index = wanted;
        error = null;
        return true;
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
        Fight fight;
        IReadOnlyList<string> notices;

        try
        {
            fight = ResolveFight(seed, out notices);
        }
        catch (ScenarioRefusedException refusal)
        {
            // No snapshots and the reason in _subtitle. _Draw used to return before
            // DrawHeading on an empty _snapshots list, so this comment's old claim —
            // "the draw path already guards the empty list, so the refusal is the
            // whole screen" — was backwards: that guard was what suppressed the
            // refusal, leaving an empty window with no reason given (#486). _Draw now
            // special-cases this case to draw the heading alone.
            _subtitle = refusal.Message;
            return;
        }
        var encounter = fight.Encounter;
        var labels = Labels.For(encounter.Combatants);

        _log = encounter.Log;
        AdoptBattlefield(encounter);
        _subtitle = $"seed {seed} — the party against {RosterOf(fight)}" + NoticeSuffix(notices);

        Capture(encounter, labels);

        while (!encounter.IsComplete && encounter.Round <= 50)
        {
            SimpleTacticsPolicy.TakeTurn(encounter);
            Capture(encounter, labels);
        }
    }

    private void Capture(Encounter encounter, Labels labels) =>
        _snapshots.Add(new Snapshot(
            Math.Max(1, encounter.Round),
            encounter.ActiveCombatant?.Id,
            TokensFrom(encounter, labels),
            encounter.Log.Count));

    public override void _Process(double delta)
    {
        // The idle loop ticks even when playback is paused: a paused fight is a frozen
        // moment, not frozen people.
        if (AdvanceSpriteAnimation(delta))
        {
            QueueRedraw();
        }

        // The walk or swing keeps playing even when paused, so a token never freezes
        // mid-stride or mid-blow.
        if (AdvanceActs(delta))
        {
            QueueRedraw();
        }

        // The camera glides after the fight, playing or paused alike — a scrubbed-to
        // moment is still framed like one.
        if (AdvanceCamera(delta))
        {
            QueueRedraw();
        }

        if (!_playing || _index >= _snapshots.Count - 1 || ActInProgress)
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

        // The turn just revealed may have walked or swung; play the route its Move
        // step recorded and the attacks it landed rather than teleporting the token to
        // the snapshot's square.
        QueueActs(_log, _snapshots[_index - 1].LogCount, _snapshots[_index].LogCount, _snapshots[_index].Tokens);
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // The mouse is the camera's here — this screen is read-only, so a wheel or a
        // middle-drag is the only thing a mouse can mean.
        if (HandleCameraInput(@event))
        {
            return;
        }

        if (@event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Space:
                _playing = !_playing;
                break;

            // Scrubbing snaps: a walk belongs to playback, and a token still hopping
            // an old route over a hand-picked snapshot would be showing two moments at
            // once. Pausing is different — that hop settles on its own.
            case Key.Right:
                _playing = false;
                _index = Math.Min(_snapshots.Count - 1, _index + 1);
                ClearActs();
                break;
            case Key.Left:
                _playing = false;
                _index = Math.Max(0, _index - 1);
                ClearActs();
                break;
            case Key.Home:
                _playing = false;
                _index = 0;
                ClearActs();
                break;
            case Key.End:
                _playing = false;
                _index = _snapshots.Count - 1;
                ClearActs();
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
            // A refused --seed, --spawn or --level leaves _subtitle holding the reason
            // and no fight to show. Draw the heading alone rather than nothing at all —
            // an empty window with no reason given is exactly the bug this guard used
            // to cause (#486).
            DrawBackdrop();
            DrawHeading(_subtitle, "[esc] quit");
            return;
        }

        var snapshot = _snapshots[_index];

        // The field first, floor to ceiling; the heading and panel float over it.
        DrawBackdrop();
        DrawGrid();
        DrawTokens(WithWalk(snapshot.Tokens), snapshot.ActiveId);
        DrawHeading(
            _subtitle,
            $"round {snapshot.Round}   turn {_index} of {_snapshots.Count - 1}   " +
            (_playing ? "playing" : "paused")
            + "   [space] play/pause  [←/→] step  [wheel/middle-drag] camera  [esc] quit");
        DrawTurnOrder(snapshot.Tokens, snapshot.ActiveId);
        DrawLog(_log, snapshot.LogCount, _snapshots[0].Tokens.Count);
    }

    private async void CaptureAndQuit(string path)
    {
        await CaptureFrame(path);
        GD.Print($"turn {_index} of {_snapshots.Count - 1}");
        GetTree().Quit();
    }
}
