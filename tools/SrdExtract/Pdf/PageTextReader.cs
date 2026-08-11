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
            lines.AddRange(ReadPage(document.GetPage(pageNumber), layout));
        }

        return lines;
    }

    private static IEnumerable<SourceLine> ReadPage(Page page, PageLayout layout)
    {
        var words = page.GetWords()
            .Where(word => word.BoundingBox.Bottom > FooterCeiling)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(Convert)
            .ToList();

        if (layout == PageLayout.FullWidth)
        {
            return GroupIntoLines(words, page.Number, column: 0);
        }

        var left = words.Where(word => word.Left < ColumnBoundary).ToList();
        var right = words.Where(word => word.Left >= ColumnBoundary).ToList();

        return GroupIntoLines(left, page.Number, column: 0)
            .Concat(GroupIntoLines(right, page.Number, column: 1));
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
