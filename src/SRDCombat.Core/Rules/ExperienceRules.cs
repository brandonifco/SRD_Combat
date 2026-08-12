using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// What a party earns for winning a fight.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one piece of the advancement chain the SRD does not publish.</b> It
/// prints the thresholds ("Character Advancement", levels 1–20), it prints each monster's
/// worth ("The Experience Points characters receive for defeating a monster"), and for
/// the step between them it says only that XP are "awarded by the Game Master". So the
/// division below is a stated interpretation, in the same spirit as
/// <c>AreaTargeting</c>'s geometry — written down rather than presented as a derivation.
/// </para>
/// <para>
/// <b>The reading: a defeated monster's printed XP is split evenly among the characters
/// who fought it.</b> The argument for it is that it makes the SRD's two published tables
/// agree. The encounter budget is "XP per character × the number of characters", so
/// dividing a fully-spent encounter back by the party size returns exactly the per-character
/// figure the budget table printed. A low-difficulty fight for four level 1 characters
/// costs 200 XP and hands each of them the 50 the table names. Awarding every character
/// the undivided total would make that number mean two different things in two places.
/// </para>
/// <para>
/// Integer division drops at most one XP per character short of a whole share. Recorded
/// rather than fixed: it is a rounding of a number the book does not define at all, and
/// carrying fractional experience to avoid it would be precision this rule has not earned.
/// </para>
/// </remarks>
public static class ExperienceRules
{
    /// <summary>
    /// Experience each surviving character earns for defeating these monsters.
    /// </summary>
    /// <param name="defeated">The monsters beaten. Their <em>printed</em> XP is used.</param>
    /// <param name="characterCount">How many characters share it.</param>
    public static int AwardPerCharacter(IEnumerable<MonsterDefinition> defeated, int characterCount)
    {
        ArgumentNullException.ThrowIfNull(defeated);
        ArgumentOutOfRangeException.ThrowIfLessThan(characterCount, 1);

        return defeated.Sum(monster => monster.ExperiencePoints) / characterCount;
    }

    /// <summary>
    /// The level a character has earned, capped at what this game supports.
    /// </summary>
    /// <remarks>
    /// The SRD's table runs to level 20; this game is scoped to tier 1, so a character
    /// with enough experience for level 6 stays at 5 rather than resolving against class
    /// rows this project has never exercised.
    /// </remarks>
    public static int LevelFor(int experience) =>
        Math.Min(AdvancementRules.LevelForExperience(experience), AdvancementRules.MaximumSupportedLevel);
}
