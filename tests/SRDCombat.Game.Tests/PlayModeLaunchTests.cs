namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="PlayModeLaunch.TryResolve"/> (#491): the Godot twin of the console's
/// <c>ConsoleLaunch</c> (#624). <c>PlayMode.OnReady</c> used to run
/// <see cref="SavePathArgument.TryResolve"/> and <see cref="GauntletStart.Resolve"/> as
/// two separate resolve-and-branch blocks after the seed, then re-read
/// <c>HasArgument("one-fight")</c>/<c>HasArgument("continue")</c> to pick a branch. This
/// folds their composition — the order they run in, which refusal wins when two flags
/// are wrong at once, and the resulting mode — into one callable function so a plain
/// xUnit test pins the composition rather than only a live-node probe capture.
/// </summary>
/// <remarks>
/// The sub-seams keep their own tests: <c>SavePathArgumentTests</c> and
/// <see cref="GauntletStart"/>'s <c>GauntletStartTests</c> pin each decision in
/// isolation; these tests pin only that <see cref="PlayModeLaunch"/> composes them in the
/// right order and reports the right mode. The seed is deliberately not among the inputs —
/// it stays <c>PlayMode.OnReady</c>'s own first step (its default roll is ambient and
/// probe/capture-aware, and its resolver lives in the client assembly), so its refusal is
/// still covered by <c>SRDCombat.Viewer.Tests.FlagRefusalTests</c>, not here.
/// </remarks>
public class PlayModeLaunchTests
{
    // ---- Each mode resolves for valid args ----

    [Fact]
    public void OneFightResolvesToTheOneFightMode()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: true,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PlayModeLaunchMode.OneFight, launch!.Mode);
        Assert.Equal(SavePathArgument.DefaultPath, launch.SavePath);
    }

    [Fact]
    public void ContinueResolvesToTheContinueMode()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: true,
            level: FlagValue.Absent,
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PlayModeLaunchMode.Continue, launch!.Mode);
    }

    [Fact]
    public void NoModeFlagsResolveToAFreshGauntletAtLevelOne()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PlayModeLaunchMode.FreshGauntlet, launch!.Mode);
        Assert.Equal(1, launch.Level);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    public void AFreshGauntletForwardsAnInRangeStartingLevel(string text)
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Of(text),
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PlayModeLaunchMode.FreshGauntlet, launch!.Mode);
        Assert.Equal(int.Parse(text), launch.Level);
    }

    // ---- Mode precedence ----

    /// <summary>
    /// <c>--one-fight</c> wins over <c>--continue</c> the same way <c>OnReady</c>'s
    /// <c>if (one-fight) … else if (continue)</c> ordering did — and, because
    /// <see cref="GauntletStart.Resolve"/> short-circuits on <c>oneFight</c>, the
    /// <c>--continue --level</c> combination that would refuse on the gauntlet path is
    /// not refused here, exactly as it never was under <c>--one-fight</c>.
    /// </summary>
    [Fact]
    public void OneFightWinsOverContinueAndIsNotRefusedByTheContinueLevelGate()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: true,
            continuing: true,
            level: FlagValue.Of("4"),
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PlayModeLaunchMode.OneFight, launch!.Mode);
    }

    // ---- Save path (forwarded from SavePathArgument) ----

    [Fact]
    public void AnExplicitSavePathIsForwarded()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Of("run.json"),
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: true,
            level: FlagValue.Absent,
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("run.json", launch!.SavePath);
    }

    [Fact]
    public void ABareSaveIsRefusedRatherThanDefaulted()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Bare(),
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--save", error);
        Assert.Contains("no value given", error);
    }

    // ---- Gauntlet-start refusals surface verbatim ----

    [Fact]
    public void SpawnWithoutOneFightIsRefused()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Of("goblin x3"),
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--spawn refused", error);
    }

    [Fact]
    public void ScenarioWithoutOneFightIsRefused()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Of("fight.json"),
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--scenario refused", error);
    }

    [Fact]
    public void DifficultyWithoutOneFightIsRefused()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Of("high"),
            out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--difficulty refused", error);
    }

    [Fact]
    public void ContinuingWithALevelIsRefused()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: true,
            level: FlagValue.Of("4"),
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--level refused", error);
        Assert.Contains("--continue", error);
    }

    [Fact]
    public void AnOutOfRangeStartingLevelIsRefusedRatherThanClamped()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Absent,
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Of("9"),
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--level refused", error);
        Assert.Contains("1-5", error);
    }

    // ---- Order: save is checked before the gauntlet-start gates ----

    /// <summary>
    /// When both <c>--save</c> and a gauntlet flag are wrong at once, the save refusal
    /// wins — because <see cref="SavePathArgument.TryResolve"/> runs before
    /// <see cref="GauntletStart.Resolve"/>, exactly as the save block returned before the
    /// gauntlet block ever ran in <c>OnReady</c>. If those two swapped order, this fight
    /// would surface the <c>--spawn</c> refusal instead.
    /// </summary>
    [Fact]
    public void ABareSaveWinsOverABadGauntletFlag()
    {
        var ok = PlayModeLaunch.TryResolve(
            save: FlagValue.Bare(),
            spawn: FlagValue.Of("goblin x3"),
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Absent,
            out var launch, out var error);

        Assert.False(ok);
        Assert.Null(launch);
        Assert.Contains("--save", error);
        Assert.DoesNotContain("--spawn", error);
    }
}
