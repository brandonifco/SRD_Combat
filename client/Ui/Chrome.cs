using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// The font every screen draws with, the shared truncation rule, and the heading's
/// backdrop measurement. A plain class, constructed by each screen — it holds no
/// board, camera or sprite state, and it cannot itself call <c>DrawString</c> (a
/// <see cref="CanvasItem"/> instance method), so a screen draws with
/// <c>DrawString(_chrome.Font, …)</c> rather than Chrome drawing anything itself.
/// That is fine, and is not worth an abstraction of its own (#327 S7).
/// </summary>
internal sealed class Chrome(Font font)
{
    internal Font Font { get; } = font;

    /// <summary>Cuts text to fit a fixed character width, marking the cut with an ellipsis.</summary>
    internal static string Trim(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

    /// <summary>
    /// Breaks text into lines of at most <paramref name="width"/> characters, on word
    /// boundaries, keeping any newlines the text already had. A pure text function
    /// like <see cref="Trim"/>, so it lives beside it rather than needing a screen.
    /// </summary>
    internal static IReadOnlyList<string> Wrap(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = string.Empty;

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length + word.Length + 1 > width && line.Length > 0)
                {
                    lines.Add(line);
                    line = string.Empty;
                }

                line += (line.Length > 0 ? " " : string.Empty) + word;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>
    /// The heading's backdrop rectangle, measured to the widest of its three lines
    /// rather than a fixed strip — the caller still issues the actual
    /// <c>DrawRect</c>/<c>DrawString</c> calls.
    /// </summary>
    internal Rect2 HeadingBackdrop(string title, string subtitle, string statusLine)
    {
        var width = MathF.Max(
            Font.GetStringSize(title, fontSize: 20).X,
            MathF.Max(
                Font.GetStringSize(subtitle, fontSize: 13).X,
                Font.GetStringSize(statusLine, fontSize: 12).X)) + 32;

        return new Rect2(8, 8, width, 80);
    }
}
