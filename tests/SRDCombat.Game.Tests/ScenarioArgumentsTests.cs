namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="ScenarioArguments.TryParseLevel"/> (#463, #470): a bad <c>--level</c> is
/// refused by name and range, never defaulted or clamped past a typo — and a
/// present-but-valueless <c>--level</c> (a bare flag, or the console's unsupported
/// space form) is refused rather than silently read as absent and defaulted to 3.
/// </summary>
public class ScenarioArgumentsTests
{
    [Fact]
    public void AbsentLevelDefaultsToThree()
    {
        var ok = ScenarioArguments.TryParseLevel(null, present: false, out var level, out var error);

        Assert.True(ok);
        Assert.Equal(3, level);
        Assert.Null(error);
    }

    [Fact]
    public void APresentButValuelessLevelIsRefusedRatherThanDefaulted()
    {
        var ok = ScenarioArguments.TryParseLevel(null, present: true, out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level", error);
        Assert.Contains("no value given", error);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    [InlineData("3")]
    public void EveryLevelInRangeParses(string text)
    {
        var ok = ScenarioArguments.TryParseLevel(text, present: true, out var level, out var error);

        Assert.True(ok);
        Assert.Equal(int.Parse(text), level);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("3.5")]
    [InlineData("")]
    [InlineData(" ")]
    public void ANonNumericLevelIsRefusedRatherThanDefaulted(string text)
    {
        var ok = ScenarioArguments.TryParseLevel(text, present: true, out _, out var error);

        Assert.False(ok);
        Assert.Contains($"--level=\"{text}\"", error);
        Assert.Contains("1-5", error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("9")]
    [InlineData("-1")]
    public void AnOutOfRangeLevelIsRefusedRatherThanClamped(string text)
    {
        var ok = ScenarioArguments.TryParseLevel(text, present: true, out _, out var error);

        Assert.False(ok);
        Assert.Contains($"--level={text}", error);
        Assert.Contains("1-5", error);
    }
}
