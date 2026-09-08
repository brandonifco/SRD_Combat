namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="SavePathArgument.TryResolve"/> — the Godot client's <c>--save</c> decision,
/// moved into <c>SRDCombat.Game</c> the same way <see cref="GauntletStart"/> and
/// <see cref="ScenarioComposition"/> were (#654, #490's pattern), so a plain xUnit test
/// pins it rather than only a probe capture.
/// </summary>
public class SavePathArgumentTests
{
    [Fact]
    public void AnAbsentSaveDefaultsToTheDefaultPath()
    {
        var ok = SavePathArgument.TryResolve(FlagValue.Absent, out var savePath, out var error);

        Assert.True(ok);
        Assert.Equal(SavePathArgument.DefaultPath, savePath);
        Assert.Null(error);
    }

    /// <summary>
    /// The bug this pins: <c>ArgumentValue("save") ?? "srdcombat-save.json"</c> could not
    /// tell a bare <c>--save</c> apart from an absent one — both read as <c>null</c> —
    /// so a bare <c>--save</c> silently wrote autosaves to the default path instead of
    /// naming the missing value.
    /// </summary>
    [Fact]
    public void APresentButValuelessSaveIsRefusedRatherThanDefaulted()
    {
        var ok = SavePathArgument.TryResolve(FlagValue.Bare(), out _, out var error);

        Assert.False(ok);
        Assert.Contains("--save", error);
        Assert.Contains("no value given", error);
    }

    [Fact]
    public void APresentAndValuedSaveResolvesToItsValueVerbatim()
    {
        var ok = SavePathArgument.TryResolve(FlagValue.Of("custom-save.json"), out var savePath, out var error);

        Assert.True(ok);
        Assert.Equal("custom-save.json", savePath);
        Assert.Null(error);
    }
}
