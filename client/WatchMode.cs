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
        var seed = SeedArgument();

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
        var fight = ResolveFight(seed);
        var encounter = fight.Encounter;
        var labels = Labels.For(encounter.Combatants);

        _log = encounter.Log;
        AdoptBattlefield(encounter);
        _subtitle = $"seed {seed} — the party against {RosterOf(fight)}";

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

        // The walk keeps playing even when paused, so a token never freezes mid-hop.
        if (AdvanceWalks(delta))
        {
            QueueRedraw();
        }

        if (!_playing || _index >= _snapshots.Count - 1 || WalkInProgress)
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

        // The turn just revealed may have walked somebody; play the route its Move
        // step recorded rather than teleporting the token to the snapshot's square.
        QueueWalks(_log, _snapshots[_index - 1].LogCount, _snapshots[_index].LogCount);
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

            // Scrubbing snaps: a walk belongs to playback, and a token still hopping
            // an old route over a hand-picked snapshot would be showing two moments at
            // once. Pausing is different — that hop settles on its own.
            case Key.Right:
                _playing = false;
                _index = Math.Min(_snapshots.Count - 1, _index + 1);
                ClearWalks();
                break;
            case Key.Left:
                _playing = false;
                _index = Math.Max(0, _index - 1);
                ClearWalks();
                break;
            case Key.Home:
                _playing = false;
                _index = 0;
                ClearWalks();
                break;
            case Key.End:
                _playing = false;
                _index = _snapshots.Count - 1;
                ClearWalks();
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

        DrawChrome(
            _subtitle,
            $"round {snapshot.Round}   turn {_index} of {_snapshots.Count - 1}   " +
            (_playing ? "playing" : "paused") + "   [space] play/pause  [←/→] step  [esc] quit");

        DrawGrid();
        DrawTokens(WithWalk(snapshot.Tokens), snapshot.ActiveId);
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
