using SRDCombat.TestSupport;
using SrdExtract.Pdf;

namespace SrdExtract.Tests;

/// <summary>
/// The page-fixture harness (#189): it pins the extractor's PDF-page-reading front end —
/// the column split, baseline line-grouping, footer drop and left-to-right ordering in
/// <see cref="PageTextReader.LayoutPage"/> — without needing the SRD PDF at test time.
/// </summary>
/// <remarks>
/// The characterization slice already feeds known strings to the <em>parsers</em>; this is
/// the layer beneath them, the geometry that turns positioned words into lines. It is pinned
/// the only way it can be tested without the (uncommitted) PDF: a committed fixture carries
/// the words <see cref="PageTextReader.ConvertPageWords"/> produced for a page (its
/// <see cref="PageFixture.Words"/>) beside the lines the front end made of them (its
/// <see cref="PageFixture.ExpectedLines"/>). This test replays each fixture's words through
/// <see cref="PageTextReader.LayoutPage"/> and asserts the front end still produces the same
/// lines. Break the column boundary, the baseline tolerance, the footer ceiling or the word
/// ordering, and a fixture's replay diverges from its golden.
///
/// <para>Fixtures live in <c>tests/SrdExtract.Tests/fixtures/pages</c> and are read from
/// source, not from <c>bin/</c> — see <see cref="RepositoryPaths"/>. To add one, see
/// <c>docs/guides/extraction.md</c> ("Adding a page fixture").</para>
/// </remarks>
public sealed class PageFixtureHarnessTests
{
    private const double BaselineEpsilon = 0.05;

    private static readonly string FixtureDirectory =
        Path.Combine(RepositoryPaths.RepositoryRoot, "tests", "SrdExtract.Tests", "fixtures", "pages");

    public static IEnumerable<object[]> Fixtures() =>
        Directory.EnumerateFiles(FixtureDirectory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new object[] { Path.GetFileName(path) });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ReplayingAPageFixtureReproducesItsGoldenLines(string fileName)
    {
        var fixture = Load(fileName);

        var produced = PageTextReader.LayoutPage(fixture.Words, fixture.Page, fixture.Layout);

        Assert.True(
            produced.Count == fixture.ExpectedLines.Count,
            $"{fileName}: front end produced {produced.Count} lines, fixture expects " +
            $"{fixture.ExpectedLines.Count}.\n" + Describe(produced, fixture.ExpectedLines));

        for (var index = 0; index < fixture.ExpectedLines.Count; index++)
        {
            var actual = produced[index];
            var expected = fixture.ExpectedLines[index];

            Assert.True(
                actual.Column == expected.Column
                    && string.Equals(actual.Text, expected.Text, StringComparison.Ordinal)
                    && string.Equals(actual.Font, expected.Font, StringComparison.Ordinal)
                    && Math.Abs(actual.Baseline - expected.Baseline) <= BaselineEpsilon,
                $"{fileName}: line {index} diverged.\n" +
                $"  expected  c{expected.Column} y{expected.Baseline:F2} {expected.Font} | {expected.Text}\n" +
                $"  produced  c{actual.Column} y{actual.Baseline:F2} {actual.Font} | {actual.Text}");
        }
    }

    /// <summary>
    /// A guard on the harness itself: an empty fixture directory would make every
    /// <see cref="Fixtures"/> theory pass vacuously (#528 — the instrument nobody verifies).
    /// This asserts the set that must be present — at least one captured real page and at
    /// least one synthetic boundary fixture, covering both layouts.
    /// </summary>
    [Fact]
    public void TheHarnessHasFixturesCoveringBothLayoutsAndBothKinds()
    {
        var fixtures = Fixtures()
            .Select(row => Load((string)row[0]))
            .ToList();

        Assert.Contains(fixtures, f => f.Layout == PageLayout.TwoColumn);
        Assert.Contains(fixtures, f => f.Layout == PageLayout.FullWidth);
        Assert.Contains(fixtures, f => f.Source.StartsWith("SRD 5.2.1", StringComparison.Ordinal));
        Assert.Contains(fixtures, f => f.Source.StartsWith("Synthetic", StringComparison.Ordinal));
    }

    /// <summary>
    /// The capture guard (Codex P3): rounding a captured page's coordinates to two decimals
    /// must not silently move a word across the column boundary — or the committed fixture
    /// would pin the rounded surrogate, not the real page. A word at x=299.996 is in the left
    /// column, but rounds to x=300.00, which is the right column; <see cref="PageFixture.Capture"/>
    /// must refuse rather than emit the flipped golden.
    /// </summary>
    [Fact]
    public void CaptureRefusesWhenRoundingWouldFlipAWordAcrossTheColumnBoundary()
    {
        var words = new[]
        {
            new SourceWord("left", "Optima-Bold", 63.0, 90.0, 6.0, 200.0),
            new SourceWord("flip", "Optima-Regular", 299.996, 300.0, 6.0, 200.0),
        };

        var thrown = Assert.Throws<InvalidOperationException>(
            () => PageFixture.Capture("test", 1, PageLayout.TwoColumn, words));

        Assert.Contains("boundary", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>The guard's counterpart: coordinates clear of every boundary capture cleanly,
    /// so the guard is proven not to false-trip on ordinary pages.</summary>
    [Fact]
    public void CaptureSucceedsWhenNoCoordinateSitsOnABoundary()
    {
        var words = new[]
        {
            new SourceWord("Right", "GillSans", 313.0, 350.0, 6.0, 200.0),
            new SourceWord("Left", "Optima-Bold", 63.0, 90.0, 6.0, 200.0),
        };

        var fixture = PageFixture.Capture("test", 1, PageLayout.TwoColumn, words);

        Assert.Equal(2, fixture.ExpectedLines.Count);
        Assert.Equal("Left", fixture.ExpectedLines[0].Text);
        Assert.Equal(0, fixture.ExpectedLines[0].Column);
        Assert.Equal("Right", fixture.ExpectedLines[1].Text);
        Assert.Equal(1, fixture.ExpectedLines[1].Column);
    }

    private static PageFixture Load(string fileName) =>
        PageFixture.FromJson(File.ReadAllText(Path.Combine(FixtureDirectory, fileName)));

    private static string Describe(
        IReadOnlyList<SourceLine> produced,
        IReadOnlyList<PageFixtureLine> expected)
    {
        var lines = new List<string> { "  expected:" };
        lines.AddRange(expected.Select(l => $"    c{l.Column} y{l.Baseline:F2} {l.Font} | {l.Text}"));
        lines.Add("  produced:");
        lines.AddRange(produced.Select(l => $"    c{l.Column} y{l.Baseline:F2} {l.Font} | {l.Text}"));
        return string.Join('\n', lines);
    }
}
