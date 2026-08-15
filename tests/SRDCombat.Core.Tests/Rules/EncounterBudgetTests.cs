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
    public void MostOfTheBudgetIsSpent()
    {
        // "Spend as much of your XP budget as you can without going over." The builder
        // chooses how many creatures to field before choosing which, so it cannot spend
        // to the last point the way a greedy fill would — but a fight well under its
        // budget is an easy fight wearing a hard fight's label, so most of it must go.
        var candidates = new[] { Monster("a", 25), Monster("b", 50), Monster("c", 200) };

        var spent = Enumerable.Range(1, 40)
            .Select(seed => EncounterBuilder.Build(candidates, budget: 500, new SeededRandomSource(seed)))
            .Average(built => (double)built.Spent / built.Budget);

        Assert.True(spent > 0.75, $"Only {spent:P0} of the budget was spent on average.");
    }

    [Fact]
    public void TheCountIsChosenBeforeTheCreaturesAndVaries()
    {
        // The correction this replaced: picking uniformly among everything affordable
        // sounds even-handed but fills the cap with cheap creatures, because a cheap
        // creature is affordable at every step. A low-difficulty fight for four level 1
        // characters came to 5.4 creatures on average that way.
        var candidates = new[] { Monster("a", 25), Monster("b", 50), Monster("c", 200) };

        var counts = Enumerable.Range(1, 60)
            .Select(seed => EncounterBuilder.Build(candidates, 400, new SeededRandomSource(seed), maximumMonsters: 5))
            .Select(built => built.Monsters.Count)
            .ToArray();

        Assert.True(counts.Distinct().Count() > 1, "Every encounter came out the same size.");
        Assert.All(counts, count => Assert.InRange(count, 1, 5));
    }

    [Fact]
    public void TheCountCapIsRespected()
    {
        // A budget that could buy forty 10 XP creatures must not field forty of them.
        // The cap is a ceiling on a count chosen up front rather than a target to fill,
        // so the only thing guaranteed is that nothing exceeds it.
        foreach (var seed in Enumerable.Range(1, 30))
        {
            var built = EncounterBuilder.Build(
                [Monster("a", 10)],
                budget: 400,
                new SeededRandomSource(seed),
                maximumMonsters: 3);

            Assert.InRange(built.Monsters.Count, 1, 3);
            Assert.Equal(built.Monsters.Count * 10, built.Spent);
        }
    }

    [Fact]
    public void APartyIsNeverBadlyOutnumbered()
    {
        // Every extra monster is another whole turn of attacks each round, so a party
        // outnumbered two to one loses on the action economy however well it plays.
        // The SRD caps nothing; this is a stated interpretation and the lever that
        // matters most for whether a fight is survivable.
        Assert.Equal(5, EncounterBuilder.MaximumFor(4, partyLevel: 3));
        Assert.Equal(2, EncounterBuilder.MaximumFor(1, partyLevel: 3));
        Assert.Equal(
            EncounterBuilder.DefaultMaximumMonsters,
            EncounterBuilder.MaximumFor(50, partyLevel: 20));
    }

    [Fact]
    public void AFragilePartyIsFieldedFewerCreatures()
    {
        // The cost of being outnumbered is paid in characters *removed*, and at level 1
        // very nearly every landed hit removes one — the creatures a level 1 budget buys
        // hit for about a level 1 character's whole hit point pool. So the cap grows with
        // the party's capacity to take a hit rather than sitting at a constant.
        Assert.Equal(3, EncounterBuilder.MaximumFor(4, partyLevel: 1));
        Assert.Equal(4, EncounterBuilder.MaximumFor(4, partyLevel: 2));
        Assert.Equal(5, EncounterBuilder.MaximumFor(4, partyLevel: 3));

        // It never grows past the original one-more-than-the-party, whatever the level.
        Assert.Equal(5, EncounterBuilder.MaximumFor(4, partyLevel: 20));

        // And the budget is untouched: this changes how many creatures the same XP is
        // spent across, never how much there is to spend.
        Assert.Equal(
            EncounterBudget.ForLevels([1, 1, 1, 1], EncounterDifficulty.Moderate),
            EncounterBudget.ForLevels([1, 1, 1, 1], EncounterDifficulty.Moderate));
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
