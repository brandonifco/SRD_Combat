using SRDCombat.Game;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The client's whole flag-surface sweep (#489, #488): <c>FightScreen.TryParseSeed</c>,
/// <c>WatchMode.TryParseAt</c> and <c>PlayMode.TryResolveGauntletLevel</c> are each the
/// pure half of a flag whose Godot-reading half (<c>ArgumentValue</c>/<c>HasArgument</c>)
/// cannot run under a plain xUnit test — split the same way
/// <c>FightScreen.ScenarioFromFile</c> is split from reading <c>--scenario</c> (#476;
/// see <c>ScenarioFromFileTests</c>'s own remarks for why). Each one used to fall
/// through to a silent default or a silent clamp on a bad value; each now refuses,
/// naming the flag, the value and the accepted set.
/// </summary>
/// <remarks>
/// This is also the first test to construct anything from <c>PlayMode</c> or
/// <c>WatchMode</c> — both are <c>Node</c>-derived, and per this project's own stated
/// boundary (see this project's <c>.csproj</c>) only their *static* members are
/// reachable without a live scene. It closes the pure-logic half of #490's stated gap
/// for these two flags; the live-node half (constructing the screen, calling
/// <c>OnReady</c> itself) stays probe-only, exactly as #490 says it does for
/// <c>PlayMode</c> as a whole.
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
}
