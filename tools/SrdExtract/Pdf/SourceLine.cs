namespace SrdExtract.Pdf;

/// <summary>One word, with the position and font the PDF gave it.</summary>
/// <param name="Text">The word's text, with SRD punctuation normalized.</param>
/// <param name="Font">
/// The font name with its subset prefix stripped — <c>Optima-BoldItalic</c> rather
/// than <c>ABCDEF+Optima-BoldItalic</c>.
/// </param>
/// <param name="Left">Left edge, in points from the page's left.</param>
/// <param name="Right">Right edge, in points.</param>
/// <param name="Height">Glyph height, which stands in for font size.</param>
/// <param name="Baseline">
/// Vertical position, measured up from the page's bottom. Words sharing a baseline are
/// on the same visual line.
/// </param>
public sealed record SourceWord(
    string Text,
    string Font,
    double Left,
    double Right,
    double Height,
    double Baseline);

/// <summary>
/// A visual line of text: the words sharing a baseline within one column, ordered left
/// to right.
/// </summary>
/// <param name="Page">The PDF page this line came from, 1-based.</param>
/// <param name="Column">0 for the left column, 1 for the right. Always 0 on a full-width page.</param>
/// <param name="Baseline">Vertical position, measured up from the page's bottom.</param>
/// <param name="Words">The line's words, left to right.</param>
public sealed record SourceLine(int Page, int Column, double Baseline, IReadOnlyList<SourceWord> Words)
{
    /// <summary>The whole line as text, single-spaced.</summary>
    public string Text { get; } = string.Join(' ', Words.Select(word => word.Text));

    /// <summary>The first word's font. Stands in for "what kind of line is this".</summary>
    public string Font => Words.Count > 0 ? Words[0].Font : string.Empty;

    /// <summary>The tallest glyph on the line, which distinguishes headers by level.</summary>
    public double Height => Words.Count > 0 ? Words.Max(word => word.Height) : 0;

    public double Left => Words.Count > 0 ? Words[0].Left : 0;

    /// <summary>True when every word uses a font whose name contains <paramref name="fragment"/>.</summary>
    public bool AllWordsUseFont(string fragment) =>
        Words.Count > 0 && Words.All(word => word.Font.Contains(fragment, StringComparison.Ordinal));

    /// <summary>
    /// The leading run of words in a font matching <paramref name="fragment"/>, as text.
    /// This is how an entry's name is separated from its body: the SRD sets
    /// "Burning Hammer." in bold italic and the prose after it in regular, on the same
    /// visual line.
    /// </summary>
    public string LeadingRunInFont(string fragment)
    {
        var words = Words
            .TakeWhile(word => word.Font.Contains(fragment, StringComparison.Ordinal))
            .Select(word => word.Text);

        return string.Join(' ', words);
    }

    public override string ToString() => $"p{Page} c{Column} y{Baseline:F0} {Font} | {Text}";
}
