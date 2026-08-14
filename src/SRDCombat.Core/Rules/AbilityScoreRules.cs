using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// Generating ability scores at character creation, from the printed "Generate Your
/// Scores" methods (Character Creation chapter, printed page 21).
/// </summary>
/// <remarks>
/// Two of the three printed methods are here. <b>Standard Array</b>: "Use the following
/// six scores for your abilities: 15, 14, 13, 12, 10, 8." <b>Point Cost</b>: "You have
/// 27 points to spend on your ability scores", against the printed Ability Score Point
/// Costs table — 8 costs 0, 9 costs 1, 10 costs 2, 11 costs 3, 12 costs 4, 13 costs 5,
/// 14 costs 7, 15 costs 9, transcribed from print the way <c>PotionRules</c> is. The
/// third method, Random Generation (4d6 drop lowest), is not offered yet: it is the
/// only one needing dice, and creation currently runs before a run's seed exists.
/// </remarks>
public static class AbilityScoreRules
{
    /// <summary>The printed Standard Array, highest first.</summary>
    public static IReadOnlyList<int> StandardArray { get; } = [15, 14, 13, 12, 10, 8];

    /// <summary>"You have 27 points to spend on your ability scores."</summary>
    public const int PointBudget = 27;

    /// <summary>The printed Ability Score Point Costs table.</summary>
    public static IReadOnlyDictionary<int, int> PointCosts { get; } =
        new Dictionary<int, int>
        {
            [8] = 0,
            [9] = 1,
            [10] = 2,
            [11] = 3,
            [12] = 4,
            [13] = 5,
            [14] = 7,
            [15] = 9,
        };

    /// <summary>
    /// The total point cost of a set of scores, or null when any score is outside the
    /// table — Point Cost can only buy 8 through 15.
    /// </summary>
    public static int? PointCost(IReadOnlyDictionary<Ability, int> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        var total = 0;

        foreach (var score in scores.Values)
        {
            if (!PointCosts.TryGetValue(score, out var cost))
            {
                return null;
            }

            total += cost;
        }

        return total;
    }

    /// <summary>Whether these six scores are a legal Point Cost purchase.</summary>
    public static bool IsLegalPointBuy(IReadOnlyDictionary<Ability, int> scores) =>
        scores.Count == 6 && PointCost(scores) is { } cost && cost <= PointBudget;

    /// <summary>
    /// Whether these six scores are the Standard Array, in some assignment. Order-free:
    /// the array is six numbers, and which ability holds which is the whole choice.
    /// </summary>
    public static bool IsStandardArrayAssignment(IReadOnlyDictionary<Ability, int> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        return scores.Count == 6
            && scores.Values.OrderByDescending(score => score)
                .SequenceEqual(StandardArray);
    }
}
