using SRDCombat.Content;
using SRDCombat.Game;
using SRDCombat.PacingMeasure;
using SRDCombat.TestSupport;

namespace PacingMeasure.Tests;

/// <summary>
/// Pins #707: a party wipe is a complete encounter, so <c>PacingRun.RunSeed</c> used to
/// append its row to the same population the report then printed under "fights won" /
/// "fights cleared" — one lost fight per defeated run, silently averaged into hp-left,
/// downed and rounds figures under a label that denies containing it.
/// <see cref="FightRecord.Won"/> is the fix: it is read from
/// <c>Encounter.WinningSide</c> at the instant the fight completes, so the population a
/// caller filters to <c>Won</c> — exactly what both of <c>Program.cs</c>'s report blocks
/// do — excludes a lost fight the way "won" and "cleared" already claim it does.
/// </summary>
/// <remarks>
/// Seed 7 at level 1 on the default ladder is scripted rather than searched for at test
/// time: with the engine and content as committed, it clears fight 1 and is wiped in
/// fight 2 (measured directly with this tool while writing this test). A seed that
/// stops being true — the content or the engine's dice usage changed under it — is a
/// content-test failure on an entry nobody edited, exactly the shape
/// <c>ConditionRules.Executable</c>'s doc comment warns about; the fix is a fresh scripted
/// seed, not loosening what this test asserts.
/// </remarks>
public sealed class PacingRunTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void DefeatedRunExcludesItsLostFightFromTheWonPopulation()
    {
        var ladder = GauntletLadder.Default();

        var result = PacingRun.RunSeed(Content, ladder, startLevel: 1, seed: 7, noLoot: false);

        Assert.Equal(RunEnd.Defeated, result.End);
        Assert.Equal(1, result.Cleared);

        // Two encounters were played to completion: one won, one lost to the wipe.
        Assert.Equal(2, result.Fights.Count);
        Assert.True(result.Fights[0].Won, "The first fight was cleared and must be recorded as won.");
        Assert.False(result.Fights[1].Won, "The second fight ended the run in defeat and must not be recorded as won.");

        // The invariant the bug violated: the report's "won"/"cleared" population is
        // exactly the fights actually cleared, never the fights merely finished.
        Assert.Equal(result.Cleared, result.Fights.Count(f => f.Won));

        // Inclusion in `deaths`: the lost fight, and only the lost fight, is the death.
        var death = Assert.Single(result.Deaths);
        Assert.Equal(result.Fights[1].Fight, death.Fight);

        // Mirrors Program.cs's own gate (`fights.Where(f => f.Won)`, #707): the lost
        // fight must not appear in the population either report block iterates.
        var wonFights = result.Fights.Where(f => f.Won).ToArray();
        Assert.DoesNotContain(wonFights, f => f.Fight == result.Fights[1].Fight);
    }
}
