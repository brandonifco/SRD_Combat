namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The client builds fights through one seam and one only (#474, criterion 5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Written as a source check because the shape is the point, not any one behaviour.</b>
/// <c>--spawn</c>, <c>--scenario</c> (S4) and the builder screen (S10) all describe a
/// <c>BattleScenario</c> and hand it to <c>ScenarioRunner</c>; the moment a second caller
/// reaches past that into <c>EncounterFactory</c>, the two routes start making their own
/// decisions about what a fight is and drift apart one flag at a time. No behavioural
/// test catches a duplicate route — both routes work, which is exactly the problem — so
/// the validator asserts the shape instead, which is this project's standing lesson
/// pointed at a seam rather than at a parser.
/// </para>
/// <para>
/// It matches a call rather than a name, so the doc comments that reference
/// <c>EncounterFactory</c> to explain the seam stay legal: a <c>&lt;see cref&gt;</c>
/// carries no bracket.
/// </para>
/// </remarks>
public class FightSeamTests
{
    private static IReadOnlyList<(string File, string Text)> Sources { get; } = Read();

    /// <summary>
    /// The premise the checks below rest on. An empty list would make each of them pass
    /// by examining nothing — the <c>&gt;= 300</c> spell floor's failure shape.
    /// </summary>
    [Fact]
    public void TheClientSourcesWereFound() => Assert.NotEmpty(Sources);

    [Fact]
    public void NothingInTheClientCallsEncounterFactoryDirectly() =>
        Assert.Empty(Sources
            .Where(source =>
                source.Text.Contains("EncounterFactory.Build(", StringComparison.Ordinal)
                || source.Text.Contains("EncounterFactory.BuildChosen(", StringComparison.Ordinal))
            .Select(source => source.File));

    /// <summary>
    /// And there is exactly one call into the runner, so "one seam" is a fact about the
    /// tree rather than a claim in a doc comment.
    /// </summary>
    [Fact]
    public void TheRunnerIsCalledFromExactlyOnePlace() =>
        Assert.Equal(
            ["FightScreen.cs"],
            Sources
                .Where(source => source.Text.Contains("ScenarioRunner.Build(", StringComparison.Ordinal))
                .Select(source => source.File)
                .ToArray());

    private static IReadOnlyList<(string, string)> Read() =>
    [
        .. Directory
            .EnumerateFiles(ViewerRepositoryPaths.ClientSourceDirectory, "*.cs", SearchOption.AllDirectories)
            // Godot's own generated glue lives under .godot and is not the client's code.
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.godot{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path))),
    ];
}
