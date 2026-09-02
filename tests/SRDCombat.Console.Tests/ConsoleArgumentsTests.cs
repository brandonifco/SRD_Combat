using SRDCombat.Core.Rules;

namespace SRDCombat.Console.Tests;

/// <summary>
/// <see cref="ConsoleArguments"/> (#489): a present-but-unusable <c>--level</c>,
/// <c>--seed</c> or <c>--difficulty</c> is refused by name and value, never defaulted
/// or clamped — the console-dialect twin of <c>ScenarioArguments.TryParseLevel</c>
/// (<c>SRDCombat.Game.Tests</c>) and <c>FightScreen.TryParseSeed</c>
/// (<c>SRDCombat.Viewer.Tests</c>).
/// </summary>
public class ConsoleArgumentsTests
{
    [Fact]
    public void AbsentLevelDefaultsToOne()
    {
        var ok = ConsoleArguments.TryParseLevel([], out var level, out var error);

        Assert.True(ok);
        Assert.Equal(1, level);
        Assert.Null(error);
    }

    [Fact]
    public void APresentButValuelessLevelIsRefusedRatherThanDefaulted()
    {
        var ok = ConsoleArguments.TryParseLevel(["--level"], out _, out var error);

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
        var ok = ConsoleArguments.TryParseLevel(["--level", text], out var level, out var error);

        Assert.True(ok);
        Assert.Equal(int.Parse(text), level);
        Assert.Null(error);
    }

    [Fact]
    public void ANonNumericLevelIsRefusedRatherThanDefaulted()
    {
        var ok = ConsoleArguments.TryParseLevel(["--level", "abc"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level abc", error);
        Assert.Contains("1-5", error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    public void AnOutOfRangeLevelIsRefusedRatherThanClamped(string text)
    {
        var ok = ConsoleArguments.TryParseLevel(["--level", text], out _, out var error);

        Assert.False(ok);
        Assert.Contains($"--level {text}", error);
        Assert.Contains("1-5", error);
    }

    [Fact]
    public void AbsentSeedIsNullRatherThanRefused()
    {
        var ok = ConsoleArguments.TryParseSeed([], out var seed, out var error);

        Assert.True(ok);
        Assert.Null(seed);
        Assert.Null(error);
    }

    [Fact]
    public void APresentButValuelessSeedIsRefusedRatherThanRolledRandomly()
    {
        var ok = ConsoleArguments.TryParseSeed(["--seed"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--seed", error);
        Assert.Contains("no value given", error);
    }

    [Fact]
    public void ANumericSeedParses()
    {
        var ok = ConsoleArguments.TryParseSeed(["--seed", "12345"], out var seed, out var error);

        Assert.True(ok);
        Assert.Equal(12345, seed);
        Assert.Null(error);
    }

    [Fact]
    public void ANonNumericSeedIsRefusedRatherThanRolledRandomly()
    {
        var ok = ConsoleArguments.TryParseSeed(["--seed", "abc"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--seed abc", error);
        Assert.Contains("not a whole number", error);
    }

    [Fact]
    public void AbsentDifficultyDefaultsToLow()
    {
        var ok = ConsoleArguments.TryParseDifficulty([], out var difficulty, out var error);

        Assert.True(ok);
        Assert.Equal(EncounterDifficulty.Low, difficulty);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("low", EncounterDifficulty.Low)]
    [InlineData("MODERATE", EncounterDifficulty.Moderate)]
    [InlineData("High", EncounterDifficulty.High)]
    public void EveryRecognisedDifficultyParsesCaseInsensitively(string text, EncounterDifficulty expected)
    {
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty", text], out var difficulty, out var error);

        Assert.True(ok);
        Assert.Equal(expected, difficulty);
        Assert.Null(error);
    }

    [Fact]
    public void AnUnrecognisedDifficultyIsRefusedRatherThanDefaulted()
    {
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty", "extreme"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--difficulty extreme", error);
        Assert.Contains("low, moderate, high", error);
    }

    [Fact]
    public void APresentButValuelessDifficultyIsRefusedRatherThanDefaulted()
    {
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--difficulty", error);
        Assert.Contains("no value given", error);
    }

    // ---- Numeric --difficulty (#602) ----

    /// <summary>
    /// <c>Enum.TryParse</c> alone accepts numeric text for any enum, defined or not: "3"
    /// used to parse as the undefined <c>EncounterDifficulty</c> value 3 (which throws
    /// downstream at the engine, not here, so the refusal never named the flag) and "0"
    /// used to silently parse as <c>Low</c> — a coincidence of <c>Low</c> sitting at
    /// ordinal 0, not a value anyone typed. Only an exact declared name is accepted now.
    /// </summary>
    [Theory]
    [InlineData("3")]
    [InlineData("99")]
    [InlineData("-1")]
    public void AnUndefinedNumericDifficultyIsRefusedRatherThanThrowingDownstream(string text)
    {
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty", text], out _, out var error);

        Assert.False(ok);
        Assert.Contains($"--difficulty {text}", error);
        Assert.Contains("low, moderate, high", error);
    }

    [Fact]
    public void ANumericDifficultyMatchingADefinedOrdinalIsRefusedRatherThanAcceptedAsThatValue()
    {
        // "0" is Low's own ordinal — the coincidence this test guards against: numeric
        // text is refused regardless of whether it happens to land on a defined value.
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty", "0"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--difficulty 0", error);
        Assert.Contains("low, moderate, high", error);
    }
}
