namespace SRDCombat.PacingMeasure;

/// <summary>
/// The report's own population gate, extracted so a test can drive the exact function
/// <c>Program.cs</c> calls rather than a copy of its logic (#707 review round).
/// </summary>
/// <remarks>
/// The first cut of #707 fixed <see cref="FightRecord.Won"/> and then had
/// <c>Program.cs</c> filter to it inline (<c>fights.Where(f =&gt; f.Won)</c>), with the
/// test asserting only that <c>Won</c> came out correctly from <c>PacingRun.RunSeed</c>.
/// That knockout-verifies the wrong thing: reverting <c>Program.cs</c>'s own filter back
/// to "every completed fight" (the original bug) leaves every assertion in the test
/// green, because the test never calls into <c>Program.cs</c> at all — it recomputes the
/// same filter on its own copy of the data. Routing both of <c>Program.cs</c>'s report
/// blocks through <see cref="WonRows"/>, and having the test call this method too, means
/// there is exactly one place the gate can live, and knocking it out here is knocking out
/// what the report actually runs.
/// </remarks>
public static class PacingReport
{
    /// <summary>
    /// The population either report block (by-monster-count, per-band) iterates: the
    /// fights the party actually won, excluding the one that ends a defeated run in a
    /// party wipe even though that encounter also ran to completion.
    /// </summary>
    public static IReadOnlyList<FightRecord> WonRows(IReadOnlyList<FightRecord> fights)
    {
        ArgumentNullException.ThrowIfNull(fights);

        return fights.Where(f => f.Won).ToArray();
    }
}
