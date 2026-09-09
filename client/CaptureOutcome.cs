using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// The pure half of a capture (#705): whether <c>Image.SavePng</c> succeeded, and what
/// to say when it did not. <c>FightScreen.CaptureFrame</c> and
/// <c>CreateMode.CaptureFrame</c> used to print either line and return either way — a
/// probe run whose output directory could not be written (missing, unwritable, disk
/// full) still exited 0, with every step "succeeding" and no PNG on disk to show for
/// it, because nothing downstream of <c>SavePng</c> ever looked at its <see
/// cref="Error"/>. Both call sites now throw on failure instead, which
/// <c>ProbeFaults.FireAndObserve</c> turns into a crashed probe and a non-zero exit —
/// the same path a thrown assertion inside a probe step already takes.
/// </summary>
/// <remarks>
/// <c>Image.SavePng</c> itself needs a live Godot engine and cannot be called from
/// xUnit (constructing an <c>Image</c> outside a running engine terminates the test
/// host rather than throwing — see <c>tests/SRDCombat.Viewer.Tests</c>'s own project
/// comment). <see cref="Error"/> is a plain enum, though, not a handle onto a native
/// object, so the *decision* of what a given result means is split out here where a
/// plain xUnit test can pin it without any engine at all.
/// </remarks>
internal static class CaptureOutcome
{
    /// <summary>
    /// The message a failed capture should throw, or null when <paramref name="error"/>
    /// reports success and the caller should proceed to print its own "captured to"
    /// line as before.
    /// </summary>
    internal static string? FailureMessage(string path, Error error) =>
        error == Error.Ok ? null : $"probe: could not save capture '{path}': {error}";
}
