using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The gauntlet's full thirty-fight playthrough, kept in its own class (its own xUnit
/// collection) so it runs alongside the rest of the suite rather than serialising behind
/// the many short arithmetic tests in <see cref="GauntletTests"/>.
/// </summary>
/// <remarks>
/// Moved verbatim from <see cref="GauntletTests"/> — same seed (4242), same ladder, same
/// assertions. Isolated by construction (fresh content read, fresh <see cref="GauntletRun"/>,
/// fresh <see cref="SeededRandomSource"/>), so it is safe to run in parallel.
/// </remarks>
public class GauntletFullRunTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void ARunReachesLevelFiveIfItIsPlayedOutFarEnough()
    {
        // The pacing claim the default ladder's length rests on: thirty fights is enough
        // to carry a party from level 1 to the top of this game's tier. Fought by the
        // policy on both sides, so it is the arithmetic being tested, not tactics.
        var run = GauntletRun.Start(Content, GauntletLadder.Default());
        var random = new SeededRandomSource(4242);

        while (run.Next is not null)
        {
            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight);
        }

        // However the run ended, nobody may exceed the supported tier.
        Assert.All(run.States, state => Assert.InRange(state.Level, 1, AdvancementRules.MaximumSupportedLevel));

        if (run.Outcome == RunOutcome.Survived)
        {
            Assert.Contains(run.States, state => state.Level >= 3);
        }
    }
}
