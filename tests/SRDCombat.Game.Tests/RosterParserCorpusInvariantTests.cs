using SRDCombat.Content;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The trip-wire for the corpus invariant <see cref="RosterParser"/>'s grammar rests on
/// (#464, the #412 misattribution-guard pattern). <see cref="RosterParser.Parse"/> is
/// unambiguous only because the bestiary holds three shapes nothing in the parser itself
/// asserts:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
/// <b>No name contains a comma.</b> Entries are split on <c>,</c> before a name is ever
/// read, so a comma-bearing name would be silently severed into two unparseable
/// fragments — not a crash, just entries the parser reports as unknown monsters,
/// naming the wrong (truncated) text.
/// </item>
/// <item>
/// <b>No name begins with a token <c>int.TryParse</c> accepts.</b> A leading count is
/// recognised by parsing the entry's first word as an integer, so a name whose first
/// word is numeric would have that word eaten as a count — best case an off-by-one
/// count and a truncated, unspellable remainder; worst case the remainder happens to
/// match a *different* monster's name and the wrong creature is silently spawned.
/// </item>
/// <item>
/// <b>Names are unique case-insensitively.</b> <see cref="RosterParser.Parse"/> matches
/// with <c>FirstOrDefault</c> under <see cref="StringComparison.OrdinalIgnoreCase"/>, so
/// two names differing only in case would make the match depend on bestiary order —
/// whichever entry the content loader happened to list first would always win, and the
/// second would be unreachable by name.
/// </item>
/// </list>
/// <para>
/// All three hold today (qc verified this against the corpus while reviewing #459), but
/// nothing asserted it before this test. Per CLAUDE.md's #412 doctrine, a reading that
/// rests on an unasserted corpus invariant gets a trip-wire so the first counter-example
/// forces a decision — widen the grammar (a quoting rule for commas, a marker other than
/// position for counts) or reject the offending content — rather than misparsing
/// silently the day a new monster breaks it.
/// </para>
/// </remarks>
public class RosterParserCorpusInvariantTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void NoBestiaryNameContainsAComma()
    {
        var offenders = Content.Monsters
            .Where(monster => monster.Name.Contains(','))
            .Select(monster => monster.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Bestiary names containing a comma: {string.Join(", ", offenders)}. "
            + "RosterParser.Parse splits --spawn entries on ',' before reading a name, so "
            + "a comma-bearing name is severed into unparseable fragments rather than "
            + "refused by its real name. This is the #464/#412 corpus-invariant trip-wire "
            + "firing: either the roster grammar needs a quoting or escaping rule for "
            + "commas, or this name needs a decision from designer/architect before it "
            + "ships.");
    }

    [Fact]
    public void NoMultiWordBestiaryNameBeginsWithAnIntegerToken()
    {
        // Scoped to MULTI-WORD names deliberately: RosterParser only reads a leading
        // integer as a count when the entry has more than one word (its `words.Length > 1`
        // gate), so a single-token numeric name is parsed as a name, not a count, and is
        // NOT a counter-example. The invariant this asserts is exactly the parser's:
        // no multi-word name may begin with an int-parseable token.
        var offenders = Content.Monsters
            .Where(monster =>
            {
                var words = monster.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return words.Length > 1 && int.TryParse(words[0], out _);
            })
            .Select(monster => monster.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Multi-word bestiary names beginning with a token int.TryParse accepts: "
            + $"{string.Join(", ", offenders)}. RosterParser.Parse reads a multi-word "
            + "entry's leading count by parsing its first word as an integer, so a name "
            + "shaped the same way has its first word silently eaten as a count — the "
            + "remainder can even happen to match a *different* monster. This is the "
            + "#464/#412 corpus-invariant trip-wire firing: the grammar needs a decision "
            + "(a marker other than word position for counts) before this name ships.");
    }

    [Fact]
    public void NoTwoBestiaryNamesAreEqualIgnoringCase()
    {
        var duplicates = Content.Monsters
            .GroupBy(monster => monster.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.True(
            duplicates.Length == 0,
            $"Bestiary names equal ignoring case: {string.Join(", ", duplicates)}. "
            + "RosterParser.Parse matches names with FirstOrDefault under "
            + "OrdinalIgnoreCase, so a case-insensitive duplicate makes the match depend "
            + "silently on bestiary order — the second name becomes unreachable by "
            + "--spawn. This is the #464/#412 corpus-invariant trip-wire firing.");
    }
}
