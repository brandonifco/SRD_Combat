namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The pure half of #322's fix: three probe methods used to run as <c>async void</c>,
/// so an exception thrown inside them vanished into Godot's own unhandled-exception
/// path (logged to stderr, process still exits 0) instead of failing the probe. They
/// are <c>async Task</c> now, observed at their one fire-and-forget call site by
/// <c>ProbeFaults.FireAndObserve</c>, which turns a faulted task into
/// <c>GetTree().Quit(1)</c>. That exit call needs a live <c>SceneTree</c> and is
/// probe-only (demonstrated live for the PR, the same shape #490 describes for the
/// argv boundary) — what a plain xUnit test *can* pin, with no Godot scene at all, is
/// <see cref="ProbeFaults.FaultMessage"/>: the question of whether a completed task
/// faulted, answered from a bare <see cref="Task"/>, which is not a Godot type.
/// </summary>
public class ProbeFaultsTests
{
    [Fact]
    public async Task ACompletedTaskWithNoExceptionHasNoFaultMessage()
    {
        var task = Task.CompletedTask;
        await task;

        Assert.Null(ProbeFaults.FaultMessage(task));
    }

    [Fact]
    public async Task AFaultedTaskProducesAMessageNamingTheException()
    {
        var task = Task.Run(() => throw new InvalidOperationException("probe demo throw"));

        // Swallow the fault here the way a real ContinueWith observer does — otherwise
        // xUnit's own unobserved-task-exception reporting would flag this deliberately
        // faulted task as a second, unrelated problem.
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);

        var message = ProbeFaults.FaultMessage(task);

        Assert.NotNull(message);
        Assert.Contains("probe demo throw", message);
    }
}
