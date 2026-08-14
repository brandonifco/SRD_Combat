using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// Chooses the screen. Playing is the default — the phase is named for it — and
/// <c>--watch</c> keeps the read-only screen; <c>--capture</c> implies it, because a
/// capture of a fight nobody is playing is the watch screen's job. <c>--create</c>
/// builds the party first and hands its drafts to the play screen. (<c>--probe</c> is
/// each screen's own verification flag.)
/// </summary>
public partial class Main : Node2D
{
    public override void _Ready()
    {
        var watch = FightScreen.HasArgument("watch") || FightScreen.ArgumentValue("capture") is not null;

        AddChild(
            watch ? new WatchMode()
            : FightScreen.HasArgument("create") ? new CreateMode()
            : new PlayMode());
    }
}
