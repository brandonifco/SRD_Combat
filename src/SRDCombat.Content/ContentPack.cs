namespace SRDCombat.Content;

/// <summary>
/// A content file: a format version, a note on where the content came from, and the
/// items themselves.
/// </summary>
/// <typeparam name="TItem">The definition type the file holds.</typeparam>
public sealed record ContentPack<TItem>
{
    /// <summary>Matches <see cref="ContentSerializer.CurrentFormatVersion"/> for a loadable file.</summary>
    public required int FormatVersion { get; init; }

    /// <summary>What the file holds — <c>monsters</c>, <c>weapons</c>, <c>armor</c>.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// Attribution for the content, carried in the file itself so a copy of it away
    /// from this repository still says where it came from. See <c>NOTICE.md</c>.
    /// </summary>
    public required string Source { get; init; }

    public required IReadOnlyList<TItem> Items { get; init; }
}
