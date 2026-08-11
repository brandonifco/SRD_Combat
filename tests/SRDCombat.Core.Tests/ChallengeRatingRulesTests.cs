using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests;

public class ChallengeRatingRulesTests
{
    [Theory]
    [InlineData(0.125, 25)]
    [InlineData(0.25, 50)]
    [InlineData(0.5, 100)]
    [InlineData(1, 200)]
    [InlineData(2, 450)]
    [InlineData(4, 1_100)]
    [InlineData(14, 11_500)]
    [InlineData(30, 155_000)]
    public void GetExperience_MatchesTheSrdTable(double rating, int expected) =>
        Assert.Equal(expected, ChallengeRatingRules.GetExperience((decimal)rating));

    [Theory]
    // The bonus steps up every four ratings; these pin both sides of each boundary.
    [InlineData(0, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(12, 4)]
    [InlineData(13, 5)]
    [InlineData(16, 5)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    [InlineData(21, 7)]
    [InlineData(30, 9)]
    public void GetProficiencyBonus_StepsAtTheRightRatings(double rating, int expected) =>
        Assert.Equal(expected, ChallengeRatingRules.GetProficiencyBonus((decimal)rating));

    [Fact]
    public void GetProficiencyBonus_IsTwoForEveryFractionalRating() =>
        Assert.All(
            new[] { 0.125m, 0.25m, 0.5m },
            rating => Assert.Equal(2, ChallengeRatingRules.GetProficiencyBonus(rating)));

    [Fact]
    public void AllRatings_CoversTheWholeTableInOrder()
    {
        Assert.Equal(34, ChallengeRatingRules.AllRatings.Count);
        Assert.Equal(0m, ChallengeRatingRules.AllRatings[0]);
        Assert.Equal(30m, ChallengeRatingRules.AllRatings[^1]);
        Assert.Equal(ChallengeRatingRules.AllRatings.OrderBy(rating => rating), ChallengeRatingRules.AllRatings);
    }

    [Fact]
    public void GetExperience_RejectsARatingTheSrdDoesNotDefine() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ChallengeRatingRules.GetExperience(3.5m));
}
