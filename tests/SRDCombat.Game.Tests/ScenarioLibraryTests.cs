using SRDCombat.Content;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The committed <c>scenarios/</c> library: every file in it loads, resolves against the
/// content beside it, states why it exists, and is exactly what the serializer writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the drift guard, and it is deliberately better than a save's rather than a
/// copy of it.</b> A save lives on a player's disk and can never be re-checked when the
/// corpus changes, which is why #287 stamps a content version and
/// <see cref="GauntletRun.Resume"/> refuses a mismatch. A committed scenario has the
/// opposite property: CI runs on every change to <c>data/srd</c> with the scenario right
/// there. So a regeneration that invalidates a committed scenario fails <b>in the pull
/// request that caused it</b>, instead of being discovered months later by somebody
/// opening the file. <see cref="BattleScenario.ContentVersion"/> stays provenance and is
/// deliberately not asserted here — a stamp going stale is not a scenario going wrong.
/// </para>
/// <para>
/// This is the project's standing lesson — <em>write the validator that asserts the shape
/// of what should have been found</em> — pointed at the new directory.
/// </para>
/// </remarks>
public class ScenarioLibraryTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    /// <summary>
    /// Every <c>.json</c> in the directory, not only every <c>.scenario.json</c>: a file
    /// that landed there under the wrong name should fail this suite loudly rather than
    /// be skipped by the glob that was supposed to check it.
    /// </summary>
    public static TheoryData<string> Files
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var path in Directory.EnumerateFiles(RepositoryPaths.ScenarioDirectory, "*.json"))
            {
                data.Add(Path.GetFileName(path));
            }

            return data;
        }
    }

    /// <summary>
    /// The premise every theory below rests on. A <see cref="TheoryData{T}"/> that came
    /// back empty would make each of them pass by examining nothing — the shape of the
    /// <c>&gt;= 300</c> spell floor that stayed green for months.
    /// </summary>
    [Fact]
    public void TheLibraryIsNotEmpty() => Assert.NotEmpty(Files);

    [Theory]
    [MemberData(nameof(Files))]
    public void ItIsNamedAsAScenario(string file) =>
        Assert.EndsWith(ScenarioFile.Extension, file, StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(Files))]
    public void ItLoads(string file) => Assert.Empty(Load(file).Errors);

    [Theory]
    [MemberData(nameof(Files))]
    public void ItResolvesAgainstThisBuildsContent(string file) =>
        Assert.Empty(ScenarioContent.CheckAgainst(Load(file).Scenario!, Content).Errors);

    /// <summary>
    /// The library's admission rule — <b>if you would cite it in an issue, commit it;
    /// otherwise use <c>--spawn</c></b> — made enforceable rather than aspirational. A
    /// committed scenario with blank notes is a review finding, and this is the review.
    /// </summary>
    [Theory]
    [MemberData(nameof(Files))]
    public void ItStatesWhyItExists(string file) =>
        Assert.False(
            string.IsNullOrWhiteSpace(Load(file).Scenario!.Notes),
            $"{file} does not say what it exists to show. A committed scenario asks a question the tree "
            + "wants asked again; anything else is a --spawn line, which needs no file.");

    /// <summary>
    /// A library file is exactly what <see cref="ScenarioFile.ToJson"/> writes, because a
    /// scenario file is the authoring surface's artifact and nothing else — Brandon's
    /// answer on 2026-08-26. Regenerate rather than hand-repair when this fails: re-save
    /// the scenario from whatever authored it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Files))]
    public void ItIsExactlyWhatTheSerializerWrites(string file) =>
        Assert.Equal(
            ScenarioFile.ToJson(Load(file).Scenario!),
            Read(file).TrimEnd('\n'));

    private static ScenarioLoad Load(string file) => ScenarioFile.FromJson(Read(file));

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(RepositoryPaths.ScenarioDirectory, file)).ReplaceLineEndings("\n");
}
