namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="GauntletStart.Resolve"/> — the gauntlet-start half of #490's flag-vs-mode
/// seam, moved off <c>PlayMode</c> by #490b so a plain xUnit test pins the decision and
/// its assembled refusal wording rather than only a probe capture.
/// </summary>
/// <remarks>
/// <b>Moved from <c>SRDCombat.Viewer.Tests.FlagRefusalTests</c>.</b> That class pinned
/// <c>PlayMode.TryResolveGauntletLevel</c> — the <c>--continue</c>/<c>--level</c> half
/// alone, because it was the only piece already split out of the live
/// <c>PlayMode</c> node. Now that the whole gauntlet-start decision (the
/// <c>--spawn</c>/<c>--scenario</c>-without-<c>--one-fight</c> refusals plus the
/// <c>--continue</c>/<c>--level</c> interaction and parse) lives in
/// <see cref="GauntletStart"/>, this class tests all of it directly, with no client and
/// no Godot involved at all. <c>FlagRefusalTests</c> keeps the seed/<c>--at</c> cases
/// (#489/#602 territory, untouched by this move) and the <c>--create --level</c>
/// forwarding source check, which stays a probe-only seam because it needs the live
/// node.
/// </remarks>
public class GauntletStartTests
{
    // ---- --spawn/--scenario without --one-fight (#463, #476) ----

    [Fact]
    public void SpawnWithoutOneFightIsRefused()
    {
        var result = GauntletStart.Resolve(
            spawn: FlagValue.Of("goblin x3"),
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent);

        Assert.Contains("--spawn refused", result.Refusal);
        Assert.Contains("the gauntlet does not read it", result.Refusal);
    }

    [Fact]
    public void SpawnWithOneFightIsNotRefusedByThisGate()
    {
        var result = GauntletStart.Resolve(
            spawn: FlagValue.Of("goblin x3"),
            scenario: FlagValue.Absent,
            oneFight: true,
            continuing: false,
            level: FlagValue.Absent);

        Assert.Null(result.Refusal);
    }

    [Fact]
    public void ScenarioWithoutOneFightIsRefused()
    {
        var result = GauntletStart.Resolve(
            spawn: FlagValue.Absent,
            scenario: FlagValue.Of("fight.json"),
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent);

        Assert.Contains("--scenario refused", result.Refusal);
        Assert.Contains("the gauntlet does not read it", result.Refusal);
    }

    [Fact]
    public void ScenarioWithOneFightIsNotRefusedByThisGate()
    {
        var result = GauntletStart.Resolve(
            spawn: FlagValue.Absent,
            scenario: FlagValue.Of("fight.json"),
            oneFight: true,
            continuing: false,
            level: FlagValue.Absent);

        Assert.Null(result.Refusal);
    }

    // ---- --difficulty without --one-fight (#443's own follow-up, a Codex finding) ----

    [Fact]
    public void DifficultyWithoutOneFightIsRefused()
    {
        var result = GauntletStart.Resolve(
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Of("high"));

        Assert.Contains("--difficulty refused", result.Refusal);
        Assert.Contains("the gauntlet does not read it", result.Refusal);
    }

    [Fact]
    public void DifficultyWithOneFightIsNotRefusedByThisGate()
    {
        var result = GauntletStart.Resolve(
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: true,
            continuing: false,
            level: FlagValue.Absent,
            difficulty: FlagValue.Of("high"));

        Assert.Null(result.Refusal);
    }

    [Fact]
    public void AGauntletLaunchWithNoDifficultyIsUnaffected()
    {
        var result = GauntletStart.Resolve(
            spawn: FlagValue.Absent,
            scenario: FlagValue.Absent,
            oneFight: false,
            continuing: false,
            level: FlagValue.Absent);

        Assert.Null(result.Refusal);
        Assert.Equal(1, result.Level);
    }

    // ---- --level / --continue (#488) ----

    [Fact]
    public void AFreshRunWithNoLevelDefaultsToOne()
    {
        var result = GauntletStart.Resolve(
            FlagValue.Absent, FlagValue.Absent, oneFight: false, continuing: false, level: FlagValue.Absent);

        Assert.Null(result.Refusal);
        Assert.Equal(1, result.Level);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    public void AFreshRunWithAnInRangeLevelParsesIt(string text)
    {
        var result = GauntletStart.Resolve(
            FlagValue.Absent, FlagValue.Absent, oneFight: false, continuing: false, level: FlagValue.Of(text));

        Assert.Null(result.Refusal);
        Assert.Equal(int.Parse(text), result.Level);
    }

    [Fact]
    public void AFreshRunWithANonNumericLevelIsRefusedRatherThanDefaulted()
    {
        var result = GauntletStart.Resolve(
            FlagValue.Absent, FlagValue.Absent, oneFight: false, continuing: false, level: FlagValue.Of("x"));

        Assert.Contains("--level refused", result.Refusal);
        Assert.Contains("1-5", result.Refusal);
    }

    [Fact]
    public void AFreshRunWithAnOutOfRangeLevelIsRefusedRatherThanClamped()
    {
        var result = GauntletStart.Resolve(
            FlagValue.Absent, FlagValue.Absent, oneFight: false, continuing: false, level: FlagValue.Of("9"));

        Assert.Contains("--level refused", result.Refusal);
        Assert.Contains("1-5", result.Refusal);
    }

    [Fact]
    public void APresentButValuelessLevelOnAFreshRunIsRefused()
    {
        var result = GauntletStart.Resolve(
            FlagValue.Absent, FlagValue.Absent, oneFight: false, continuing: false, level: FlagValue.Bare());

        Assert.Contains("--level refused", result.Refusal);
        Assert.Contains("no value given", result.Refusal);
    }

    [Fact]
    public void ContinuingWithNoLevelSucceedsAndTheLevelIsUnused()
    {
        var result = GauntletStart.Resolve(
            FlagValue.Absent, FlagValue.Absent, oneFight: false, continuing: true, level: FlagValue.Absent);

        Assert.Null(result.Refusal);
    }

    [Fact]
    public void ContinuingWithALevelIsRefusedRatherThanSilentlyIgnored()
    {
        var result = GauntletStart.Resolve(
            FlagValue.Absent, FlagValue.Absent, oneFight: false, continuing: true, level: FlagValue.Of("4"));

        Assert.Contains("--level refused", result.Refusal);
        Assert.Contains("--continue", result.Refusal);
    }

    /// <summary>
    /// A one-fight run never reaches the gauntlet's own <c>--level</c> handling (it
    /// goes through <see cref="ScenarioComposition"/> instead), so
    /// <c>--one-fight --continue --level=4</c> is silently ignored here exactly as it
    /// always was — not newly refused by folding the two gates into one call.
    /// </summary>
    [Fact]
    public void OneFightWithContinueAndLevelIsNotRefusedByThisGate()
    {
        var result = GauntletStart.Resolve(
            FlagValue.Absent, FlagValue.Absent, oneFight: true, continuing: true, level: FlagValue.Of("4"));

        Assert.Null(result.Refusal);
    }
}
