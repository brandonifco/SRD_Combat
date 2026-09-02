using SRDCombat.Game;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The client's whole flag-surface sweep (#489, #488, #602): <c>FightScreen.TryParseSeed</c>
/// / <c>TryResolveSeed</c>, <c>WatchMode.TryParseAt</c> / <c>TryResolveAt</c> and
/// <c>PlayMode.TryResolveGauntletLevel</c> are each the pure half of a flag whose
/// Godot-reading half (<c>ArgumentValue</c>/<c>HasArgument</c>) cannot run under a plain
/// xUnit test — split the same way <c>FightScreen.ScenarioFromFile</c> is split from
/// reading <c>--scenario</c> (#476; see <c>ScenarioFromFileTests</c>'s own remarks for
/// why). Each one used to fall through to a silent default or a silent clamp on a bad
/// value; each now refuses, naming the flag, the value and the accepted set. The
/// <c>TryResolveSeed</c>/<c>TryResolveAt</c> pair added by #602 closes a narrower gap an
/// independent review found in #489's own sweep: <c>TryParseSeed</c>/<c>TryParseAt</c>
/// only ever saw an already-read value, so a *bare* flag (<c>-- --seed</c>,
/// <c>--capture=out.png --at</c>) reached them as the same <c>null</c> an absent flag
/// would — the two are now told apart the same way <c>PlayMode.TryResolveGauntletLevel</c>
/// already told a bare <c>--level</c> from an absent one.
/// </summary>
/// <remarks>
/// This is also the first test to construct anything from <c>PlayMode</c> or
/// <c>WatchMode</c> — both are <c>Node</c>-derived, and per this project's own stated
/// boundary (see this project's <c>.csproj</c>) only their *static* members are
/// reachable without a live scene. It closes the pure-logic half of #490's stated gap
/// for these two flags; the live-node half (constructing the screen, calling
/// <c>OnReady</c> itself) stays probe-only, exactly as #490 says it does for
/// <c>PlayMode</c> as a whole. The <c>--create --level</c> forwarding guard at the
/// bottom of this class is a source check for the same reason (see its own remarks).
/// </remarks>
public class FlagRefusalTests
{
    // ---- FightScreen.TryParseSeed (#489) ----

    [Fact]
    public void ANumericSeedParses()
    {
        var ok = FightScreen.TryParseSeed("12345", out var seed, out var error);

        Assert.True(ok);
        Assert.Equal(12345, seed);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12.5")]
    [InlineData("")]
    public void ANonNumericSeedIsRefusedRatherThanRolledRandomly(string text)
    {
        var ok = FightScreen.TryParseSeed(text, out _, out var error);

        Assert.False(ok);
        Assert.Contains($"--seed=\"{text}\"", error);
        Assert.Contains("not a whole number", error);
    }

    // ---- FightScreen.TryResolveSeed (#602) ----

    [Fact]
    public void AnAbsentSeedResolvesToNullRatherThanRefusing()
    {
        var ok = FightScreen.TryResolveSeed(given: false, text: null, out var seed, out var error);

        Assert.True(ok);
        Assert.Null(seed);
        Assert.Null(error);
    }

    /// <summary>
    /// <c>-- --seed</c> (present, no value) used to fall through to
    /// <c>ArgumentValue</c> returning <c>null</c> — indistinguishable from the flag
    /// never having been passed — and silently rolled a fresh seed instead of naming the
    /// missing value.
    /// </summary>
    [Fact]
    public void APresentButValuelessSeedIsRefusedRatherThanRolledRandomly()
    {
        var ok = FightScreen.TryResolveSeed(given: true, text: null, out _, out var error);

        Assert.False(ok);
        Assert.Contains("--seed", error);
        Assert.Contains("no value given", error);
    }

    [Fact]
    public void APresentAndNumericSeedResolvesToItsValue()
    {
        var ok = FightScreen.TryResolveSeed(given: true, text: "12345", out var seed, out var error);

        Assert.True(ok);
        Assert.Equal(12345, seed);
        Assert.Null(error);
    }

    [Fact]
    public void APresentAndNonNumericSeedIsRefused()
    {
        var ok = FightScreen.TryResolveSeed(given: true, text: "abc", out _, out var error);

        Assert.False(ok);
        Assert.Contains("--seed=\"abc\"", error);
        Assert.Contains("not a whole number", error);
    }

    // ---- WatchMode.TryParseAt (#489) ----

    [Fact]
    public void AbsentAtDefaultsToTheLastSnapshot()
    {
        var ok = WatchMode.TryParseAt(null, maxIndex: 9, out var index, out var error);

        Assert.True(ok);
        Assert.Equal(9, index);
        Assert.Null(error);
    }

    [Fact]
    public void AnInRangeAtParses()
    {
        var ok = WatchMode.TryParseAt("4", maxIndex: 9, out var index, out var error);

        Assert.True(ok);
        Assert.Equal(4, index);
        Assert.Null(error);
    }

    [Fact]
    public void ANonNumericAtIsRefusedRatherThanReadAsTheLastTurn()
    {
        var ok = WatchMode.TryParseAt("abc", maxIndex: 9, out _, out var error);

        Assert.False(ok);
        Assert.Contains("--at=\"abc\"", error);
        Assert.Contains("0-9", error);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("10")]
    public void AnOutOfRangeAtIsRefusedRatherThanClamped(string text)
    {
        var ok = WatchMode.TryParseAt(text, maxIndex: 9, out _, out var error);

        Assert.False(ok);
        Assert.Contains($"--at={text}", error);
        Assert.Contains("0-9", error);
    }

    // ---- WatchMode.TryResolveAt (#602) ----

    [Fact]
    public void AnAbsentAtStillDefaultsToTheLastSnapshot()
    {
        var ok = WatchMode.TryResolveAt(given: false, text: null, maxIndex: 9, out var index, out var error);

        Assert.True(ok);
        Assert.Equal(9, index);
        Assert.Null(error);
    }

    /// <summary>
    /// <c>--capture=out.png --at</c> (present, no value) used to be read the same as
    /// <c>--at</c> never having been passed at all, silently capturing the last snapshot
    /// instead of naming the missing value.
    /// </summary>
    [Fact]
    public void APresentButValuelessAtIsRefusedRatherThanDefaultingToTheLastSnapshot()
    {
        var ok = WatchMode.TryResolveAt(given: true, text: null, maxIndex: 9, out _, out var error);

        Assert.False(ok);
        Assert.Contains("--at", error);
        Assert.Contains("no value given", error);
    }

    [Fact]
    public void APresentAndInRangeAtResolvesToIt()
    {
        var ok = WatchMode.TryResolveAt(given: true, text: "4", maxIndex: 9, out var index, out var error);

        Assert.True(ok);
        Assert.Equal(4, index);
        Assert.Null(error);
    }

    [Fact]
    public void APresentAndNonNumericAtIsRefused()
    {
        var ok = WatchMode.TryResolveAt(given: true, text: "abc", maxIndex: 9, out _, out var error);

        Assert.False(ok);
        Assert.Contains("--at=\"abc\"", error);
        Assert.Contains("0-9", error);
    }

    // ---- PlayMode.TryResolveGauntletLevel (#488) ----

    [Fact]
    public void AFreshRunWithNoLevelDefaultsToOne()
    {
        var ok = PlayMode.TryResolveGauntletLevel(
            continuing: false, levelGiven: false, levelText: null, out var level, out var error);

        Assert.True(ok);
        Assert.Equal(1, level);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    public void AFreshRunWithAnInRangeLevelParsesIt(string text)
    {
        var ok = PlayMode.TryResolveGauntletLevel(
            continuing: false, levelGiven: true, levelText: text, out var level, out var error);

        Assert.True(ok);
        Assert.Equal(int.Parse(text), level);
        Assert.Null(error);
    }

    [Fact]
    public void AFreshRunWithANonNumericLevelIsRefusedRatherThanDefaulted()
    {
        var ok = PlayMode.TryResolveGauntletLevel(
            continuing: false, levelGiven: true, levelText: "x", out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level refused", error);
        Assert.Contains("1-5", error);
    }

    [Fact]
    public void AFreshRunWithAnOutOfRangeLevelIsRefusedRatherThanClamped()
    {
        var ok = PlayMode.TryResolveGauntletLevel(
            continuing: false, levelGiven: true, levelText: "9", out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level refused", error);
        Assert.Contains("1-5", error);
    }

    [Fact]
    public void APresentButValuelessLevelOnAFreshRunIsRefused()
    {
        var ok = PlayMode.TryResolveGauntletLevel(
            continuing: false, levelGiven: true, levelText: null, out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level refused", error);
        Assert.Contains("no value given", error);
    }

    [Fact]
    public void ContinuingWithNoLevelSucceedsAndTheLevelIsUnused()
    {
        var ok = PlayMode.TryResolveGauntletLevel(
            continuing: true, levelGiven: false, levelText: null, out _, out var error);

        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public void ContinuingWithALevelIsRefusedRatherThanSilentlyIgnored()
    {
        var ok = PlayMode.TryResolveGauntletLevel(
            continuing: true, levelGiven: true, levelText: "4", out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level refused", error);
        Assert.Contains("--continue", error);
    }

    // ---- PlayMode's --create --level forwarding (#488, #602) ----

    /// <summary>
    /// <see cref="TryResolveGauntletLevel"/> above pins the flag being parsed into the
    /// right <c>level</c> value; nothing pinned that value actually reaching
    /// <c>GauntletRun.Start</c>'s <c>startingLevel</c> parameter on the
    /// <c>CreatedDrafts</c> branch — <c>--create --level=4</c> used to start at 1 with
    /// nothing said (#488's own bug). <c>PlayMode</c> is a live <c>Node</c> and
    /// <c>OnReady</c> cannot run under xUnit (this class's own remarks), so there is no
    /// way to call the real branch and observe the level it resolves to; the seam is a
    /// source check instead, the same shape <c>FightSeamTests</c> uses for its own
    /// reach-past-the-seam guard. It fails the instant this exact call stops appearing
    /// in <c>PlayMode.cs</c> — whether the <c>startingLevel:</c> argument is dropped, or
    /// <c>level</c> is swapped for a literal.
    /// </summary>
    [Fact]
    public void CreatedDraftsRunStartForwardsTheResolvedLevelRatherThanDefaultingToOne()
    {
        var source = File.ReadAllText(Path.Combine(ViewerRepositoryPaths.ClientSourceDirectory, "PlayMode.cs"));

        Assert.Contains(
            "GauntletRun.Start(content, CreatedDrafts, seed: _seed, startingLevel: level)",
            source,
            StringComparison.Ordinal);
    }
}
