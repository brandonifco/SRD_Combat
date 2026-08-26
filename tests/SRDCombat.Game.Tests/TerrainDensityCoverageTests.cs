using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The density tiers' realized coverage, measured over a seed sweep against the bands
/// design §6 states — the "committed check" the design doc's own diagnosis promised
/// would replace its ad hoc survey instrument.
/// </summary>
/// <remarks>
/// <para>
/// The bands are a distribution claim over a sweep, not a per-board guarantee: a single
/// cluttered board can tail below its band under rejection. So every assertion here
/// buckets many boards by their <em>drawn</em> tier and checks the bucket's mean, never
/// a single board's count — both a floor and a ceiling per tier, since a floor-only
/// check is exactly the shape that let 39 of 339 spells go missing for months while a
/// <c>&gt;= 300</c> test stayed green.
/// </para>
/// <para>
/// <b>Measured on the Columns layout</b> — the design's own §1 diagnosis board and the
/// commonest draw (every fight below level 3, half of fights above it), so it is the
/// canonical shape a player actually sees most. CornerGroups measures close behind it
/// (checked below as a looser sanity check, not a second strict band): its contested
/// region is a wider unnarrowed band, so it lands a little richer at the same dial.
/// Surrounded runs structurally lower at every tier — its twelve spawns are scattered
/// clear across the board rather than clustered on two columns, so their 3x3
/// clearances (§5) overlap far less and remove much more total ground before a single
/// obstacle is drawn. One global dial cannot equalize that without breaking the
/// invariant that Sparse is today's unchanged dial (~3.6%, design §6's own "today, for
/// reference" row) — so Surrounded is checked only for the shape that matters
/// (density still climbs with the tier, nothing regresses to zero), not the same
/// absolute band. This is a stated reading, not a printed rule.
/// </para>
/// </remarks>
public class TerrainDensityCoverageTests
{
    // The standard EncounterFactory board for the commonest fight: 28 wide, 18 tall —
    // 504 squares total, matching the design doc's own reference figure — two columns
    // of four, at the margin/separation boundary (MarginSquares = 8, separation = 12
    // squares), reproducing EncounterFactory.PlaceSides's own arithmetic.
    private const int Width = 28;
    private const int Height = 18;
    private const int TotalSquares = Width * Height;

    private static readonly GridPosition[] PartySpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(8, y))];

    private static readonly GridPosition[] MonsterSpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(20, y))];

    // Enough seeds that each of the three tiers (25/50/25 draw weights) collects at
    // least a couple hundred boards of its own, which is what "each tier's realized
    // mean" needs to be a stable claim rather than a handful of samples.
    private const int SweepSize = 1000;

    private static double CoverageOf(Battlefield field) =>
        (double)(field.Blocked.Count + field.LowObstacles.Count + field.DifficultTerrain.Count) / TotalSquares;

    private static IEnumerable<(TerrainDensity Tier, double Coverage)> Sweep(
        BattleLayout layout, IReadOnlyList<GridPosition> party, IReadOnlyList<GridPosition> monsters)
    {
        for (var seed = 1; seed <= SweepSize; seed++)
        {
            // Same seed, two fresh sources: one to peek the tier the way Generate will
            // draw it (its own first roll, before any other die is spent), one to
            // actually generate the board. Fresh instances on the same seed reproduce
            // the same sequence, so this is not a second, different draw — it is
            // reading the first one twice.
            var tier = TerrainGenerator.DrawDensity(new SeededRandomSource(seed));
            var field = TerrainGenerator.Generate(Width, Height, party, monsters, layout, new SeededRandomSource(seed));

            yield return (tier, CoverageOf(field));
        }
    }

    private static double MeanCoverage(IEnumerable<(TerrainDensity Tier, double Coverage)> samples, TerrainDensity tier) =>
        samples.Where(sample => sample.Tier == tier).Select(sample => sample.Coverage).Average();

    [Fact]
    public void SparseLandsInItsBand() => AssertBand(TerrainDensity.Sparse, 0.03, 0.06);

    [Fact]
    public void StandardLandsInItsBand() => AssertBand(TerrainDensity.Standard, 0.07, 0.11);

    [Fact]
    public void ClutteredLandsInItsBand() => AssertBand(TerrainDensity.Cluttered, 0.12, 0.16);

    private static void AssertBand(TerrainDensity tier, double floor, double ceiling)
    {
        var samples = Sweep(BattleLayout.Columns, PartySpawns, MonsterSpawns).ToArray();
        var bucket = samples.Where(sample => sample.Tier == tier).ToArray();

        Assert.True(bucket.Length >= 100, $"Only {bucket.Length} {tier} boards drawn in a sweep of {SweepSize}.");

        var mean = bucket.Average(sample => sample.Coverage);

        Assert.True(mean >= floor, $"{tier} mean coverage {mean:P2} fell below the floor {floor:P0}.");
        Assert.True(mean <= ceiling, $"{tier} mean coverage {mean:P2} rose above the ceiling {ceiling:P0}.");
    }

    [Fact]
    public void EachTierDrawsAtItsStatedWeight()
    {
        // 25 / 50 / 25 (design §6) — checked loosely, since this is a die roll, not a
        // planted sequence.
        var tiers = Enumerable.Range(1, SweepSize)
            .Select(seed => TerrainGenerator.DrawDensity(new SeededRandomSource(seed)))
            .ToArray();

        var sparse = tiers.Count(tier => tier == TerrainDensity.Sparse) / (double)SweepSize;
        var standard = tiers.Count(tier => tier == TerrainDensity.Standard) / (double)SweepSize;
        var cluttered = tiers.Count(tier => tier == TerrainDensity.Cluttered) / (double)SweepSize;

        Assert.InRange(sparse, 0.20, 0.30);
        Assert.InRange(standard, 0.45, 0.55);
        Assert.InRange(cluttered, 0.20, 0.30);
    }

    [Theory]
    [InlineData(BattleLayout.Columns)]
    [InlineData(BattleLayout.CornerGroups)]
    [InlineData(BattleLayout.Surrounded)]
    public void DensityClimbsWithTheTierOnEveryLayout(BattleLayout layout)
    {
        // The one guarantee every layout must keep, whatever its own absolute numbers:
        // the dial does something, and it does it in the right direction. Geometry
        // (see class remarks) is left free to set each layout's own baseline.
        var (party, monsters) = layout switch
        {
            BattleLayout.CornerGroups => (PartySpawns, (GridPosition[])[new(20, 1), new(20, 16), new(20, 2), new(20, 15)]),
            BattleLayout.Surrounded => (
                (GridPosition[])[new(14, 9), new(15, 9), new(14, 10), new(15, 10)],
                (GridPosition[])[new(20, 9), new(8, 9), new(14, 3), new(14, 15), new(20, 10), new(8, 10), new(15, 3), new(15, 15)]),
            _ => (PartySpawns, MonsterSpawns),
        };

        var samples = Sweep(layout, party, monsters).ToArray();

        var sparseMean = MeanCoverage(samples, TerrainDensity.Sparse);
        var standardMean = MeanCoverage(samples, TerrainDensity.Standard);
        var clutteredMean = MeanCoverage(samples, TerrainDensity.Cluttered);

        Assert.True(sparseMean > 0, $"{layout} sparse produced no terrain at all across {SweepSize} seeds.");
        Assert.True(
            standardMean > sparseMean,
            $"{layout} standard ({standardMean:P2}) did not exceed sparse ({sparseMean:P2}).");
        Assert.True(
            clutteredMean > standardMean,
            $"{layout} cluttered ({clutteredMean:P2}) did not exceed standard ({standardMean:P2}).");
    }
}
