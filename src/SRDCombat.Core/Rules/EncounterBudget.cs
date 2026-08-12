namespace SRDCombat.Core.Rules;

/// <summary>
/// The three difficulties the SRD's encounter guidelines describe.
/// </summary>
/// <remarks>
/// The printed descriptions, because they are what the numbers mean. <b>Low:</b> "one or
/// two scary moments ... their characters should emerge victorious with no casualties."
/// <b>Moderate:</b> "could go badly for the adventurers. Weaker characters might get
/// taken out of the fight, and there's a slim chance that one or more characters might
/// die." <b>High:</b> "could be lethal for one or more characters."
/// </remarks>
public enum EncounterDifficulty
{
    Low,
    Moderate,
    High,
}

/// <summary>
/// The SRD's XP Budget per Character table, and the arithmetic that turns it into an
/// encounter's budget.
/// </summary>
/// <remarks>
/// <para>
/// Printed page 202, reproduced exactly. This is a published table rather than a
/// balancing heuristic of this project's own, which is the whole reason the encounter
/// builder can claim to produce a fair fight at all.
/// </para>
/// <para>
/// The procedure the SRD gives is three steps, and each lands somewhere different in the
/// code. <b>Step 1</b> choose a difficulty — the caller's. <b>Step 2</b> cross-reference
/// the party's level with that difficulty and "multiply the number in the table by the
/// number of characters in the party" — <see cref="For"/>. <b>Step 3</b> spend it, which
/// is <see cref="EncounterBuilder"/>.
/// </para>
/// </remarks>
public static class EncounterBudget
{
    /// <summary>XP per character by level, in Low / Moderate / High order.</summary>
    private static readonly IReadOnlyDictionary<int, (int Low, int Moderate, int High)> PerCharacterByLevel =
        new Dictionary<int, (int, int, int)>
        {
            [1] = (50, 75, 100),
            [2] = (100, 150, 200),
            [3] = (150, 225, 400),
            [4] = (250, 375, 500),
            [5] = (500, 750, 1_100),
            [6] = (600, 1_000, 1_400),
            [7] = (750, 1_300, 1_700),
            [8] = (1_000, 1_700, 2_100),
            [9] = (1_300, 2_000, 2_600),
            [10] = (1_600, 2_300, 3_100),
            [11] = (1_900, 2_900, 4_100),
            [12] = (2_200, 3_700, 4_700),
            [13] = (2_600, 4_200, 5_400),
            [14] = (2_900, 4_900, 6_200),
            [15] = (3_300, 5_400, 7_800),
            [16] = (3_800, 6_100, 9_800),
            [17] = (4_500, 7_200, 11_700),
            [18] = (5_000, 8_700, 14_200),
            [19] = (5_500, 10_700, 17_200),
            [20] = (6_400, 13_200, 22_000),
        };

    /// <summary>The highest level the printed table covers.</summary>
    public const int MaximumLevel = 20;

    /// <summary>The table's own figure: XP per character at a level and difficulty.</summary>
    public static int PerCharacter(int level, EncounterDifficulty difficulty)
    {
        if (!PerCharacterByLevel.TryGetValue(level, out var row))
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                $"The XP budget table covers levels 1-{MaximumLevel}.");
        }

        return difficulty switch
        {
            EncounterDifficulty.Low => row.Low,
            EncounterDifficulty.Moderate => row.Moderate,
            EncounterDifficulty.High => row.High,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Unknown difficulty."),
        };
    }

    /// <summary>
    /// The whole encounter's budget: the table's figure times the number of characters.
    /// </summary>
    /// <remarks>
    /// "Multiply the number in the table by the number of characters in the party to get
    /// your XP budget for the encounter." A party of mixed levels is not something the
    /// printed procedure addresses, and this game's party levels together, so the level
    /// is one number rather than an average that the book never asks for.
    /// </remarks>
    public static int For(int partySize, int level, EncounterDifficulty difficulty)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(partySize, 1);

        return PerCharacter(level, difficulty) * partySize;
    }

    /// <summary>
    /// The budget for a party whose members are not all the same level.
    /// </summary>
    /// <remarks>
    /// A stated generalisation: the printed procedure says "cross-reference the party's
    /// level", assuming one level for everybody, and multiplying by the party size is
    /// only shorthand for adding up a figure that happens to be the same each time. So a
    /// mixed party sums each character's own figure, which reduces to exactly the printed
    /// arithmetic when they are all the same level. Parties diverge in this game when a
    /// character dies and stops earning, so this is reachable rather than theoretical.
    /// </remarks>
    public static int ForLevels(IEnumerable<int> levels, EncounterDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var total = levels.Sum(level => PerCharacter(level, difficulty));

        return total > 0
            ? total
            : throw new ArgumentException("A budget needs at least one character.", nameof(levels));
    }
}
