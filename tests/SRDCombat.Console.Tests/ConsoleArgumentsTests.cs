using SRDCombat.Core.Rules;

namespace SRDCombat.Console.Tests;

/// <summary>
/// <see cref="ConsoleArguments"/> (#489): a present-but-unusable <c>--level</c>,
/// <c>--seed</c> or <c>--difficulty</c> is refused by name and value, never defaulted
/// or clamped — the console-dialect twin of <c>ScenarioArguments.TryParseLevel</c>
/// (<c>SRDCombat.Game.Tests</c>) and <c>FightScreen.TryParseSeed</c>
/// (<c>SRDCombat.Viewer.Tests</c>). #602 closed two narrower gaps an independent review
/// found in #489's own sweep: <c>--difficulty</c> accepted more than a single declared
/// name (numeric text, and a comma-separated list read as a bitwise-OR combination —
/// both <c>Enum.TryParse</c> behaviours nothing here guarded against), and
/// <c>Program.cs</c>'s <c>--create</c> path never forwarded a parsed <c>--level</c> into
/// the run it started at all — the console twin of #488's Godot bug, closed here by
/// <see cref="ConsoleArguments.TryResolveGauntletLevel"/> and a source check on the
/// forwarding call site, the same shape used for the Godot side in
/// <c>FlagRefusalTests</c>.
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

    // ---- Comma-separated --difficulty (#602) ----

    /// <summary>
    /// <c>Enum.TryParse</c> also reads a comma-separated name list as a bitwise-OR
    /// combination of flags enum values — <c>"low,high"</c> used to parse to the
    /// perfectly defined value <c>High</c> even though nobody typed a single name.
    /// <c>EncounterDifficulty</c> carries no <c>[Flags]</c> attribute, but
    /// <c>Enum.TryParse</c> does not require one to accept the comma syntax.
    /// </summary>
    [Theory]
    [InlineData("low,high")]
    [InlineData("Low,High")]
    [InlineData("low, high")]
    [InlineData("low,moderate,high")]
    public void ACommaSeparatedNameListIsRefusedRatherThanAcceptedAsABitwiseOrCombination(string text)
    {
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty", text], out _, out var error);

        Assert.False(ok);
        Assert.Contains($"--difficulty {text}", error);
        Assert.Contains("low, moderate, high", error);
    }

    [Fact]
    public void ASpaceSeparatedNameListIsAlsoRefused()
    {
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty", "low high"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("low, moderate, high", error);
    }

    /// <summary>
    /// The trim half of the direct name-set match: a value typed with incidental
    /// surrounding whitespace (quoted on the shell) still resolves to the name it
    /// names, rather than being refused for whitespace that carries no meaning.
    /// </summary>
    [Fact]
    public void ADifficultyWithSurroundingWhitespaceStillParses()
    {
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty", " Low "], out var difficulty, out var error);

        Assert.True(ok);
        Assert.Equal(EncounterDifficulty.Low, difficulty);
        Assert.Null(error);
    }

    [Fact]
    public void AWhitespaceOnlyDifficultyIsRefused()
    {
        var ok = ConsoleArguments.TryParseDifficulty(["--difficulty", "   "], out _, out var error);

        Assert.False(ok);
        Assert.Contains("not one of low, moderate, high", error);
    }

    // ---- ConsoleArguments.TryResolveGauntletLevel (#602, the console twin of #488) ----

    [Fact]
    public void AFreshGauntletRunWithNoLevelDefaultsToOne()
    {
        var ok = ConsoleArguments.TryResolveGauntletLevel(continuing: false, [], out var level, out var error);

        Assert.True(ok);
        Assert.Equal(1, level);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    [InlineData("4")]
    public void AFreshGauntletRunWithAnInRangeLevelParsesIt(string text)
    {
        var ok = ConsoleArguments.TryResolveGauntletLevel(continuing: false, ["--level", text], out var level, out var error);

        Assert.True(ok);
        Assert.Equal(int.Parse(text), level);
        Assert.Null(error);
    }

    [Fact]
    public void AFreshGauntletRunWithANonNumericLevelIsRefusedRatherThanDefaulted()
    {
        var ok = ConsoleArguments.TryResolveGauntletLevel(continuing: false, ["--level", "x"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level x", error);
        Assert.Contains("1-5", error);
    }

    [Fact]
    public void AFreshGauntletRunWithABareLevelIsRefusedRatherThanDefaulted()
    {
        var ok = ConsoleArguments.TryResolveGauntletLevel(continuing: false, ["--level"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level", error);
        Assert.Contains("no value given", error);
    }

    [Fact]
    public void ContinuingAGauntletRunWithNoLevelSucceedsAndTheLevelIsUnused()
    {
        var ok = ConsoleArguments.TryResolveGauntletLevel(continuing: true, [], out _, out var error);

        Assert.True(ok);
        Assert.Null(error);
    }

    /// <summary>
    /// <c>--continue</c> resumes at the level the save's own experience earned —
    /// <c>--level</c> has nothing to apply to, so it is refused rather than silently
    /// ignored, the same shape <c>PlayMode.TryResolveGauntletLevel</c> holds the Godot
    /// client to.
    /// </summary>
    [Fact]
    public void ContinuingAGauntletRunWithALevelIsRefusedRatherThanSilentlyIgnored()
    {
        var ok = ConsoleArguments.TryResolveGauntletLevel(continuing: true, ["--level", "4"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level refused", error);
        Assert.Contains("--continue", error);
    }

    [Fact]
    public void ContinuingAGauntletRunWithABareLevelIsAlsoRefused()
    {
        var ok = ConsoleArguments.TryResolveGauntletLevel(continuing: true, ["--level"], out _, out var error);

        Assert.False(ok);
        Assert.Contains("--level refused", error);
        Assert.Contains("--continue", error);
    }

    // ---- Program.cs's --create --level forwarding (#602, the console twin of #488) ----

    /// <summary>
    /// <see cref="ContinuingAGauntletRunWithALevelIsRefusedRatherThanSilentlyIgnored"/> and
    /// its neighbours above pin the flag being parsed into the right <c>startingLevel</c>
    /// value; nothing pinned that value actually reaching <c>GauntletRun.Start</c>'s
    /// <c>startingLevel</c> parameter on <c>Program.cs</c>'s <c>--create</c> branch —
    /// <c>--create --level 4</c> used to start at level 1 with nothing said, the console
    /// twin of #488's Godot bug. <c>Program.cs</c> is top-level statements driven by
    /// interactive <c>Console.ReadLine</c> through <c>PartyCreator</c>
    /// (this project's own <c>.csproj</c> remarks say that half stays untested, #317),
    /// so there is no way to call the real branch and observe the level it resolves to;
    /// the seam is a source check instead, the same shape
    /// <c>FlagRefusalTests.CreatedDraftsRunStartForwardsTheResolvedLevelRatherThanDefaultingToOne</c>
    /// uses for the Godot client's own equivalent line. It fails the instant this exact
    /// call stops appearing in <c>Program.cs</c> — whether <c>startingLevel:</c> is
    /// dropped, or <c>startingLevel</c> is swapped for a literal.
    /// </summary>
    [Fact]
    public void CreateRunStartForwardsTheResolvedLevelRatherThanDefaultingToOne()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SRDCombat.Console", "Program.cs"));

        Assert.Contains(
            "GauntletRun.Start(content, drafts, GauntletLadder.Default(), seed: seed, startingLevel: startingLevel)",
            source,
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SRDCombat.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find SRDCombat.sln above '{AppContext.BaseDirectory}'.");
    }
}
