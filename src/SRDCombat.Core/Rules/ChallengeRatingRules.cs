namespace SRDCombat.Core.Rules;

/// <summary>
/// The SRD's Challenge Rating table: what a monster of a given CR is worth in XP, and
/// what proficiency bonus it uses.
/// </summary>
/// <remarks>
/// These are load-bearing rather than reference data. XP is what the encounter builder
/// spends against the SRD's per-character budget, and both values double as a check on
/// extraction — a stat block whose printed XP disagrees with its printed CR was almost
/// certainly parsed wrong.
/// </remarks>
public static class ChallengeRatingRules
{
    private static readonly IReadOnlyDictionary<decimal, int> ExperienceByRating =
        new Dictionary<decimal, int>
        {
            [0m] = 10,
            [0.125m] = 25,
            [0.25m] = 50,
            [0.5m] = 100,
            [1m] = 200,
            [2m] = 450,
            [3m] = 700,
            [4m] = 1_100,
            [5m] = 1_800,
            [6m] = 2_300,
            [7m] = 2_900,
            [8m] = 3_900,
            [9m] = 5_000,
            [10m] = 5_900,
            [11m] = 7_200,
            [12m] = 8_400,
            [13m] = 10_000,
            [14m] = 11_500,
            [15m] = 13_000,
            [16m] = 15_000,
            [17m] = 18_000,
            [18m] = 20_000,
            [19m] = 22_000,
            [20m] = 25_000,
            [21m] = 33_000,
            [22m] = 41_000,
            [23m] = 50_000,
            [24m] = 62_000,
            [25m] = 75_000,
            [26m] = 90_000,
            [27m] = 105_000,
            [28m] = 120_000,
            [29m] = 135_000,
            [30m] = 155_000,
        };

    /// <summary>Every challenge rating the SRD defines, ascending.</summary>
    public static IReadOnlyList<decimal> AllRatings { get; } =
        ExperienceByRating.Keys.OrderBy(rating => rating).ToArray();

    /// <summary>True when <paramref name="rating"/> is one the SRD actually defines.</summary>
    public static bool IsDefined(decimal rating) => ExperienceByRating.ContainsKey(rating);

    /// <summary>
    /// XP awarded for defeating a monster of this rating.
    /// </summary>
    /// <remarks>
    /// A CR 0 monster is worth 10 XP here. The SRD also allows 0 for a CR 0 creature
    /// that poses no threat at all, which is why the extraction check treats a printed
    /// 0 as acceptable rather than as a mismatch.
    /// </remarks>
    public static int GetExperience(decimal rating) =>
        ExperienceByRating.TryGetValue(rating, out var experience)
            ? experience
            : throw new ArgumentOutOfRangeException(
                nameof(rating),
                rating,
                "Not a challenge rating the SRD defines.");

    /// <summary>
    /// The proficiency bonus a monster of this rating uses: +2 through CR 4, then one
    /// more for every four ratings.
    /// </summary>
    public static int GetProficiencyBonus(decimal rating)
    {
        if (!IsDefined(rating))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rating),
                rating,
                "Not a challenge rating the SRD defines.");
        }

        return rating <= 4m ? 2 : 2 + (int)Math.Ceiling((rating - 4m) / 4m);
    }
}
