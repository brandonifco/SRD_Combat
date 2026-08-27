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

        if (watch)
        {
            AddChild(new WatchMode());
            return;
        }

        if (FightScreen.HasArgument("create"))
        {
            AddChild(NewCreateMode());
            return;
        }

        AddChild(new PlayMode());
    }

    /// <summary>
    /// The completion callback <c>CreateMode</c> used to hard-code as
    /// <c>new PlayMode { CreatedDrafts = … }</c> (#327 S7) — this is the one caller
    /// that starts a run from the finished drafts; a future caller (#483's party
    /// editor) supplies its own.
    /// </summary>
    private CreateMode NewCreateMode()
    {
        CreateMode createMode = null!;

        createMode = new CreateMode
        {
            OnComplete = drafts =>
            {
                AddChild(new PlayMode { CreatedDrafts = drafts });
                createMode.QueueFree();
            },
        };

        return createMode;
    }
}
