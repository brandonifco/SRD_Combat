using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Rules;

/// <summary>
/// What a party earns for winning, and the level it buys.
/// </summary>
/// <remarks>
/// The thresholds and each monster's worth are printed; the step between them is not —
/// the SRD says only that experience is "awarded by the Game Master". So the division is
/// a stated interpretation, and the first test below is the argument for it: it makes the
/// SRD's two published tables agree with each other.
/// </remarks>
public class ExperienceRulesTests
{
    [Fact]
    public void AFullySpentEncounterPaysBackExactlyTheBudgetTablesFigure()
    {
        // The reason the award is divided rather than given whole to each character. An
        // encounter budget is "XP per character x party size", so dividing a
        // fully-spent encounter by the party size must return the per-character figure
        // the table printed — 50 for four level 1 characters at low difficulty.
        foreach (var level in new[] { 1, 3, 5 })
        {
            foreach (var difficulty in Enum.GetValues<EncounterDifficulty>())
            {
                const int partySize = 4;
                var budget = EncounterBudget.For(partySize, level, difficulty);
                var spentExactly = new[] { Monster("all", budget) };

                Assert.Equal(
                    EncounterBudget.PerCharacter(level, difficulty),
                    ExperienceRules.AwardPerCharacter(spentExactly, partySize));
            }
        }
    }

    [Fact]
    public void TheAwardIsSharedAmongTheCharactersWhoFought()
    {
        var defeated = new[] { Monster("a", 100), Monster("b", 100) };

        Assert.Equal(50, ExperienceRules.AwardPerCharacter(defeated, 4));
        Assert.Equal(200, ExperienceRules.AwardPerCharacter(defeated, 1));
    }

    [Fact]
    public void ASmallerPartyEarnsMoreEach()
    {
        // The arithmetic being honest rather than a consolation: a party that has lost
        // someone splits the same award fewer ways.
        var defeated = new[] { Monster("a", 300) };

        Assert.True(
            ExperienceRules.AwardPerCharacter(defeated, 3) > ExperienceRules.AwardPerCharacter(defeated, 4));
    }

    [Fact]
    public void TheAwardUsesPrintedExperienceNotTheChallengeRatingsWorth()
    {
        // The Archmage prints 8,000 XP where CR 12 is worth 8,400 — a real SRD
        // inconsistency. The award follows the stat block, as the encounter builder does.
        var archmageShaped = Monster("archmage", 8_000) with { ChallengeRating = 12m };

        Assert.NotEqual(
            ChallengeRatingRules.GetExperience(12m),
            ExperienceRules.AwardPerCharacter([archmageShaped], 1));
        Assert.Equal(8_000, ExperienceRules.AwardPerCharacter([archmageShaped], 1));
    }

    [Fact]
    public void WinningNothingEarnsNothing()
    {
        Assert.Equal(0, ExperienceRules.AwardPerCharacter([], 4));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 2)]
    [InlineData(900, 3)]
    [InlineData(2_700, 4)]
    [InlineData(6_500, 5)]
    public void TheLevelFollowsThePrintedThresholds(int experience, int expected) =>
        Assert.Equal(expected, ExperienceRules.LevelFor(experience));

    [Fact]
    public void LevellingStopsAtTheTierThisGameSupports()
    {
        // The SRD's table runs to 20; this game is scoped to tier 1, and resolving a
        // character against class rows nothing has exercised would be inventing content.
        Assert.Equal(AdvancementRules.MaximumSupportedLevel, ExperienceRules.LevelFor(1_000_000));
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
