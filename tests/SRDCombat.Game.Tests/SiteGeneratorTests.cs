using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// S3's two sites — crossing and central wall — placed before dressing runs: the draw
/// weights, the layout-aware placement band, the gap/ford width guarantee, the
/// spawn-clearance rule, span-aware connectivity, and the fixed-dice-regardless-of-
/// legality discipline every generator in this project states.
/// </summary>
public class SiteGeneratorTests
{
    // The standard EncounterFactory board: 28x18, spawn columns at the margin/separation
    // boundary, matching TerrainGeneratorTests' own fixture so the contested band (design
    // §4.6) is the familiar x in [13,15] for Columns.
    private static readonly GridPosition[] PartySpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(8, y))];

    private static readonly GridPosition[] MonsterSpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(20, y))];

    private const int Width = 28;
    private const int Height = 18;

    [Fact]
    public void DrawSiteWeightsMatchTheCatalogue()
    {
        // Issue #436's own S3 catalogue: open field 60%, crossing 20%, central wall 20%
        // — checked loosely, since this is a die roll, not a planted sequence.
        const int sweep = 2000;

        var drawn = Enumerable.Range(1, sweep)
            .Select(seed => SiteGenerator.DrawSite(new SeededRandomSource(seed), BattleLayout.Columns))
            .ToArray();

        var openField = drawn.Count(site => site == SiteType.OpenField) / (double)sweep;
        var crossing = drawn.Count(site => site == SiteType.Crossing) / (double)sweep;
        var centralWall = drawn.Count(site => site == SiteType.CentralWall) / (double)sweep;

        Assert.InRange(openField, 0.53, 0.67);
        Assert.InRange(crossing, 0.14, 0.26);
        Assert.InRange(centralWall, 0.14, 0.26);
    }

    [Fact]
    public void SurroundedAlwaysDrawsOpenFieldAtTheSameDiceCost()
    {
        // The implementer's-choice reading (SiteGenerator's own remarks): Surrounded
        // re-rolls to open field rather than drawing arcs. "Re-roll" is an override, not
        // an extra die — a single scripted d5 must satisfy the whole draw whatever the
        // layout, which is what proves no second roll is spent.
        for (var roll = 1; roll <= 5; roll++)
        {
            var site = SiteGenerator.DrawSite(new ScriptedRandomSource(roll), BattleLayout.Surrounded);

            Assert.Equal(SiteType.OpenField, site);
        }
    }

    [Fact]
    public void ColumnsAndCornerGroupsDrawEveryCatalogueEntryOverASweep()
    {
        foreach (var layout in new[] { BattleLayout.Columns, BattleLayout.CornerGroups })
        {
            var drawn = Enumerable.Range(1, 200)
                .Select(seed => SiteGenerator.DrawSite(new SeededRandomSource(seed), layout))
                .ToHashSet();

            Assert.Contains(SiteType.OpenField, drawn);
            Assert.Contains(SiteType.Crossing, drawn);
            Assert.Contains(SiteType.CentralWall, drawn);
        }
    }

    [Theory]
    [InlineData(BattleLayout.Columns)]
    [InlineData(BattleLayout.CornerGroups)]
    public void CentralWallGapsAreAtLeastTheThreadedSpanWide(BattleLayout layout)
    {
        foreach (var span in new[] { 1, 2, 3 })
        {
            var found = false;

            for (var seed = 1; seed <= 500 && !found; seed++)
            {
                var plan = SiteGenerator.Place(
                    SiteType.CentralWall, Width, Height, layout, PartySpawns, MonsterSpawns,
                    new SeededRandomSource(seed), span);

                var gapPieces = plan.Pieces.Where(p => p.Kind == TerrainPieceKind.Gap).ToArray();

                if (gapPieces.Length == 0)
                {
                    continue;
                }

                found = true;
                var expectedWidth = Math.Max(2, span);

                Assert.All(gapPieces, gap => Assert.True(
                    gap.Squares.Count >= expectedWidth,
                    $"Seed {seed} span {span}: gap of {gap.Squares.Count} squares, expected >= {expectedWidth}."));
            }

            Assert.True(found, $"No accepted CentralWall found for {layout} span {span} in 500 seeds.");
        }
    }

    [Theory]
    [InlineData(BattleLayout.Columns)]
    [InlineData(BattleLayout.CornerGroups)]
    public void CrossingFordsAreAtLeastTheThreadedSpanWide(BattleLayout layout)
    {
        foreach (var span in new[] { 1, 2, 3 })
        {
            var found = false;

            for (var seed = 1; seed <= 500 && !found; seed++)
            {
                var plan = SiteGenerator.Place(
                    SiteType.Crossing, Width, Height, layout, PartySpawns, MonsterSpawns,
                    new SeededRandomSource(seed), span);

                var fordPieces = plan.Pieces.Where(p => p.Kind == TerrainPieceKind.Gap).ToArray();

                if (fordPieces.Length == 0)
                {
                    continue;
                }

                found = true;
                var expectedWidth = Math.Max(2, span);

                Assert.All(fordPieces, ford => Assert.True(
                    // A ford's piece spans the band's whole depth (2-4) by the ford's
                    // row width — the row count per column is what "ford width" means.
                    ford.Squares.GroupBy(square => square.X).All(column => column.Count() >= expectedWidth),
                    $"Seed {seed} span {span}: a ford column had fewer than {expectedWidth} rows."));
            }

            Assert.True(found, $"No accepted Crossing found for {layout} span {span} in 500 seeds.");
        }
    }

    [Theory]
    [InlineData(SiteType.CentralWall)]
    [InlineData(SiteType.Crossing)]
    public void NoSiteSquareEverTouchesASpawnsClearance(SiteType site)
    {
        var cleared = TerrainGenerator.ClearedSquares([.. PartySpawns, .. MonsterSpawns]);

        foreach (var layout in new[] { BattleLayout.Columns, BattleLayout.CornerGroups })
        {
            for (var seed = 1; seed <= 300; seed++)
            {
                var plan = SiteGenerator.Place(
                    site, Width, Height, layout, PartySpawns, MonsterSpawns, new SeededRandomSource(seed));

                var placedSquares = plan.Walls.Concat(plan.LowObstacles)
                    .Concat(plan.DifficultTerrain).Concat(plan.ProtectedSquares);

                Assert.DoesNotContain(placedSquares, cleared.Contains);
            }
        }
    }

    [Fact]
    public void ACentralWallNeverStandsWithoutStayingConnectedAtItsThreadedSpan()
    {
        foreach (var span in new[] { 1, 2, 3 })
        {
            for (var seed = 1; seed <= 300; seed++)
            {
                var plan = SiteGenerator.Place(
                    SiteType.CentralWall, Width, Height, BattleLayout.Columns, PartySpawns, MonsterSpawns,
                    new SeededRandomSource(seed), span);

                if (plan.Walls.Count == 0 && plan.LowObstacles.Count == 0)
                {
                    continue;
                }

                var reserved = new HashSet<GridPosition>([.. PartySpawns, .. MonsterSpawns]);
                var candidates = plan.Walls.Concat(plan.LowObstacles).ToArray();

                Assert.True(
                    GridConnectivity.StaysConnected([], candidates, reserved, Width, Height, span),
                    $"Seed {seed} span {span}: a placed central wall did not stay connected.");
            }
        }
    }

    [Fact]
    public void SurroundedNeverPlacesAStructureEvenIfAskedDirectly()
    {
        // The belt-and-braces guard inside Place itself (see the class remarks): even a
        // caller that bypasses DrawSite's own override gets nothing back for Surrounded.
        var surroundedParty = new[] { new GridPosition(14, 9), new GridPosition(15, 9) };
        var surroundedMonsters = new[] { new GridPosition(20, 9), new GridPosition(8, 9) };

        foreach (var site in new[] { SiteType.CentralWall, SiteType.Crossing })
        {
            for (var seed = 1; seed <= 50; seed++)
            {
                var plan = SiteGenerator.Place(
                    site, Width, Height, BattleLayout.Surrounded, surroundedParty, surroundedMonsters,
                    new SeededRandomSource(seed));

                Assert.Empty(plan.Pieces);
                Assert.Empty(plan.Walls);
                Assert.Empty(plan.LowObstacles);
                Assert.Empty(plan.DifficultTerrain);
                Assert.Empty(plan.ProtectedSquares);
            }
        }
    }

    [Theory]
    [InlineData(SiteType.CentralWall)]
    [InlineData(SiteType.Crossing)]
    public void TheSameSeedPlacesTheSameStructure(SiteType site)
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var first = SiteGenerator.Place(
                site, Width, Height, BattleLayout.Columns, PartySpawns, MonsterSpawns, new SeededRandomSource(seed));
            var second = SiteGenerator.Place(
                site, Width, Height, BattleLayout.Columns, PartySpawns, MonsterSpawns, new SeededRandomSource(seed));

            Assert.Equal(first.Walls.OrderBy(s => (s.X, s.Y)), second.Walls.OrderBy(s => (s.X, s.Y)));
            Assert.Equal(
                first.DifficultTerrain.OrderBy(s => (s.X, s.Y)), second.DifficultTerrain.OrderBy(s => (s.X, s.Y)));
            Assert.Equal(
                first.ProtectedSquares.OrderBy(s => (s.X, s.Y)), second.ProtectedSquares.OrderBy(s => (s.X, s.Y)));
        }
    }

}
