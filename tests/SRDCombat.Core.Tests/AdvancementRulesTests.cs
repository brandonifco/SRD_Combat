using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests;

public class AdvancementRulesTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 300)]
    [InlineData(3, 900)]
    [InlineData(4, 2_700)]
    [InlineData(5, 6_500)]
    [InlineData(20, 355_000)]
    public void ExperienceToReach_MatchesTheCharacterAdvancementTable(int level, int expected) =>
        Assert.Equal(expected, AdvancementRules.ExperienceToReach(level));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(299, 1)]
    // Exactly on a threshold counts as having reached it.
    [InlineData(300, 2)]
    [InlineData(899, 2)]
    [InlineData(6_500, 5)]
    [InlineData(355_000, 20)]
    [InlineData(1_000_000, 20)]
    public void LevelForExperience_TurnsOverOnTheThreshold(int experience, int expected) =>
        Assert.Equal(expected, AdvancementRules.LevelForExperience(experience));

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(13, 5)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    public void ProficiencyBonusForLevel_StepsEveryFourLevels(int level, int expected) =>
        Assert.Equal(expected, AdvancementRules.ProficiencyBonusForLevel(level));

    [Fact]
    public void ExperienceToNextLevel_CountsTheRemainder()
    {
        Assert.Equal(300, AdvancementRules.ExperienceToNextLevel(0));
        Assert.Equal(1, AdvancementRules.ExperienceToNextLevel(899));

        // Nothing left to reach at the top of the table.
        Assert.Null(AdvancementRules.ExperienceToNextLevel(355_000));
    }

    [Fact]
    public void TheTableAndTheFormulaAgreeAtEveryLevel()
    {
        // Every class's own Features table prints the same proficiency bonus, and the
        // content validator checks extraction against this. If the two ever disagreed,
        // every class would fail validation at once — so it is worth pinning that this
        // is a total function over the whole range.
        for (var level = 1; level <= AdvancementRules.MaximumLevel; level++)
        {
            Assert.InRange(AdvancementRules.ProficiencyBonusForLevel(level), 2, 6);
            Assert.Equal(level, AdvancementRules.LevelForExperience(AdvancementRules.ExperienceToReach(level)));
        }
    }

    [Fact]
    public void OutOfRangeLevelsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AdvancementRules.ExperienceToReach(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdvancementRules.ExperienceToReach(21));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdvancementRules.ProficiencyBonusForLevel(0));
    }
}
