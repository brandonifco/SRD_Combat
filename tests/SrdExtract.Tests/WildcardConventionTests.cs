using SRDCombat.Content;
using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// The machine-checkable half of the wildcard convention design §2.3 states: every
/// permissive subexpression in a claiming pattern must be a named group. This is a
/// tripwire and a review prompt, not a proof — the naming half only, per the design's
/// own stated limit: "nothing verifies that a named group's content was genuinely read
/// into structure rather than named to quiet the scan."
/// </summary>
/// <remarks>
/// <b>Scope, stated because it is a judgment call (design §14).</b> "Permissive
/// subexpression" here means a literal <c>.</c> or an explicit bracket character class
/// <c>[...]</c> carrying an unbounded quantifier (<c>*</c>, <c>+</c>, or <c>{n,}</c>,
/// lazy or greedy) — exactly the design's own examples (<c>.*</c>, <c>[^.]+?</c>,
/// <c>[\w' ]*?</c>). A bare Perl shorthand class used alone — <c>\d+</c>, <c>\s*</c>,
/// <c>\w+</c> — is deliberately not in scope: unlike <c>.</c> or a hand-written bracket
/// class, it cannot match arbitrary prose, only digits or whitespace respectively, and
/// every claiming pattern in this project uses <c>\s+</c>/<c>\s*</c> between literal
/// tokens as a matter of course. Screening those too would make every claiming pattern
/// fail this test regardless of whether it over-claims, which would defeat the test's
/// purpose rather than serve it.
/// </remarks>
public sealed class WildcardConventionTests
{
    [Fact]
    public void EveryClaimingPatternWrapsItsPermissiveSubexpressionsInANamedGroup()
    {
        // Populate EntryCoverage.ClaimingPatterns — a process-wide registry filled only
        // by the Claim(Regex, Match, ...) overload — by exercising the whole corpus, so
        // the scan reaches exactly the patterns actually used for coverage rather than
        // a hand-maintained list (design §2.3).
        var monsters = ContentLoader.Load(RepositoryPaths.SrdContentDirectory).Monsters;

        foreach (var monster in monsters)
        {
            foreach (var entry in monster.Entries)
            {
                EntryMechanicsParser.Classify(entry.Name, entry.Section, entry.Text, out _);
            }
        }

        Assert.NotEmpty(EntryCoverage.ClaimingPatterns);

        var violations = new List<string>();

        foreach (var pattern in EntryCoverage.ClaimingPatterns)
        {
            violations.AddRange(
                FindUnnamedPermissiveSubexpressions(pattern)
                    .Select(position => $"'{pattern}' at character {position}"));
        }

        Assert.True(
            violations.Count == 0,
            "Unbounded quantifier on a dot or bracket character class outside a named group:\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>
    /// Finds every position where an unbounded quantifier is applied to a <c>.</c> or a
    /// <c>[...]</c> class that does not fall inside a named capturing group
    /// <c>(?&lt;name&gt;...)</c>. A best-effort scan over the closed, hand-written set of
    /// patterns this project actually uses — not a general regex parser.
    /// </summary>
    private static IEnumerable<int> FindUnnamedPermissiveSubexpressions(string pattern)
    {
        var namedGroupSpans = FindNamedGroupSpans(pattern);
        var violations = new List<int>();

        var i = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '\\')
            {
                i += 2;
                continue;
            }

            int unitStart;
            int unitEnd;

            if (c == '.')
            {
                unitStart = i;
                unitEnd = i + 1;
            }
            else if (c == '[')
            {
                var closing = FindClosingBracket(pattern, i);

                if (closing < 0)
                {
                    break;
                }

                unitStart = i;
                unitEnd = closing + 1;
            }
            else
            {
                i++;
                continue;
            }

            if (IsFollowedByUnboundedQuantifier(pattern, unitEnd)
                && !namedGroupSpans.Any(span => unitStart >= span.Start && unitStart < span.End))
            {
                violations.Add(unitStart);
            }

            i = unitEnd;
        }

        return violations;
    }

    private static bool IsFollowedByUnboundedQuantifier(string pattern, int position)
    {
        if (position >= pattern.Length)
        {
            return false;
        }

        if (pattern[position] is '*' or '+')
        {
            return true;
        }

        if (pattern[position] != '{')
        {
            return false;
        }

        // "{n,}" — a lower bound with no upper one. "{n}" and "{n,m}" are bounded and
        // not the shape this convention is about.
        return System.Text.RegularExpressions.Regex.IsMatch(pattern[position..], @"^\{\d+,\}");
    }

    /// <summary>The index just past a <c>[...]</c> class's closing bracket, or -1 if unterminated.</summary>
    private static int FindClosingBracket(string pattern, int openIndex)
    {
        var i = openIndex + 1;

        // A leading "^" (negation) and/or a "]" immediately after "[" or "[^" is a
        // literal ']', not the class's own close.
        if (i < pattern.Length && pattern[i] == '^')
        {
            i++;
        }

        if (i < pattern.Length && pattern[i] == ']')
        {
            i++;
        }

        while (i < pattern.Length)
        {
            if (pattern[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (pattern[i] == ']')
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    /// <summary>
    /// Every <c>(?&lt;name&gt;...)</c> or <c>(?'name'...)</c> group's own span, including
    /// its parentheses. Tracks parenthesis depth for every group — named, unnamed and
    /// lookaround alike — so a named group's closing paren is matched correctly however
    /// deeply it nests, but only named groups are reported.
    /// </summary>
    private static List<(int Start, int End)> FindNamedGroupSpans(string pattern)
    {
        var spans = new List<(int Start, int End)>();
        var starts = new Stack<int>();

        var i = 0;

        while (i < pattern.Length)
        {
            if (pattern[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (pattern[i] == '[')
            {
                var closing = FindClosingBracket(pattern, i);
                i = closing < 0 ? pattern.Length : closing + 1;
                continue;
            }

            if (pattern[i] == '(')
            {
                var isNamed = i + 2 < pattern.Length
                    && pattern[i + 1] == '?'
                    && (pattern[i + 2] == '<' || pattern[i + 2] == '\'')
                    && !(pattern[i + 2] == '<'
                        && i + 3 < pattern.Length
                        && pattern[i + 3] is '=' or '!'); // exclude (?<= and (?<! lookbehind

                starts.Push(isNamed ? i : -1);
                i++;
                continue;
            }

            if (pattern[i] == ')')
            {
                if (starts.Count > 0)
                {
                    var start = starts.Pop();

                    if (start >= 0)
                    {
                        spans.Add((start, i + 1));
                    }
                }

                i++;
                continue;
            }

            i++;
        }

        return spans;
    }
}
