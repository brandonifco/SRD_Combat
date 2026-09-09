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
/// <see cref="FightRecord.Won"/> is read from <c>Encounter.WinningSide</c> at the
/// instant the fight completes; <see cref="PacingReport.WonRows"/> is the gate that
/// turns that field into the population <c>Program.cs</c>'s two report blocks iterate.
/// </summary>
/// <remarks>
/// <para>
/// This test calls <see cref="PacingReport.WonRows"/> — the exact function
/// <c>Program.cs:120</c> calls — rather than recomputing its own <c>Where(f =&gt;
/// f.Won)</c> filter. An earlier version of this test did the latter, which
/// knockout-verified only that <see cref="FightRecord.Won"/> comes out correct; it
/// stayed green if <c>Program.cs</c>'s own gate were reverted to "every completed
/// fight" (the original #707 bug), because nothing here called into <c>Program.cs</c>'s
/// code path at all. Routing the assertion through <see cref="PacingReport.WonRows"/>
/// closes that gap: the callable is exactly what the report runs, so reverting its gate
/// is reverting what this test exercises.
/// </para>
/// <para>
/// Seed 7 at level 1 on the default ladder is scripted rather than searched for at test
/// time: with the engine and content as committed, it clears fight 1 and is wiped in
/// fight 2 (measured directly with this tool while writing this test). A seed that
/// stops being true — the content or the engine's dice usage changed under it — is a
/// content-test failure on an entry nobody edited, exactly the shape
/// <c>ConditionRules.Executable</c>'s doc comment warns about; the fix is a fresh scripted
/// seed, not loosening what this test asserts.
/// </para>
/// <para>
/// <c>Program.cs</c>'s own <c>Console.WriteLine</c> formatting is not exercised here or
/// anywhere else in this project — it stays a human-read instrument, as CLAUDE.md's
/// repository map already documents for this tool.
/// </para>
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

        var lostFight = result.Fights[1];

        // Inclusion in `deaths`: the lost fight, and only the lost fight, is the death.
        var death = Assert.Single(result.Deaths);
        Assert.Equal(lostFight.Fight, death.Fight);

        // The production gate itself (Program.cs calls this same method, #707 review
        // round): the lost fight must be absent from the rows either report block
        // iterates, and every row present must be a fight the party actually won.
        var wonRows = PacingReport.WonRows(result.Fights);

        Assert.DoesNotContain(wonRows, f => f.Fight == lostFight.Fight);
        Assert.All(wonRows, f => Assert.True(f.Won));

        // The invariant the bug violated: the report's "won"/"cleared" population is
        // exactly the fights actually cleared, never the fights merely finished.
        Assert.Equal(result.Cleared, wonRows.Count);
    }
}
