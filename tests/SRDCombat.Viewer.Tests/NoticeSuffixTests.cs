namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="FightScreen.NoticeSuffix"/> — the heading-formatting half of a resolved
/// fight's notices, kept in the client because it is display trimming, not a
/// composition decision. Split out of the former <c>ScenarioFromFileTests</c> when
/// #490a moved the <c>--scenario</c>/<c>--spawn</c> composition and its refusal
/// wording into <c>SRDCombat.Game.ScenarioComposition</c>
/// (<c>SRDCombat.Game.Tests.ScenarioCompositionTests</c>) — this method itself did not
/// move, so its tests stay here rather than following.
/// </summary>
public sealed class NoticeSuffixTests
{
    [Fact]
    public void NoNotices_IsEmpty() =>
        Assert.Equal(string.Empty, FightScreen.NoticeSuffix([]));

    [Fact]
    public void JoinsAndDashesAShortNotice() =>
        Assert.Equal(" — a short notice", FightScreen.NoticeSuffix(["a short notice"]));

    [Fact]
    public void TrimsALongNoticeRatherThanOverrunningTheHeading()
    {
        var suffix = FightScreen.NoticeSuffix([new string('x', 200)]);

        // " — " (3) + Chrome.Trim's own width (100), the width every other single-line
        // heading fragment in this client is trimmed to.
        Assert.Equal(103, suffix.Length);
        Assert.EndsWith("…", suffix, StringComparison.Ordinal);
    }
}
