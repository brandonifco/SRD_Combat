using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Rules;

/// <summary>
/// The SRD's encounter-building procedure: the XP budget table, and spending it.
/// </summary>
/// <remarks>
/// The printed page gives two fully worked examples with their answers, which is the
/// best kind of test material available — the book checking the code rather than the
/// code checking itself. Both are pinned below.
/// </remarks>
public class EncounterBudgetTests
{
    [Fact]
    public void TheBooksFirstWorkedExampleComesOut()
    {
        // "A low-difficulty encounter for four level 1 characters has an XP budget of
        // 50 x 4, for a total of 200 XP."
        Assert.Equal(50, EncounterBudget.PerCharacter(1, EncounterDifficulty.Low));
        Assert.Equal(200, EncounterBudget.For(partySize: 4, level: 1, EncounterDifficulty.Low));
    }

    [Fact]
    public void TheBooksSecondWorkedExampleComesOut()
    {
        // "A moderate-difficulty encounter for five level 3 characters has an XP budget
        // of 225 x 5, for a total of 1,125 XP."
        Assert.Equal(225, EncounterBudget.PerCharacter(3, EncounterDifficulty.Moderate));
        Assert.Equal(1_125, EncounterBudget.For(partySize: 5, level: 3, EncounterDifficulty.Moderate));
    }

    [Theory]
    [InlineData(1, 50, 75, 100)]
    [InlineData(5, 500, 750, 1_100)]
    [InlineData(11, 1_900, 2_900, 4_100)]
    [InlineData(20, 6_400, 13_200, 22_000)]
    public void TheTableMatchesThePrintedRows(int level, int low, int moderate, int high)
    {
        Assert.Equal(low, EncounterBudget.PerCharacter(level, EncounterDifficulty.Low));
        Assert.Equal(moderate, EncounterBudget.PerCharacter(level, EncounterDifficulty.Moderate));
        Assert.Equal(high, EncounterBudget.PerCharacter(level, EncounterDifficulty.High));
    }

    [Fact]
    public void DifficultyRisesWithinEveryLevel()
    {
        // A property of the printed table worth asserting rather than eyeballing across
        // sixty transcribed numbers: no row is out of order.
        for (var level = 1; level <= EncounterBudget.MaximumLevel; level++)
        {
            var low = EncounterBudget.PerCharacter(level, EncounterDifficulty.Low);
            var moderate = EncounterBudget.PerCharacter(level, EncounterDifficulty.Moderate);
            var high = EncounterBudget.PerCharacter(level, EncounterDifficulty.High);

            Assert.True(low < moderate, $"Level {level}: low {low} is not below moderate {moderate}.");
            Assert.True(moderate < high, $"Level {level}: moderate {moderate} is not below high {high}.");
        }
    }

    [Fact]
    public void BudgetsRiseWithLevelAtEveryDifficulty()
    {
        foreach (var difficulty in Enum.GetValues<EncounterDifficulty>())
        {
            for (var level = 2; level <= EncounterBudget.MaximumLevel; level++)
            {
                Assert.True(
                    EncounterBudget.PerCharacter(level, difficulty)
                        > EncounterBudget.PerCharacter(level - 1, difficulty),
                    $"{difficulty} at level {level} is not above level {level - 1}.");
            }
        }
    }

    [Fact]
    public void ALevelOutsideTheTableIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EncounterBudget.PerCharacter(0, EncounterDifficulty.Low));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncounterBudget.PerCharacter(21, EncounterDifficulty.Low));
    }

    [Fact]
    public void SpendingNeverGoesOverBudget()
    {
        // "Spend as much of your XP budget as you can without going over."
        var candidates = new[] { Monster("a", 25), Monster("b", 50), Monster("c", 200) };

        foreach (var seed in Enumerable.Range(1, 40))
        {
            var built = EncounterBuilder.Build(candidates, budget: 200, new SeededRandomSource(seed));

            Assert.True(built.Spent <= built.Budget, $"Seed {seed} overspent: {built.Spent} of {built.Budget}.");
            Assert.Equal(built.Spent, built.Monsters.Sum(monster => monster.ExperiencePoints));
            Assert.True(built.Remaining >= 0);
        }
    }

    [Fact]
    public void SpendingStopsOnlyWhenNothingAffordableIsLeft()
    {
        // "It's OK if you have a few unspent XP left over" — but not a lot: whatever is
        // left must be less than the cheapest thing that could have been bought, or the
        // builder gave up early.
        var candidates = new[] { Monster("a", 25), Monster("b", 50), Monster("c", 200) };

        foreach (var seed in Enumerable.Range(1, 40))
        {
            var built = EncounterBuilder.Build(candidates, budget: 500, new SeededRandomSource(seed));

            if (built.Monsters.Count < EncounterBuilder.DefaultMaximumMonsters)
            {
                Assert.True(
                    built.Remaining < 25,
                    $"Seed {seed} left {built.Remaining} unspent with a 25 XP monster available.");
            }
        }
    }

    [Fact]
    public void TheCountCapIsRespected()
    {
        // A budget that could buy forty 10 XP creatures must not field forty of them.
        var built = EncounterBuilder.Build(
            [Monster("a", 10)],
            budget: 400,
            new SeededRandomSource(1),
            maximumMonsters: 3);

        Assert.Equal(3, built.Monsters.Count);
        Assert.Equal(30, built.Spent);
    }

    [Fact]
    public void ABudgetTooSmallForAnythingBuysNothing()
    {
        var built = EncounterBuilder.Build([Monster("a", 50)], budget: 10, new SeededRandomSource(1));

        Assert.Empty(built.Monsters);
        Assert.Equal(0, built.Spent);
        Assert.Equal(10, built.Remaining);
    }

    [Fact]
    public void TheSameSeedBuildsTheSameEncounter()
    {
        // Reproducibility is what makes "it happened on seed 12345" a complete report,
        // and it must not depend on the order the candidates happen to arrive in.
        var candidates = new[] { Monster("a", 25), Monster("b", 50), Monster("c", 100) };

        var first = EncounterBuilder.Build(candidates, 200, new SeededRandomSource(7));
        var second = EncounterBuilder.Build(candidates.Reverse(), 200, new SeededRandomSource(7));

        Assert.Equal(
            first.Monsters.Select(monster => monster.Id),
            second.Monsters.Select(monster => monster.Id));
    }

    [Fact]
    public void ACreatureWorthNoExperienceIsNeverChosen()
    {
        // Zero-XP creatures would be free, so the builder would take them forever
        // without spending anything.
        var built = EncounterBuilder.Build(
            [Monster("free", 0), Monster("real", 50)],
            budget: 100,
            new SeededRandomSource(3));

        Assert.All(built.Monsters, monster => Assert.Equal("real", monster.Id));
    }

    private static MonsterDefinition Monster(string id, int experiencePoints) => new()
    {
        Id = id,
        Name = id,
        Sizes = [CreatureSize.Medium],
        Type = CreatureType.Beast,
        Alignment = "Unaligned",
        ArmorClass = 12,
        InitiativeBonus = 0,
        HitPoints = 10,
        HitDice = DiceExpression.Parse("2d8"),
        Speeds = new Dictionary<MovementMode, int> { [MovementMode.Walk] = 30 },
        Abilities = new Dictionary<Ability, MonsterAbility>(),
        Skills = new Dictionary<string, int>(),
        DamageResponses = new Dictionary<DamageType, DamageResponse>(),
        ConditionImmunities = [],
        Senses = [],
        PassivePerception = 10,
        Languages = [],
        Gear = [],
        ChallengeRating = 1m,
        ExperiencePoints = experiencePoints,
        ProficiencyBonus = 2,
        Entries = [],
        SourcePage = 1,
    };
}
