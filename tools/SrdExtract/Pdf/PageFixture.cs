using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrdExtract.Pdf;

/// <summary>
/// A committed capture of one page's mechanical layout: the positioned, font-tagged words
/// <see cref="PageTextReader.ConvertPageWords"/> produced, paired with the lines
/// <see cref="PageTextReader.LayoutPage"/> made of them. It is the fixture the page harness
/// (<c>tests/SrdExtract.Tests</c>, #189) replays with no PDF present — feeding
/// <see cref="Words"/> back through <see cref="PageTextReader.LayoutPage"/> and asserting the
/// result still equals <see cref="ExpectedLines"/>. A regression in the column split, the
/// baseline grouping, the footer drop or the word ordering changes the produced lines and the
/// replay fails.
/// </summary>
/// <remarks>
/// Two kinds of fixture share this format. A <em>captured</em> fixture is written by
/// <c>SrdExtract --capture-fixture</c> from the real PDF, so its coordinates are the book's
/// own ground truth and its golden is whatever the front end produced when it was captured —
/// regenerating it to chase a diff hides exactly the regression it exists to catch, so read
/// the diff instead (the same discipline the frozen transcript rests on). A <em>synthetic</em>
/// fixture is hand-authored: its coordinates are chosen to sit right on a documented boundary
/// (the column gutter, the 2.5pt baseline tolerance) and its <see cref="ExpectedLines"/> are
/// reasoned out by hand, so it encodes intent rather than the code's current output. Both are
/// just JSON; the harness does not care which it is reading.
/// </remarks>
internal sealed record PageFixture(
    string Source,
    int Page,
    PageLayout Layout,
    IReadOnlyList<SourceWord> Words,
    IReadOnlyList<PageFixtureLine> ExpectedLines)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Builds a fixture from a page's raw converted words, computing the golden by running the
    /// very geometry the harness will later replay. Coordinates are rounded so the committed
    /// file is small and reviewable — the SRD's columns are ~250pt apart and its lines ~12pt,
    /// so 2-decimal rounding is far finer than any boundary the geometry tests.
    /// </summary>
    /// <remarks>
    /// "Far finer" is asserted rather than assumed: rounding could, at a coordinate sitting
    /// within 0.005pt of the column boundary, footer ceiling or baseline tolerance, flip a
    /// word's column or merge two lines, so the committed fixture would pin the rounded
    /// surrogate instead of the real page. This guards against exactly that — it lays out the
    /// <em>unrounded</em> words too and refuses to write a fixture whose rounded lines differ
    /// from them, telling the author the page needs full precision.
    /// </remarks>
    public static PageFixture Capture(
        string source,
        int page,
        PageLayout layout,
        IReadOnlyList<SourceWord> pageWords)
    {
        var rounded = pageWords.Select(Round).ToList();

        var goldenFromRounded = ToFixtureLines(PageTextReader.LayoutPage(rounded, page, layout));
        var goldenFromExact = ToFixtureLines(PageTextReader.LayoutPage(pageWords, page, layout));

        if (!LinesMatch(goldenFromRounded, goldenFromExact))
        {
            throw new InvalidOperationException(
                $"Rounding page {page}'s words to 2 decimals changed the lines the front end makes " +
                "of them — a coordinate sits on a boundary (column, footer or baseline tolerance). " +
                "This page needs full precision; do not round it.");
        }

        return new PageFixture(source, page, layout, rounded, goldenFromRounded);
    }

    private static IReadOnlyList<PageFixtureLine> ToFixtureLines(IReadOnlyList<SourceLine> lines) =>
        lines
            .Select(line => new PageFixtureLine(line.Column, Math.Round(line.Baseline, 2), line.Font, line.Text))
            .ToList();

    private static bool LinesMatch(
        IReadOnlyList<PageFixtureLine> a,
        IReadOnlyList<PageFixtureLine> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        // Structure must be identical; baselines may differ only by the rounding itself
        // (<= 0.005 either way).
        return a.Zip(b).All(pair =>
            pair.First.Column == pair.Second.Column
            && string.Equals(pair.First.Text, pair.Second.Text, StringComparison.Ordinal)
            && string.Equals(pair.First.Font, pair.Second.Font, StringComparison.Ordinal)
            && Math.Abs(pair.First.Baseline - pair.Second.Baseline) <= 0.01);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static PageFixture FromJson(string json) =>
        JsonSerializer.Deserialize<PageFixture>(json, Options)
            ?? throw new JsonException("Page fixture JSON deserialised to null.");

    private static SourceWord Round(SourceWord word) => word with
    {
        Left = Math.Round(word.Left, 2),
        Right = Math.Round(word.Right, 2),
        Height = Math.Round(word.Height, 2),
        Baseline = Math.Round(word.Baseline, 2),
    };
}

/// <summary>One line of a fixture's golden: the column it landed in, its baseline, the font of
/// its first word (the "what kind of line is this" signal), and its whole text left to right.
/// </summary>
internal sealed record PageFixtureLine(int Column, double Baseline, string Font, string Text);
