namespace SrdExtract.Parsing;

/// <summary>
/// A half-open character range <c>[Start, End)</c> into one entry's original text.
/// </summary>
/// <remarks>
/// There is exactly one coordinate space in this refactor — the entry's original
/// <c>text</c>, the string that becomes <c>MonsterEntry.Text</c> or
/// <c>TraitEntry.Text</c> — and every span is an offset into it. Nothing ever
/// rewrites that string; see <see cref="EntryCoverage.Masked"/> for how a later pass
/// is kept from re-reading text an earlier one already claimed.
/// </remarks>
internal readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>The exclusive end of the range.</summary>
    public int End => Start + Length;
}
