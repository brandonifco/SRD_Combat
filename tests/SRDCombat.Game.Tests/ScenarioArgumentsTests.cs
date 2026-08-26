namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="ScenarioArguments.TryParseLevel"/> (#463): a bad <c>--level</c> is
/// refused by name and range, never defaulted or clamped past a typo.
/// </summary>
public class ScenarioArgumentsTests
{
    [Fact]
    public void AbsentLevelDefaultsToThree()
    {
        var ok = ScenarioArguments.TryParseLevel(null, out var level, out var error);

        Assert.True(ok);
        Assert.Equal(3, level);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    [InlineData("3")]
    public void EveryLevelInRangeParses(string text)
    {
        var ok = ScenarioArguments.TryParseLevel(text, out var level, out var error);

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
        var ok = ScenarioArguments.TryParseLevel(text, out _, out var error);

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
        var ok = ScenarioArguments.TryParseLevel(text, out _, out var error);

        Assert.False(ok);
        Assert.Contains($"--level={text}", error);
        Assert.Contains("1-5", error);
    }
}
