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

    /// <summary>
    /// The seam version of the PR's own live demonstration: a directory made
    /// genuinely unwritable (<c>chmod 555</c>, the same mode the live demonstration
    /// uses), a real write attempted into it — not a canned <see cref="Error"/> value
    /// asserted by fiat — and the resulting OS refusal fed through the same decision
    /// <c>CaptureFrame</c>'s own failed <c>Image.SavePng</c> would reach. Pins the
    /// print-and-continue regression without a display or a live Godot engine: this
    /// is the plain-value half of the demonstration, always runnable; the live probe
    /// run (paste in the PR) is what proves the Godot side of the same path.
    /// </summary>
    // UnixFileMode's setter is analyzer-flagged as unsupported on Windows (CA1416).
    // This project's own environment is Linux-only (CLAUDE.md, "Environment") — the
    // whole test suite, this one included, never runs anywhere else.
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [Fact]
    public void AttemptingToWriteIntoAnUnwritableDirectoryFeedsARealFailureThroughTheSameDecision()
    {
        var directory = Directory.CreateTempSubdirectory("probe-unwritable-");

        try
        {
            var path = Path.Combine(directory.FullName, "run-0-interlude.png");

            directory.UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

            var thrown = Record.Exception(() => File.WriteAllBytes(path, [0]));

            if (thrown is null)
            {
                // Root, or a filesystem that does not enforce the mode bit, would not
                // refuse this write — nothing real to feed through in that case. Made
                // explicit rather than a silent return (#719, fifth review): a bare
                // `return` here reads identically to "every assertion below passed",
                // which none of them ran to prove. Xunit.Sdk.SkipException.ForSkip is
                // xunit 2.9.3's own dynamic-skip primitive, but verified (TRX output)
                // to report as Failed rather than Skipped under this project's actual
                // pinned pair — xunit 2.9.3 core with xunit.runner.visualstudio 3.1.5,
                // built for xunit v3's protocol — so a real Skip never reaches the
                // gate as anything but green-if-silent. Assert.Fail is the mechanism
                // that is actually honest here: loud and explicit, never a silent
                // pass, on a runner where a true Skip is not available.
                Assert.Fail(
                    "the write into a chmod-555 directory unexpectedly succeeded — running as root, "
                        + "or on a filesystem that does not enforce the mode bit.");
            }

            // The real failure this write hit, specifically — not any exception at all
            // (#719, fifth review): a coincidental, unrelated throw must not be mistaken
            // for the permission refusal this test exists to demonstrate.
            Assert.True(
                thrown is UnauthorizedAccessException or IOException,
                $"expected the write to fail with a permission error (UnauthorizedAccessException or "
                    + $"IOException), got {thrown.GetType().Name}: {thrown.Message}");
            Assert.Contains(path, thrown.Message);

            // Image.SavePng reports exactly Error.FileCantOpen for this shape —
            // confirmed live in the PR's own negative demonstration against a
            // chmod-555 directory. Decided once, here, rather than assumed at
            // every call site.
            var message = CaptureOutcome.FailureMessage(path, Error.FileCantOpen);

            Assert.NotNull(message);
            Assert.Contains(path, message);
            Assert.Contains("FileCantOpen", message);
        }
        finally
        {
            directory.UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            directory.Delete(recursive: true);
        }
    }
}
