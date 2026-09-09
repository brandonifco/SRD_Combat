using Godot;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The pure half of #705's <c>CaptureFrame</c> fix: whether a completed
/// <c>Image.SavePng</c> call should make its caller throw, and what message it should
/// carry. <c>Image.SavePng</c> itself needs a live Godot engine and cannot be called
/// from xUnit — constructing an <see cref="Image"/> outside a running engine terminates
/// the test host rather than throwing (this project's own csproj comment) — but <see
/// cref="Error"/> is a plain enum, not a handle onto a native object, so the *decision*
/// built on its result is reachable here without any engine at all.
/// </summary>
public class CaptureOutcomeTests
{
    [Fact]
    public void OkProducesNoFailureMessage()
    {
        Assert.Null(CaptureOutcome.FailureMessage("run-0-interlude.png", Error.Ok));
    }

    [Fact]
    public void AnyOtherErrorProducesAMessageNamingBothThePathAndTheError()
    {
        var message = CaptureOutcome.FailureMessage("/no/such/dir/run-0-interlude.png", Error.FileCantWrite);

        Assert.NotNull(message);
        Assert.Contains("/no/such/dir/run-0-interlude.png", message);
        Assert.Contains("FileCantWrite", message);
    }
}
