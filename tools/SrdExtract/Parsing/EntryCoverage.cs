using System.Text.RegularExpressions;

namespace SrdExtract.Parsing;

/// <summary>
/// The claims made against one entry's text, and the residue left over.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a claim asserts.</b> A claim says the model expresses these characters — not
/// that a regex matched them, and not that a string was copied into a field. Three
/// consequences: a permissive subexpression inside a claiming pattern (<c>[^.]*?</c>,
/// <c>.+?</c>) matched characters nobody inspected and must not be claimed (see the
/// <see cref="Claim(Regex, Match, string, string[])"/> overload); a field that stores
/// prose verbatim without an executing resolver (a Reaction's Trigger/Response) is
/// storage, not expression, and stays unclaimed; and a grade
/// (<c>EntryMechanics</c>) says what shape an entry has, not how much of its text that
/// shape captures.
/// </para>
/// <para>
/// <b>Overlap is fine.</b> Claims are a set of characters, normalised by union. A
/// second claim over already-covered text changes nothing — under-claiming is the
/// danger this type exists to catch, not overlap.
/// </para>
/// <para>
/// <b>The glue rule.</b> Not every uncovered character is a lost rule. A tiny closed
/// set of punctuation and conjunctions, bounded on both sides by claimed spans (or by
/// the text's own edge for punctuation and whitespace alone), is absorbed rather than
/// reported as residue. See <see cref="Residue"/> and the worked table in
/// docs/2026-08-24-span-accounting-design.md, §4. Anything not provably glue is
/// residue: residue is cheap (a counted clause read once in a census), and a lazy glue
/// match is the keyword-filter bug (CLAUDE.md's bug 2) rebuilt inside the mechanism
/// meant to prevent it.
/// </para>
/// </remarks>
internal sealed class EntryCoverage
{
    /// <summary>The four punctuation characters that count as glue on their own.</summary>
    private static readonly char[] GluePunctuation = ['.', ',', ';', ':'];

    /// <summary>The three connective words that count as glue only between two claims.</summary>
    private static readonly HashSet<string> GlueConnectives =
        new(StringComparer.OrdinalIgnoreCase) { "and", "or", "plus" };

    private readonly List<TextSpan> _claims = [];

    public EntryCoverage(string text)
    {
        Text = text;
    }

    /// <summary>The entry's original text. Every span in this type is an offset into it.</summary>
    public string Text { get; }

    /// <summary>Records a claim. <paramref name="note"/> names the matcher, for the census — never serialized.</summary>
    public void Claim(TextSpan span, string note)
    {
        if (span.Start < 0 || span.Length < 0 || span.End > Text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(span),
                $"Span [{span.Start}, {span.End}) falls outside the entry's text (length {Text.Length}).");
        }

        // The note is not retained per-claim yet — the census tool (stage 3) is the
        // first consumer, and until then every call site still names its matcher so
        // that wiring costs nothing when it lands. See design §14.
        _ = note;

        if (span.Length > 0)
        {
            _claims.Add(span);
        }
    }

    /// <summary>
    /// A match's span minus the spans of the named groups the parse did not read. Takes
    /// the <see cref="Regex"/> as well as the <see cref="Match"/> so the wildcard
    /// convention (design §2.3) can be validated against patterns actually reached
    /// through this overload, rather than a hand-maintained list.
    /// </summary>
    public void Claim(Regex pattern, Match match, string note, params string[] unreadGroups)
    {
        if (!match.Success)
        {
            throw new ArgumentException("Cannot claim an unsuccessful match.", nameof(match));
        }

        ClaimingPatterns.Add(pattern.ToString());

        var matchEnd = match.Index + match.Length;

        var holes = unreadGroups
            .Select(name => match.Groups[name])
            .Where(group => group.Success)
            .Select(group => new TextSpan(group.Index, group.Length))
            .OrderBy(hole => hole.Start)
            .ToArray();

        var cursor = match.Index;

        foreach (var hole in holes)
        {
            if (hole.Start > cursor)
            {
                Claim(new TextSpan(cursor, hole.Start - cursor), note);
            }

            cursor = Math.Max(cursor, hole.End);
        }

        if (cursor < matchEnd)
        {
            Claim(new TextSpan(cursor, matchEnd - cursor), note);
        }
    }

    /// <summary>The whole entry, by a curated human decision — <c>Passive</c> or <c>Narrative</c>.</summary>
    public void ClaimWholeEntry(string note) => Claim(new TextSpan(0, Text.Length), note);

    /// <summary>
    /// The text with every claimed range replaced by spaces of the same length. Offsets
    /// are preserved exactly, so a later pass reading this instead of <see cref="Text"/>
    /// cannot match across a claimed region — the effect the deleted
    /// <c>string.Replace</c> lift-outs had, without losing the coordinate space (design
    /// §5).
    /// </summary>
    public string Masked
    {
        get
        {
            var chars = Text.ToCharArray();

            foreach (var span in _claims)
            {
                for (var i = span.Start; i < span.End; i++)
                {
                    chars[i] = ' ';
                }
            }

            return new string(chars);
        }
    }

    /// <summary>
    /// Uncovered runs with their spans, for the census tool. Every maximal run of
    /// characters no claim covers — glue and non-glue alike, unfiltered.
    /// <see cref="Residue"/> is built from this by absorbing the glue runs and chunking
    /// what remains.
    /// </summary>
    /// <remarks>
    /// Neighbouring claim notes are not reported yet — notes are not retained per-claim
    /// (see <see cref="Claim(TextSpan, string)"/>'s remark) — so both sides of the tuple
    /// read <see langword="null"/> until the census tool needs them.
    /// </remarks>
    public IReadOnlyList<(TextSpan Span, string Text, string? Before, string? After)> Uncovered() =>
        MaximalUncoveredRuns()
            .Select(span => (span, Text[span.Start..span.End], (string?)null, (string?)null))
            .ToArray();

    /// <summary>Uncovered runs, glue-absorbed and chunked. See design §6.</summary>
    public IReadOnlyList<string> Residue()
    {
        var chunks = new List<string>();

        foreach (var span in MaximalUncoveredRuns())
        {
            var runText = Text[span.Start..span.End];

            if (IsAbsorbedGlue(span, runText))
            {
                continue;
            }

            foreach (var chunkSpan in ChunkAtSentenceBoundaries(span))
            {
                var trimmed = TrimGlue(chunkSpan);

                if (trimmed.Length > 0)
                {
                    chunks.Add(trimmed);
                }
            }
        }

        return chunks;
    }

    /// <summary>
    /// Every regex pattern text ever passed to <see cref="Claim(Regex, Match, string, string[])"/>,
    /// process-wide. Populated as claiming matchers actually run against real entries, so
    /// the wildcard-convention test (design §2.3) can scan exactly the patterns that
    /// participate in coverage rather than a hand-maintained list. Deliberately not
    /// entry-scoped — the set accumulates across a whole corpus run.
    /// </summary>
    internal static HashSet<string> ClaimingPatterns { get; } = new(StringComparer.Ordinal);

    private IEnumerable<TextSpan> MaximalUncoveredRuns()
    {
        var covered = new bool[Text.Length];

        foreach (var span in _claims)
        {
            for (var j = span.Start; j < span.End; j++)
            {
                covered[j] = true;
            }
        }

        var i = 0;

        while (i < Text.Length)
        {
            if (covered[i])
            {
                i++;
                continue;
            }

            var start = i;

            while (i < Text.Length && !covered[i])
            {
                i++;
            }

            yield return new TextSpan(start, i - start);
        }
    }

    /// <summary>
    /// The boundedness rule (design §4.2). A run is absorbed exactly when every token in
    /// it is a glue token, and — only when it contains a connective word — both of its
    /// neighbours are claimed spans rather than the text's own edge. A run's neighbour is
    /// always either a claimed character or an edge (runs are maximal by construction),
    /// so "bounded by a claim" reduces to "not at the text's edge".
    /// </summary>
    private bool IsAbsorbedGlue(TextSpan span, string runText)
    {
        var tokens = TokenizeGlue(runText);

        if (tokens.Any(token => token.Kind == GlueTokenKind.Word))
        {
            return false;
        }

        var containsConnective = tokens.Any(token => token.Kind == GlueTokenKind.Connective);

        if (!containsConnective)
        {
            return true;
        }

        var leftIsEdge = span.Start == 0;
        var rightIsEdge = span.End == Text.Length;

        return !leftIsEdge && !rightIsEdge;
    }

    private enum GlueTokenKind
    {
        Whitespace,
        Punctuation,
        Connective,
        Word,
    }

    private readonly record struct GlueToken(GlueTokenKind Kind, string Text);

    /// <summary>
    /// Tokenises a run's text: whitespace runs, the four glue punctuation characters
    /// individually, and everything else as words — a word is
    /// <see cref="GlueTokenKind.Connective"/> when it is exactly "and", "or" or "plus"
    /// (case-insensitive) and <see cref="GlueTokenKind.Word"/> otherwise.
    /// </summary>
    private static List<GlueToken> TokenizeGlue(string runText)
    {
        var tokens = new List<GlueToken>();
        var i = 0;

        while (i < runText.Length)
        {
            var c = runText[i];

            if (char.IsWhiteSpace(c))
            {
                var start = i;

                while (i < runText.Length && char.IsWhiteSpace(runText[i]))
                {
                    i++;
                }

                tokens.Add(new GlueToken(GlueTokenKind.Whitespace, runText[start..i]));
                continue;
            }

            if (Array.IndexOf(GluePunctuation, c) >= 0)
            {
                tokens.Add(new GlueToken(GlueTokenKind.Punctuation, c.ToString()));
                i++;
                continue;
            }

            var wordStart = i;

            while (i < runText.Length
                && !char.IsWhiteSpace(runText[i])
                && Array.IndexOf(GluePunctuation, runText[i]) < 0)
            {
                i++;
            }

            var word = runText[wordStart..i];

            tokens.Add(new GlueToken(
                GlueConnectives.Contains(word) ? GlueTokenKind.Connective : GlueTokenKind.Word,
                word));
        }

        return tokens;
    }

    /// <summary>
    /// Splits a surviving uncovered span at sentence boundaries, so a run spanning two
    /// sentences yields two clauses (design §6.1, step 3). Reuses the same boundary
    /// pattern <c>SplitSentences</c> applies to a whole entry, over just this run's
    /// text, and reports each piece as a span so the caller can trim it without losing
    /// its offset into the entry's original text.
    /// </summary>
    private IEnumerable<TextSpan> ChunkAtSentenceBoundaries(TextSpan span)
    {
        var runText = Text[span.Start..span.End];
        var cursor = 0;

        foreach (var boundary in EntryMechanicsParser.SentenceBoundaryMatches(runText))
        {
            if (boundary.Index > cursor)
            {
                yield return new TextSpan(span.Start + cursor, boundary.Index - cursor);
            }

            cursor = boundary.Index + boundary.Length;
        }

        if (cursor < runText.Length)
        {
            yield return new TextSpan(span.Start + cursor, runText.Length - cursor);
        }
    }

    /// <summary>
    /// Trims a chunk of leading and trailing whitespace and glue punctuation — never a
    /// connective word, which is a sentence fragment somebody lost and should stay
    /// visible in the residue (design §4.2, rule 3; §6.1, step 4).
    /// </summary>
    private string TrimGlue(TextSpan span)
    {
        var text = Text[span.Start..span.End];
        var start = 0;
        var end = text.Length;

        while (start < end
            && (char.IsWhiteSpace(text[start]) || Array.IndexOf(GluePunctuation, text[start]) >= 0))
        {
            start++;
        }

        while (end > start
            && (char.IsWhiteSpace(text[end - 1]) || Array.IndexOf(GluePunctuation, text[end - 1]) >= 0))
        {
            end--;
        }

        return text[start..end];
    }
}
