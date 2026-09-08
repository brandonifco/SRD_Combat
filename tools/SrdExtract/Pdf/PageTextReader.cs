using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SrdExtract.Pdf;

/// <summary>How a page's text is laid out.</summary>
public enum PageLayout
{
    /// <summary>
    /// The SRD's normal body layout. Reading straight across a page interleaves the two
    /// columns into nonsense, so each column is read separately, left then right.
    /// </summary>
    TwoColumn,

    /// <summary>A table spanning the full page width, such as the Weapons table.</summary>
    FullWidth,
}

/// <summary>
/// Turns SRD pages into ordered lines of positioned, font-tagged words.
/// </summary>
/// <remarks>
/// Everything downstream depends on two things this class gets right. First, the
/// column split: the two text columns start at roughly x=63 and x=313, and grouping
/// words by baseline across the whole page merges unrelated lines — a monster's name
/// ends up glued to the adjacent column's AC line. Second, fonts: the SRD's typography
/// is consistent enough to be a parsing signal, so the font name travels with every
/// word rather than being discarded.
/// </remarks>
public static partial class PageTextReader
{
    /// <summary>
    /// Words to the left of this x belong to the left column. The columns start near
    /// x=63 and x=313 on a 594pt page, so the gutter is wide and this boundary is not
    /// close to any real text.
    /// </summary>
    private const double ColumnBoundary = 300.0;

    /// <summary>
    /// Words within this many points of each other vertically are on the same line.
    /// Body text is set about 12pt apart, so this separates lines without splitting one.
    /// </summary>
    private const double BaselineTolerance = 2.5;

    /// <summary>Anything below this is the running footer, not content.</summary>
    private const double FooterCeiling = 40.0;

    public static IReadOnlyList<SourceLine> Read(string pdfPath, int firstPage, int lastPage, PageLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        var lines = new List<SourceLine>();

        using var document = PdfDocument.Open(pdfPath);

        for (var pageNumber = firstPage; pageNumber <= lastPage; pageNumber++)
        {
            var page = document.GetPage(pageNumber);
            lines.AddRange(LayoutPage(ConvertPageWords(page), page.Number, layout));
        }

        return lines;
    }

    /// <summary>
    /// Opens the PDF and returns one page's raw converted words — the input a page fixture
    /// captures. Used only by <c>SrdExtract --capture-fixture</c> at fixture-authoring time;
    /// the harness itself never calls this and never touches the PDF.
    /// </summary>
    internal static IReadOnlyList<SourceWord> ReadPageWords(string pdfPath, int pageNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        using var document = PdfDocument.Open(pdfPath);
        return ConvertPageWords(document.GetPage(pageNumber));
    }

    /// <summary>
    /// Converts every word on a page into a positioned, font-tagged <see cref="SourceWord"/>,
    /// with nothing dropped and nothing grouped. This is the boundary between PdfPig and the
    /// geometry: it is the only part that needs the PDF, so a captured list of the words it
    /// returns is exactly what <see cref="LayoutPage"/> — the geometry under test — consumes.
    /// The page-fixture harness (<c>tests/SrdExtract.Tests</c>, #189) pins that geometry by
    /// replaying committed word lists through <see cref="LayoutPage"/> with no PDF present.
    /// </summary>
    internal static IReadOnlyList<SourceWord> ConvertPageWords(Page page) =>
        page.GetWords().Select(Convert).ToList();

    /// <summary>
    /// The geometry front end, pure and PDF-free: drop the running footer and blank words,
    /// split the two columns (or not, when the page is a full-width table), and group the
    /// survivors into baseline-ordered lines. Every rule downstream reads these lines, so a
    /// wrong column boundary or baseline tolerance corrupts the whole extraction silently —
    /// which is why this is the layer the page fixtures exercise directly.
    /// </summary>
    /// <remarks>
    /// The footer and blank-word drops were once applied to the raw PdfPig word before
    /// conversion; they moved here, onto the converted word, so the fixture harness pins them
    /// too. This is behaviour-preserving: <see cref="SourceWord.Baseline"/> is the raw word's
    /// <c>BoundingBox.Bottom</c> unchanged, and a word is blank after normalisation exactly
    /// when it was blank before it (normalisation only swaps punctuation for ASCII and trims).
    /// </remarks>
    internal static IReadOnlyList<SourceLine> LayoutPage(
        IReadOnlyList<SourceWord> pageWords,
        int pageNumber,
        PageLayout layout)
    {
        var words = pageWords
            .Where(word => word.Baseline > FooterCeiling)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .ToList();

        if (layout == PageLayout.FullWidth)
        {
            return GroupIntoLines(words, pageNumber, column: 0);
        }

        var left = words.Where(word => word.Left < ColumnBoundary).ToList();
        var right = words.Where(word => word.Left >= ColumnBoundary).ToList();

        return GroupIntoLines(left, pageNumber, column: 0)
            .Concat(GroupIntoLines(right, pageNumber, column: 1))
            .ToList();
    }

    private static List<SourceLine> GroupIntoLines(List<SourceWord> words, int pageNumber, int column)
    {
        var lines = new List<SourceLine>();

        // Descending, because PDF coordinates put the page's top at the largest y.
        var ordered = words.OrderByDescending(word => word.Baseline).ToList();

        var current = new List<SourceWord>();
        var currentBaseline = double.NaN;

        foreach (var word in ordered)
        {
            if (current.Count > 0 && Math.Abs(word.Baseline - currentBaseline) > BaselineTolerance)
            {
                lines.Add(Finish(current, pageNumber, column, currentBaseline));
                current = [];
            }

            if (current.Count == 0)
            {
                currentBaseline = word.Baseline;
            }

            current.Add(word);
        }

        if (current.Count > 0)
        {
            lines.Add(Finish(current, pageNumber, column, currentBaseline));
        }

        return lines;
    }

    private static SourceLine Finish(List<SourceWord> words, int pageNumber, int column, double baseline) =>
        new(pageNumber, column, baseline, words.OrderBy(word => word.Left).ToArray());

    private static SourceWord Convert(Word word) => new(
        Normalize(word.Text),
        StripSubsetPrefix(word.FontName),
        word.BoundingBox.Left,
        word.BoundingBox.Right,
        word.BoundingBox.Height,
        word.BoundingBox.Bottom);

    /// <summary>
    /// Normalizes the typographic characters the SRD actually uses. The Unicode minus
    /// in particular matters: initiative and ability modifiers can be negative, and
    /// <c>−1</c> is not parseable as a number without this.
    /// </summary>
    private static string Normalize(string text) => text
        .Replace('−', '-')
        .Replace('–', '-')
        .Replace('—', '-')
        .Replace('’', '\'')
        .Replace('‘', '\'')
        .Replace('“', '"')
        .Replace('”', '"')
        .Replace(' ', ' ')
        .Trim();

    private static string StripSubsetPrefix(string? fontName)
    {
        if (string.IsNullOrEmpty(fontName))
        {
            return string.Empty;
        }

        var match = SubsetPrefix().Match(fontName);
        return match.Success ? fontName[(match.Length)..] : fontName;
    }

    [GeneratedRegex("^[A-Z]{6}\\+")]
    private static partial Regex SubsetPrefix();
}
