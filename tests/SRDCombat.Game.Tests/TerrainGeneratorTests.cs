using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Generated terrain: seeded, between the sides, and never able to make a fight
/// unwinnable on foot.
/// </summary>
/// <remarks>
/// The guarantees are asserted over many seeds rather than one, because the failure
/// mode of a generator is the draw nobody happened to look at — the lesson every
/// extraction validator in this project was built on.
/// </remarks>
public class TerrainGeneratorTests
{
    // The shape EncounterFactory actually builds: two columns of four, six squares of
    // gap, on a 9x6 field.
    private static readonly GridPosition[] Spawns =
    [
        new(1, 1), new(1, 2), new(1, 3), new(1, 4),
        new(7, 1), new(7, 2), new(7, 3), new(7, 4),
    ];

    private static Battlefield Generate(int seed) =>
        TerrainGenerator.Generate(9, 6, Spawns, new SeededRandomSource(seed));

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
    public void TerrainNeverTouchesASpawnSquare()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var field = Generate(seed);

            foreach (var spawn in Spawns)
            {
                Assert.True(field.IsPassable(spawn), $"Seed {seed} walled spawn {spawn}.");
                Assert.DoesNotContain(spawn, field.DifficultTerrain);
            }
        }
    }

    [Fact]
    public void TerrainStaysStrictlyBetweenTheSides()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var field = Generate(seed);

            foreach (var square in field.Blocked.Concat(field.DifficultTerrain))
            {
                Assert.InRange(square.X, 2, 6);
                Assert.InRange(square.Y, 0, 5);
            }
        }
    }

    [Fact]
    public void EverySpawnCanAlwaysReachEveryOther()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var field = Generate(seed);
            var reached = new HashSet<GridPosition> { Spawns[0] };
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

            foreach (var spawn in Spawns)
            {
                Assert.Contains(spawn, reached);
            }
        }
    }

    [Fact]
    public void ObstaclesAreWholeFootprintsOfTheirArtsSize()
    {
        // Walls block 2x4, low obstacles 2x2 — the drawn art's own coverage, which is
        // the whole point: a picture may never overhang a square a character can stand
        // on. Asserted on the real board shape as well as the small fixture.
        for (var seed = 1; seed <= 200; seed++)
        {
            foreach (var field in new[] { Generate(seed), GenerateWide(seed) })
            {
                foreach (var component in Components([.. field.LowObstacles]))
                {
                    AssertWholeRect(component, expectedWidth: 2, expectedHeight: 2, seed);
                }

                foreach (var component in Components([.. field.Blocked]))
                {
                    AssertWholeRect(component, expectedWidth: 2, expectedHeight: 4, seed);
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
                    width == 2 && (height == 2 || height == 4) && component.Count == width * height,
                    $"Seed {seed}: footprints merged into a {width}x{height} component "
                    + $"of {component.Count} squares.");
            }
        }
    }

    // The current EncounterFactory board: 18x12, spawn columns near either edge.
    private static Battlefield GenerateWide(int seed) =>
        TerrainGenerator.Generate(
            18,
            12,
            [.. Enumerable.Range(4, 4).Select(y => new GridPosition(3, y)),
             .. Enumerable.Range(4, 4).Select(y => new GridPosition(14, y))],
            new SeededRandomSource(seed));

    private static void AssertWholeRect(
        IReadOnlyCollection<GridPosition> component, int expectedWidth, int expectedHeight, int seed)
    {
        var width = component.Max(s => s.X) - component.Min(s => s.X) + 1;
        var height = component.Max(s => s.Y) - component.Min(s => s.Y) + 1;

        Assert.True(
            width == expectedWidth && height == expectedHeight
                && component.Count == expectedWidth * expectedHeight,
            $"Seed {seed}: component of {component.Count} squares spans {width}x{height}, "
            + $"expected a whole {expectedWidth}x{expectedHeight}.");
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
    public void BareFieldsStayPossible()
    {
        var fields = Enumerable.Range(1, 200).Select(Generate).ToArray();

        Assert.Contains(fields, field => field.Blocked.Count == 0 && field.DifficultTerrain.Count == 0);
    }

    [Fact]
    public void ADegenerateRegionMeansABareFieldRatherThanAThrow()
    {
        // Sides in adjacent columns leave no band between them to put terrain in.
        var field = TerrainGenerator.Generate(
            4,
            4,
            [new GridPosition(1, 1), new GridPosition(2, 1)],
            new SeededRandomSource(1));

        Assert.Empty(field.Blocked);
        Assert.Empty(field.DifficultTerrain);
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
