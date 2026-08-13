using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// Chooses the screen. Playing is the default — the phase is named for it — and
/// <c>--watch</c> keeps the read-only screen; <c>--capture</c> implies it, because a
/// capture of a fight nobody is playing is the watch screen's job. (<c>--probe</c> is
/// the play screen's own verification flag.)
/// </summary>
public partial class Main : Node2D
{
    public override void _Ready()
    {
        var watch = FightScreen.HasArgument("watch") || FightScreen.ArgumentValue("capture") is not null;

        AddChild(watch ? new WatchMode() : new PlayMode());
    }
}
