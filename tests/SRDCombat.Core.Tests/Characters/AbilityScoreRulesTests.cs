using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Characters;

/// <summary>
/// The printed score-generation methods: the Standard Array and the 27-point Point
/// Cost table (Character Creation chapter, printed page 21).
/// </summary>
public class AbilityScoreRulesTests
{
    [Fact]
    public void TheStandardArrayIsThePrintedSix() =>
        Assert.Equal([15, 14, 13, 12, 10, 8], AbilityScoreRules.StandardArray);

    [Fact]
    public void AnyAssignmentOfTheArrayQualifies()
    {
        Assert.True(AbilityScoreRules.IsStandardArrayAssignment(Scores(8, 10, 12, 13, 14, 15)));
        Assert.False(AbilityScoreRules.IsStandardArrayAssignment(Scores(15, 15, 13, 12, 10, 8)));
    }

    [Fact]
    public void TheCostTableMatchesPrint()
    {
        Assert.Equal(0, AbilityScoreRules.PointCosts[8]);
        Assert.Equal(1, AbilityScoreRules.PointCosts[9]);
        Assert.Equal(2, AbilityScoreRules.PointCosts[10]);
        Assert.Equal(3, AbilityScoreRules.PointCosts[11]);
        Assert.Equal(4, AbilityScoreRules.PointCosts[12]);
        Assert.Equal(5, AbilityScoreRules.PointCosts[13]);
        Assert.Equal(7, AbilityScoreRules.PointCosts[14]);
        Assert.Equal(9, AbilityScoreRules.PointCosts[15]);
    }

    [Fact]
    public void TheStandardArrayCostsExactlyTheBudget()
    {
        // Not printed, but true, and worth pinning: the two methods agree at the top —
        // buying the array spends 27 points to the coin.
        Assert.Equal(
            AbilityScoreRules.PointBudget,
            AbilityScoreRules.PointCost(Scores(15, 14, 13, 12, 10, 8)));
    }

    [Fact]
    public void AScoreOutsideTheTableCannotBeBought()
    {
        Assert.Null(AbilityScoreRules.PointCost(Scores(16, 14, 13, 12, 10, 8)));
        Assert.Null(AbilityScoreRules.PointCost(Scores(7, 14, 13, 12, 10, 8)));
        Assert.False(AbilityScoreRules.IsLegalPointBuy(Scores(16, 14, 13, 12, 10, 8)));
    }

    [Fact]
    public void OverspendingIsIllegalAndUnderspendingIsNot()
    {
        // Three 15s and three 8s is exactly 27 — legal, and a nice proof the budget is
        // computed rather than pattern-matched. One more point tips it over.
        Assert.True(AbilityScoreRules.IsLegalPointBuy(Scores(15, 15, 15, 8, 8, 8)));
        Assert.False(AbilityScoreRules.IsLegalPointBuy(Scores(15, 15, 15, 9, 8, 8)));
        Assert.True(AbilityScoreRules.IsLegalPointBuy(Scores(8, 8, 8, 8, 8, 8)));
    }

    private static Dictionary<Ability, int> Scores(int str, int dex, int con, int intelligence, int wis, int cha) =>
        new()
        {
            [Ability.Strength] = str,
            [Ability.Dexterity] = dex,
            [Ability.Constitution] = con,
            [Ability.Intelligence] = intelligence,
            [Ability.Wisdom] = wis,
            [Ability.Charisma] = cha,
        };
}
