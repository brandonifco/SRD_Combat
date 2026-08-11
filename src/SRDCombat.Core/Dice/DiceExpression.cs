using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SRDCombat.Core.Dice;

/// <summary>
/// A dice expression of the form <c>NdS + M</c>, such as <c>2d6 + 3</c>, <c>1d12</c>,
/// or the flat <c>1</c> that the Blowgun's damage uses.
/// </summary>
/// <param name="Count">Number of dice. Zero for a flat value.</param>
/// <param name="Sides">Sides per die. Zero when <paramref name="Count"/> is zero.</param>
/// <param name="Modifier">Flat amount added after the roll. May be negative.</param>
public sealed partial record DiceExpression(int Count, int Sides, int Modifier)
{
    /// <summary>
    /// The SRD prints an average alongside every damage expression — <c>10 (2d6 + 3)</c>.
    /// Averages are rounded down, which is what those printed numbers use, so this
    /// doubles as a check that an extracted expression matches its printed average.
    /// </summary>
    public int Average => Count == 0
        ? Modifier
        : (int)Math.Floor((Count * ((Sides + 1) / 2.0)) + Modifier);

    /// <summary>Smallest result this expression can produce.</summary>
    public int Minimum => Count + Modifier;

    /// <summary>Largest result this expression can produce.</summary>
    public int Maximum => (Count * Sides) + Modifier;

    /// <summary>A flat value with no dice.</summary>
    public static DiceExpression Flat(int value) => new(0, 0, value);

    public static DiceExpression Parse(string text) =>
        TryParse(text, out var expression)
            ? expression
            : throw new FormatException($"'{text}' is not a dice expression.");

    /// <summary>
    /// Parses <c>2d6 + 3</c>, <c>2d6+3</c>, <c>1d12</c>, <c>2d6 - 1</c> or a bare
    /// integer. The SRD uses a Unicode minus (U+2212) in places, so both that and
    /// the ASCII hyphen are accepted.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out DiceExpression? expression)
    {
        expression = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Replace('−', '-').Replace('–', '-').Trim();

        var match = DicePattern().Match(normalized);
        if (!match.Success)
        {
            if (!int.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var flat))
            {
                return false;
            }

            expression = Flat(flat);
            return true;
        }

        var count = int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
        var sides = int.Parse(match.Groups["sides"].Value, CultureInfo.InvariantCulture);

        var modifier = 0;
        if (match.Groups["mod"].Success)
        {
            modifier = int.Parse(match.Groups["mod"].Value.Replace(" ", string.Empty), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        expression = new DiceExpression(count, sides, modifier);
        return true;
    }

    public override string ToString()
    {
        if (Count == 0)
        {
            return Modifier.ToString(CultureInfo.InvariantCulture);
        }

        var dice = $"{Count}d{Sides}";

        return Modifier switch
        {
            0 => dice,
            > 0 => $"{dice} + {Modifier}",
            _ => $"{dice} - {-Modifier}",
        };
    }

    [GeneratedRegex(@"^(?<count>\d+)\s*[dD]\s*(?<sides>\d+)(?:\s*(?<mod>[+-]\s*\d+))?$")]
    private static partial Regex DicePattern();
}
