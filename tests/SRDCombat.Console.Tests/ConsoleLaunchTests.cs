using SRDCombat.Core.Rules;

namespace SRDCombat.Console.Tests;

/// <summary>
/// <see cref="ConsoleLaunch"/> (#624): the launch-mode decision <c>Program.cs</c>'s
/// top-level statements used to make by their own sequence — which of the four modes a
/// launch is in, and which flags that mode reads — pulled out into a pure, callable,
/// directly testable function. Before this seam existed, nothing could observe "this
/// flag is read by nothing in this mode" except six process-level launches
/// (<see cref="ProgramRefusalTests"/>) that cannot reach a mode which opens a fight or
/// blocks on further stdin. These tests call <see cref="ConsoleLaunch.TryResolve"/>
/// directly, for every mode's happy path and for the silent-drop shapes #624 closes:
/// <c>--one-fight</c> against <c>--continue</c>/<c>--create</c>/<c>--save</c>, and
/// <c>--continue</c> against <c>--create</c>/<c>--seed</c>.
/// </summary>
public class ConsoleLaunchTests
{
    // ---- Each mode resolves correctly for valid args ----

    [Fact]
    public void OneFightResolvesWithItsOwnFlagsAndNoSavePath()
    {
        var ok = ConsoleLaunch.TryResolve(
            ["--one-fight", "--seed", "12345", "--level", "3", "--difficulty", "high"],
            out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ConsoleLaunchMode.OneFight, launch!.Mode);
        Assert.Equal(12345, launch.Seed);
        Assert.Equal(3, launch.Level);
        Assert.Equal(EncounterDifficulty.High, launch.Difficulty);
    }

    [Fact]
    public void OneFightAloneDefaultsLevelAndDifficultyAndRollsNoSeedItself()
    {
        var ok = ConsoleLaunch.TryResolve(["--one-fight"], out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ConsoleLaunchMode.OneFight, launch!.Mode);
        Assert.Null(launch.Seed);
        Assert.Equal(1, launch.Level);
        Assert.Equal(EncounterDifficulty.Low, launch.Difficulty);
    }

    [Fact]
    public void ContinueResolvesWithTheDefaultSavePathAbsentSave()
    {
        var ok = ConsoleLaunch.TryResolve(["--continue"], out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ConsoleLaunchMode.Continue, launch!.Mode);
        Assert.Equal(ConsoleLaunch.DefaultSavePath, launch.SavePath);
        Assert.Null(launch.Seed);
    }

    [Fact]
    public void ContinueForwardsAnExplicitSavePath()
    {
        var ok = ConsoleLaunch.TryResolve(["--continue", "--save", "run.json"], out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ConsoleLaunchMode.Continue, launch!.Mode);
        Assert.Equal("run.json", launch.SavePath);
    }

    [Fact]
    public void CreateResolvesAndForwardsTheStartingLevel()
    {
        var ok = ConsoleLaunch.TryResolve(["--create", "--level", "3"], out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ConsoleLaunchMode.Create, launch!.Mode);
        Assert.Equal(3, launch.Level);
    }

    [Fact]
    public void FreshGauntletIsTheDefaultModeWithNoFlagsAtAll()
    {
        var ok = ConsoleLaunch.TryResolve([], out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ConsoleLaunchMode.FreshGauntlet, launch!.Mode);
        Assert.Equal(1, launch.Level);
        Assert.Null(launch.Seed);
        Assert.Equal(ConsoleLaunch.DefaultSavePath, launch.SavePath);
    }

    // ---- --one-fight refuses the three flags it reads nothing from (#624's headline bug) ----

    [Fact]
    public void OneFightWithContinueIsRefusedRatherThanSilentlyPlayingAFreshFight()
    {
        var ok = ConsoleLaunch.TryResolve(["--one-fight", "--continue"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--continue refused", error);
        Assert.Contains("--one-fight", error);
    }

    [Fact]
    public void OneFightWithCreateIsRefusedRatherThanSilentlyPlayingThePregeneratedParty()
    {
        var ok = ConsoleLaunch.TryResolve(["--one-fight", "--create"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--create refused", error);
        Assert.Contains("--one-fight", error);
    }

    [Fact]
    public void OneFightWithSaveIsRefused()
    {
        var ok = ConsoleLaunch.TryResolve(["--one-fight", "--save", "run.json"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--save refused", error);
        Assert.Contains("--one-fight", error);
    }

    // ---- --continue against --create and a syntactically valid --seed ----

    [Fact]
    public void ContinueWithCreateIsRefusedRatherThanSilentlyResuming()
    {
        var ok = ConsoleLaunch.TryResolve(["--continue", "--create"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--create refused", error);
        Assert.Contains("--continue", error);
    }

    [Fact]
    public void ContinueWithASeedIsRefusedRatherThanSilentlyIgnored()
    {
        var ok = ConsoleLaunch.TryResolve(["--continue", "--seed", "5"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--seed refused", error);
        Assert.Contains("--continue", error);
    }

    [Fact]
    public void ContinueWithALevelIsStillRefusedThroughTheResolver()
    {
        var ok = ConsoleLaunch.TryResolve(["--continue", "--level", "4"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--level refused", error);
        Assert.Contains("--continue", error);
    }

    // ---- The four existing wiring refusals, reproduced through the resolver ----

    [Fact]
    public void AnInvalidSeedIsRefusedByTheResolver()
    {
        var ok = ConsoleLaunch.TryResolve(["--seed", "abc"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--seed abc", error);
        Assert.Contains("not a whole number", error);
    }

    [Fact]
    public void AnInvalidLevelIsRefusedByTheResolver()
    {
        var ok = ConsoleLaunch.TryResolve(["--level", "99"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--level 99", error);
        Assert.Contains("1-5", error);
    }

    [Fact]
    public void ADifficultyFlagWithoutOneFightIsRefusedByTheResolver()
    {
        var ok = ConsoleLaunch.TryResolve(["--difficulty", "low"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--difficulty refused", error);
        Assert.Contains("--one-fight", error);
    }

    [Fact]
    public void AnInvalidDifficultyWithOneFightIsRefusedByTheResolver()
    {
        var ok = ConsoleLaunch.TryResolve(["--one-fight", "--difficulty", "extreme"], out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--difficulty extreme", error);
        Assert.Contains("low, moderate, high", error);
    }
}
