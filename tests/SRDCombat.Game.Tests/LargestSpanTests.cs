using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The largest footprint the pool can field, and the promise that it is derived rather
/// than written down (#429, criterion 7).
/// </summary>
public class LargestSpanTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    /// <summary>
    /// Today's answer is 3, on the Awakened Tree's Huge space.
    /// </summary>
    /// <remarks>
    /// Pinned exactly rather than as a floor, and it is <em>expected to move</em>: this
    /// test is how the project finds out. If coverage growth ever admits a Gargantuan
    /// creature at CR 4 or below the answer becomes 4, and the failure is the
    /// notification that terrain generation is now guaranteeing wider routes — which is a
    /// thing somebody should read a pacing run about rather than discover in a played
    /// fight.
    /// </remarks>
    [Fact]
    public void TheTierOnePoolsLargestFootprintIsThreeSquares() =>
        Assert.Equal(3, MonsterPool.LargestSpan(Content.Monsters, maximumChallengeRating: 4m));

    [Fact]
    public void ItIsDerivedFromTheSameFiltersThatDecideAdmission()
    {
        var pool = MonsterPool.Draw(Content.Monsters, maximumChallengeRating: 4m);

        var byHand = pool
            .Select(monster => CreatureSizeRules.SpaceSpanSquares(monster.Sizes[0]))
            .Max();

        Assert.Equal(byHand, MonsterPool.LargestSpan(Content.Monsters, maximumChallengeRating: 4m));

        // And it moves with the filters rather than being a constant wearing a method's
        // clothes: relaxing the coverage floor admits more creatures, which can only
        // widen the answer.
        Assert.True(
            MonsterPool.LargestSpan(Content.Monsters, 4m, MonsterCoverage.Diminished)
                >= MonsterPool.LargestSpan(Content.Monsters, 4m));
    }

    [Fact]
    public void AnEmptyPoolStillProducesAWalkableBattlefield() =>
        Assert.Equal(1, MonsterPool.LargestSpan([]));

    /// <summary>
    /// The fight-level question is a different one and is answered from the creatures
    /// actually drawn — which is what <c>EncounterFactory</c> hands terrain generation.
    /// </summary>
    [Fact]
    public void TheChosenCreaturesAnswerTheirOwnQuestion()
    {
        var party = PregeneratedParty.Build(Content, 3);
        var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(7));

        Assert.Equal(
            MonsterPool.LargestSpan(fight.Built.Monsters),
            fight.Built.Monsters
                .Select(monster => CreatureSizeRules.SpaceSpanSquares(monster.Sizes[0]))
                .DefaultIfEmpty(1)
                .Max());
    }

    /// <summary>
    /// Every body a built fight deploys is on the board and touches no other body — the
    /// legality guarantee, asked of real content across seeds, difficulties and every
    /// layout the draw can produce.
    /// </summary>
    /// <remarks>
    /// While every creature is one square this asserts the invariant the game has always
    /// had. It becomes the property test #429's criterion 6 asks for the moment the final
    /// slice makes spans real, which is why it is written against spaces rather than
    /// against positions.
    /// </remarks>
    [Theory]
    [InlineData(EncounterDifficulty.Low)]
    [InlineData(EncounterDifficulty.Moderate)]
    [InlineData(EncounterDifficulty.High)]
    public void EveryBuiltFightDeploysEveryBodyLegally(EncounterDifficulty difficulty)
    {
        for (var level = 1; level <= 5; level++)
        {
            for (var seed = 1; seed <= 25; seed++)
            {
                var party = PregeneratedParty.Build(Content, level);

                var fight = EncounterFactory.Build(
                    Content,
                    party,
                    difficulty,
                    new SeededRandomSource((level * 1000) + seed),
                    horde: seed % 3 == 0);

                var field = fight.Encounter.Battlefield;
                var claimed = new Dictionary<GridPosition, string>();

                foreach (var combatant in fight.Encounter.Combatants)
                {
                    foreach (var square in combatant.Space.Squares())
                    {
                        Assert.True(
                            field.IsPassable(square),
                            $"Seed {seed} level {level}: {combatant.Name} stands on {square}, which is not passable.");

                        Assert.False(
                            claimed.TryGetValue(square, out var other),
                            $"Seed {seed} level {level}: {combatant.Name} and {other} both claim {square}.");

                        claimed[square] = combatant.Name;
                    }
                }
            }
        }
    }
}
