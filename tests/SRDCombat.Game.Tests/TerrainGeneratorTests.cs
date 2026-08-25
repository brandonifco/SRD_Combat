using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Generated terrain: seeded, spread across the whole board, and never able to make a
/// fight unwinnable on foot.
/// </summary>
/// <remarks>
/// The guarantees are asserted over many seeds rather than one, because the failure
/// mode of a generator is the draw nobody happened to look at — the lesson every
/// extraction validator in this project was built on.
/// </remarks>
public class TerrainGeneratorTests
{
    // The shape EncounterFactory actually builds for the classic two-column fight: two
    // columns of four, twelve squares of gap (60 feet), on a 9x6 field.
    private static readonly GridPosition[] PartySpawns =
        [new(1, 1), new(1, 2), new(1, 3), new(1, 4)];

    private static readonly GridPosition[] MonsterSpawns =
        [new(7, 1), new(7, 2), new(7, 3), new(7, 4)];

    private static Battlefield Generate(int seed) =>
        TerrainGenerator.Generate(9, 6, PartySpawns, MonsterSpawns, BattleLayout.Columns, new SeededRandomSource(seed));

    [Fact]
    public void TheSameSeedGrowsTheSameField()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var first = Generate(seed);
            var second = Generate(seed);

            Assert.Equal(first.Blocked.OrderBy(s => (s.X, s.Y)), second.Blocked.OrderBy(s => (s.X, s.Y)));
            Assert.Equal(
                first.DifficultTerrain.OrderBy(s => (s.X, s.Y)),
                second.DifficultTerrain.OrderBy(s => (s.X, s.Y)));
        }
    }

    [Fact]
    public void TerrainNeverStandsOnOrAdjacentToASpawnSquare()
    {
        // The free 3x3 block around every spawn (design §5) — a genuine tightening over
        // the old rule, which only excluded the spawn square itself.
        for (var seed = 1; seed <= 200; seed++)
        {
            foreach (var field in new[] { Generate(seed), GenerateWide(seed) })
            {
                var spawns = field.Width == 9
                    ? PartySpawns.Concat(MonsterSpawns)
                    : WidePartySpawns.Concat(WideMonsterSpawns);

                foreach (var spawn in spawns)
                {
                    Assert.True(field.IsPassable(spawn), $"Seed {seed} walled spawn {spawn}.");
                    Assert.DoesNotContain(spawn, field.DifficultTerrain);

                    foreach (var neighbour in spawn.Neighbours())
                    {
                        Assert.True(
                            field.IsPassable(neighbour),
                            $"Seed {seed} placed impassable terrain at {neighbour}, adjacent to spawn {spawn}.");
                        Assert.DoesNotContain(neighbour, field.DifficultTerrain);
                    }
                }
            }
        }
    }

    [Fact]
    public void TerrainCanNowLandInTheFlankingMargins()
    {
        // The whole-board eligibility change's own point: the old rule confined terrain
        // to the band strictly between the spawn columns (x in 2..6 on this fixture),
        // leaving the flanks permanently bare. Asserted as "it happens somewhere across
        // many seeds", not "it happens on seed 1" — the generator still rejects freely.
        var fields = Enumerable.Range(1, 400).Select(GenerateWide).ToArray();

        Assert.Contains(
            fields,
            field => field.Blocked.Concat(field.DifficultTerrain).Concat(field.LowObstacles)
                .Any(square => square.X < WidePartySpawns[0].X || square.X > WideMonsterSpawns[0].X));
    }

    [Fact]
    public void EverySpawnCanAlwaysReachEveryOther()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            foreach (var field in new[] { Generate(seed), GenerateWide(seed) })
            {
                var spawns = field.Width == 9
                    ? PartySpawns.Concat(MonsterSpawns).ToArray()
                    : WidePartySpawns.Concat(WideMonsterSpawns).ToArray();

                var reached = new HashSet<GridPosition> { spawns[0] };
                var frontier = new Queue<GridPosition>(reached);

                while (frontier.Count > 0)
                {
                    foreach (var next in frontier.Dequeue().Neighbours())
                    {
                        if (field.IsPassable(next) && reached.Add(next))
                        {
                            frontier.Enqueue(next);
                        }
                    }
                }

                foreach (var spawn in spawns)
                {
                    Assert.Contains(spawn, reached);
                }
            }
        }
    }

    [Fact]
    public void ObstaclesAreWholeFootprintsOfTheirArtsSize()
    {
        // Walls block 2x4 upright or 4x2 lying across the field, low obstacles 2x2 —
        // the drawn art's own coverage, which is
        // the whole point: a picture may never overhang a square a character can stand
        // on. Asserted on the real board shape as well as the small fixture.
        for (var seed = 1; seed <= 200; seed++)
        {
            foreach (var field in new[] { Generate(seed), GenerateWide(seed) })
            {
                foreach (var component in Components([.. field.LowObstacles]))
                {
                    AssertWholeRect(component, [(2, 2)], seed);
                }

                foreach (var component in Components([.. field.Blocked]))
                {
                    AssertWholeRect(component, [(2, 4), (4, 2)], seed);
                }
            }
        }
    }

    [Fact]
    public void FootprintsNeverTouchEachOther()
    {
        // Separation is what lets a client recover each footprint from the blocked
        // squares as a connected component and dress it with one drawing. Two
        // footprints that touched — same kind or different — would merge into one
        // component of the combined set, and that component could no longer be a
        // single footprint's whole rectangle.
        for (var seed = 1; seed <= 200; seed++)
        {
            var field = GenerateWide(seed);

            foreach (var component in Components([.. field.Blocked, .. field.LowObstacles]))
            {
                var width = component.Max(s => s.X) - component.Min(s => s.X) + 1;
                var height = component.Max(s => s.Y) - component.Min(s => s.Y) + 1;

                Assert.True(
                    ((width == 2 && (height == 2 || height == 4)) || (width == 4 && height == 2))
                        && component.Count == width * height,
                    $"Seed {seed}: footprints merged into a {width}x{height} component "
                    + $"of {component.Count} squares.");
            }
        }
    }

    // The standard EncounterFactory board: 28x18, spawn columns at the margin/separation
    // boundary — MarginSquares (8) and MarginSquares + separation (20).
    private static readonly GridPosition[] WidePartySpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(8, y))];

    private static readonly GridPosition[] WideMonsterSpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(20, y))];

    private static Battlefield GenerateWide(int seed) =>
        TerrainGenerator.Generate(
            28, 18, WidePartySpawns, WideMonsterSpawns, BattleLayout.Columns, new SeededRandomSource(seed));

    private static void AssertWholeRect(
        IReadOnlyCollection<GridPosition> component,
        IReadOnlyCollection<(int Width, int Height)> allowed,
        int seed)
    {
        var width = component.Max(s => s.X) - component.Min(s => s.X) + 1;
        var height = component.Max(s => s.Y) - component.Min(s => s.Y) + 1;

        Assert.True(
            allowed.Contains((width, height)) && component.Count == width * height,
            $"Seed {seed}: component of {component.Count} squares spans {width}x{height}, "
            + $"expected a whole {string.Join(" or ", allowed.Select(a => $"{a.Width}x{a.Height}"))}.");
    }

    private static IEnumerable<List<GridPosition>> Components(HashSet<GridPosition> squares)
    {
        while (squares.Count > 0)
        {
            var component = new List<GridPosition>();
            var frontier = new Queue<GridPosition>();
            var seed = squares.First();
            squares.Remove(seed);
            frontier.Enqueue(seed);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                component.Add(current);

                foreach (var next in current.Neighbours())
                {
                    if (squares.Remove(next))
                    {
                        frontier.Enqueue(next);
                    }
                }
            }

            yield return component;
        }
    }

    [Fact]
    public void BothKindsOfTerrainActuallyOccur()
    {
        // A generator whose guards reject everything would pass every test above while
        // producing only bare plains — the "clock nothing runs on" shape.
        var fields = Enumerable.Range(1, 50).Select(Generate).ToArray();

        Assert.Contains(fields, field => field.Blocked.Count > 0);
        Assert.Contains(fields, field => field.DifficultTerrain.Count > 0);
    }

    [Fact]
    public void TheWideBoardTypicallyCarriesSeveralObstacles()
    {
        // The density complaint from play (2026-08-20): a mean of one footprint per
        // field read as an empty room. The dial now lands two to three or more on most
        // fields (density tiers only raise this further). Asserted as a floor over many
        // seeds, not an exact count, so a better landing rate never fails the build —
        // the monster-pool convention.
        var fields = Enumerable.Range(1, 200).Select(GenerateWide).ToArray();
        var footprintCounts = fields
            .Select(field => Components([.. field.Blocked, .. field.LowObstacles]).Count())
            .ToArray();

        Assert.True(
            footprintCounts.Average() >= 2.0,
            $"Mean footprints per field fell to {footprintCounts.Average():F2}.");
        Assert.True(
            footprintCounts.Count(count => count == 0) <= 10,
            $"{footprintCounts.Count(count => count == 0)} of 200 wide fields were bare.");
    }

    [Fact]
    public void BareFieldsStayPossible()
    {
        var fields = Enumerable.Range(1, 200).Select(Generate).ToArray();

        Assert.Contains(fields, field => field.Blocked.Count == 0 && field.DifficultTerrain.Count == 0);
    }

    [Fact]
    public void ADegenerateBoardNeverThrowsAndNeverStandsAWallOnAnyoneOrDisconnects()
    {
        // Sides in adjacent columns leave no open band between them — the contested
        // region and the eligibility rules must both fall back gracefully rather than
        // throw, and the guarantees that matter (nobody walled in, everyone connected)
        // must still hold on a board this small.
        var partySpawns = new[] { new GridPosition(1, 1) };
        var monsterSpawns = new[] { new GridPosition(2, 1) };

        for (var seed = 1; seed <= 50; seed++)
        {
            var field = TerrainGenerator.Generate(
                4, 4, partySpawns, monsterSpawns, BattleLayout.Columns, new SeededRandomSource(seed));

            // The 2x2 minimum footprint cannot fit in the sliver of board this leaves
            // eligible, so no obstacle ever lands — but that is an observed consequence
            // of the geometry, not a special case in the generator.
            Assert.Empty(field.Blocked);
            Assert.Empty(field.LowObstacles);

            foreach (var spawn in partySpawns.Concat(monsterSpawns))
            {
                Assert.True(field.IsPassable(spawn));
            }
        }
    }

    [Fact]
    public void DressingLeansTowardTheContestedGround()
    {
        // Design §5's "mild bias": two-thirds of dressing anchors draw from the
        // contested region. Not a per-board guarantee — asserted as a density
        // comparison over many seeds on the standard board, where the contested region
        // (the middle third of the gap, under Columns) is a known, narrow slice of the
        // whole field.
        var fields = Enumerable.Range(1, 400).Select(GenerateWide).ToArray();

        var bandMinX = WidePartySpawns[0].X + 1;
        var bandMaxX = WideMonsterSpawns[0].X - 1;
        var bandWidth = bandMaxX - bandMinX + 1;
        var thirdWidth = Math.Max(1, bandWidth / 3);
        var midStart = bandMinX + ((bandWidth - thirdWidth) / 2);
        var midEnd = Math.Min(bandMaxX, midStart + thirdWidth - 1);

        bool InContestedRegion(GridPosition square) => square.X >= midStart && square.X <= midEnd;

        var contestedSquares = (double)(midEnd - midStart + 1) * 18;
        var wholeBoardSquares = 28.0 * 18;

        var contestedDensity = fields.Average(field =>
            field.Blocked.Concat(field.DifficultTerrain).Concat(field.LowObstacles).Count(InContestedRegion))
            / contestedSquares;
        var overallDensity = fields.Average(field =>
            field.Blocked.Count + field.DifficultTerrain.Count + field.LowObstacles.Count)
            / wholeBoardSquares;

        Assert.True(
            contestedDensity > overallDensity,
            $"Contested-region density {contestedDensity:P2} did not exceed overall density {overallDensity:P2}.");
    }

    [Fact]
    public void TheFactoryDeliversTerrainIntoARealFight()
    {
        var content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);
        var party = PregeneratedParty.Build(content, level: 2);

        // One seed is enough to prove the wiring; which seeds carry terrain is the
        // generator tests' business.
        var terrained = Enumerable.Range(1, 30)
            .Select(seed => EncounterFactory.Build(
                content, party, EncounterDifficulty.Moderate, new SeededRandomSource(seed)))
            .FirstOrDefault(fight =>
                fight.Encounter.Battlefield.Blocked.Count > 0
                || fight.Encounter.Battlefield.DifficultTerrain.Count > 0);

        Assert.NotNull(terrained);

        // And nobody was placed into it.
        foreach (var combatant in terrained.Encounter.Combatants)
        {
            Assert.True(terrained.Encounter.Battlefield.IsPassable(combatant.Position));
        }
    }
}
