namespace SRDCombat.Core.Rules;

/// <summary>
/// The SRD's Character Advancement table: the experience needed for each level, and the
/// proficiency bonus that comes with it.
/// </summary>
/// <remarks>
/// This is the spine of the gauntlet's progression, and it doubles as a check on
/// extraction: every class's Features table prints the same proficiency bonus by level,
/// so a class table that disagrees with this one was misread. That makes it the
/// character-side equivalent of hit-points-versus-hit-dice for monsters.
/// </remarks>
public static class AdvancementRules
{
    /// <summary>The highest level this game supports. Tier 1, per the design decision.</summary>
    public const int MaximumSupportedLevel = 5;

    /// <summary>The highest level the SRD's own table defines.</summary>
    public const int MaximumLevel = 20;

    /// <summary>Experience needed to reach each level, indexed from level 1.</summary>
    private static readonly int[] ExperienceForLevel =
    [
        0,        // 1
        300,      // 2
        900,      // 3
        2_700,    // 4
        6_500,    // 5
        14_000,   // 6
        23_000,   // 7
        34_000,   // 8
        48_000,   // 9
        64_000,   // 10
        85_000,   // 11
        100_000,  // 12
        120_000,  // 13
        140_000,  // 14
        165_000,  // 15
        195_000,  // 16
        225_000,  // 17
        265_000,  // 18
        305_000,  // 19
        355_000,  // 20
    ];

    /// <summary>The experience total needed to reach a level.</summary>
    public static int ExperienceToReach(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, MaximumLevel);

        return ExperienceForLevel[level - 1];
    }

    /// <summary>
    /// The level a character with this much experience has reached.
    /// </summary>
    public static int LevelForExperience(int experience)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(experience);

        var level = 1;

        while (level < MaximumLevel && experience >= ExperienceForLevel[level])
        {
            level++;
        }

        return level;
    }

    /// <summary>
    /// The proficiency bonus at a given character level: +2 at levels 1–4, then one more
    /// every four levels.
    /// </summary>
    public static int ProficiencyBonusForLevel(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, MaximumLevel);

        return 2 + ((level - 1) / 4);
    }

    /// <summary>
    /// Experience still needed to reach the next level, or null at the maximum.
    /// </summary>
    public static int? ExperienceToNextLevel(int experience)
    {
        var level = LevelForExperience(experience);

        return level >= MaximumLevel ? null : ExperienceForLevel[level] - experience;
    }
}
