using System.Threading;
using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// Turns a probe's fire-and-forget task into a loud, non-zero exit on fault, instead of
/// the vanishing exception an <c>async void</c> probe method used to produce (#322):
/// Godot's own unhandled-exception path logs to stderr but does not fail the process, so
/// a throw inside <c>PlayMode.RunProbe</c>, <c>CreateMode.RunProbe</c> or
/// <c>WatchMode.CaptureAndQuit</c> used to disappear and the probe still reported
/// success (exit 0) — or, if nothing downstream of the throw ever called
/// <c>GetTree().Quit()</c>, hung forever instead.
/// </summary>
/// <remarks>
/// The three call sites are synchronous Godot lifecycle points (<c>RunProbeIfAsked</c>,
/// <c>OnReady</c>) that cannot themselves be made <c>async</c> without changing what
/// calls them, so the probe method is still started fire-and-forget — the difference is
/// that its <see cref="Task"/> is now observed rather than dropped.
/// <see cref="FaultMessage"/> is split out as the pure half: whether a completed task
/// faulted and what to print, reachable from a plain xUnit test without a live scene
/// (<c>ProbeFaultsTests</c>); the actual exit (<see cref="ReportFault"/>) needs a real
/// <c>SceneTree</c> and stays probe-only, the same shape #490 describes for the argv
/// boundary.
/// </remarks>
/// <remarks>
/// <b>The <see cref="SceneTree"/> is fetched on the caller's thread, never inside the
/// continuation.</b> Godot's scene tree is not thread-safe
/// (docs.godotengine.org/en/stable/tutorials/performance/thread_safe_apis.html), and
/// <c>TaskScheduler.Default</c> runs the fault continuation on a thread-pool thread — a
/// <c>GetTree()</c> call made there, rather than here, could itself race scene teardown.
/// <c>FireAndObserve</c> runs synchronously on the caller's own thread (the Godot main
/// thread, since every call site is a lifecycle method), so <c>node.GetTree()</c> happens
/// there and the tree reference alone crosses into the continuation.
/// <see cref="SceneTree.CallDeferred(StringName, Variant[])"/> itself is the one part of
/// this that is safe to call from any thread — it queues the call rather than running it
/// — which is why only the lookup, not the deferred call, needed moving.
/// </remarks>
/// <remarks>
/// <b>The exit path also cannot be allowed to fail silently.</b> If <c>CallDeferred</c>
/// itself throws — a torn-down tree, some other race this class did not anticipate — the
/// catch below falls back to <see cref="System.Environment.Exit(int)"/>: an abrupt process exit is
/// preferable, for a headless test instrument, to reintroducing the exact indefinite hang
/// this class exists to close. The happy path (no fault) never reaches either branch.
/// </remarks>
internal static class ProbeFaults
{
    internal static void FireAndObserve(this Node node, Task probe)
    {
        // Captured here, on the caller's thread, per the remarks above — never inside
        // ReportFault, which runs on a thread-pool thread once the task completes.
        var tree = node.GetTree();
        _ = probe.ContinueWith(t => ReportFault(tree, t), TaskScheduler.Default);
    }

    private static void ReportFault(SceneTree tree, Task probe)
    {
        if (FaultMessage(probe) is not { } message)
        {
            return;
        }

        try
        {
            GD.PrintErr(message);
            tree.CallDeferred(SceneTree.MethodName.Quit, 1);
        }
        catch (Exception ex)
        {
            // Belt-and-suspenders (see the class remarks): a probe must never hang.
            // Environment.Exit does not unwind, so the reason is printed first — this
            // is the only place in this class that can still see it.
            GD.PrintErr($"probe: fault-reporting itself failed — {ex}");
            System.Environment.Exit(1);
        }
    }

    /// <summary>
    /// The pure half (#322): a faulted task's message to print, or <c>null</c> when it
    /// completed without one. Named rather than a bare <c>bool</c> so the printed line
    /// and the "did it fault" question stay one fact instead of two that could drift.
    /// </summary>
    internal static string? FaultMessage(Task probe) =>
        probe.Exception is { } aggregate ? $"probe: crashed — {aggregate.GetBaseException()}" : null;
}
