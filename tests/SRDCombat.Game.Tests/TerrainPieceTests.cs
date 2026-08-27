using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The battlefield-overhaul S2 model: <see cref="TerrainPiece"/> describes generated
/// structures without changing what a square means, and boards generated after this
/// slice are the same boards the same seed generated before it.
/// </summary>
public class TerrainPieceTests
{
    private static readonly GridPosition[] PartySpawns =
        [new(1, 1), new(1, 2), new(1, 3), new(1, 4)];

    private static readonly GridPosition[] MonsterSpawns =
        [new(7, 1), new(7, 2), new(7, 3), new(7, 4)];

    private static readonly GridPosition[] WidePartySpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(8, y))];

    private static readonly GridPosition[] WideMonsterSpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(20, y))];

    private static Battlefield GenerateSmall(int seed) =>
        TerrainGenerator.Generate(9, 6, PartySpawns, MonsterSpawns, BattleLayout.Columns, new SeededRandomSource(seed));

    private static Battlefield GenerateWide(int seed) =>
        TerrainGenerator.Generate(
            28, 18, WidePartySpawns, WideMonsterSpawns, BattleLayout.Columns, new SeededRandomSource(seed));

    /// <summary>
    /// Captured from <c>TerrainGenerator.Generate</c> on the pre-S2 build (branch point
    /// `07f33e5`), before <see cref="TerrainPiece"/> or the <c>pieces</c> constructor
    /// parameter existed. #435's acceptance criterion is that a board a seed generated
    /// before this slice is the same board it generates after — S2 adds description
    /// alongside the three square sets, and touches none of the dice or placement logic
    /// that decides them. One line per fixture per seed: <c>B</c>/<c>D</c>/<c>L</c> are
    /// <see cref="Battlefield.Blocked"/>/<see cref="Battlefield.DifficultTerrain"/>/
    /// <see cref="Battlefield.LowObstacles"/>, each square sorted by (X, Y).
    /// </summary>
    private static readonly string[] PreSliceFingerprints =
    [
        "small|1|B=4,2;4,3;4,4;4,5;5,2;5,3;5,4;5,5|D=|L=",
        "wide|1|B=14,6;14,7;14,8;14,9;15,6;15,7;15,8;15,9|D=15,10;15,11;15,12;16,10|L=14,0;14,1;15,0;15,1",
        "small|2|B=4,2;4,3;4,4;4,5;5,2;5,3;5,4;5,5|D=3,1;3,4;4,1;5,1|L=",
        "wide|2|B=13,6;13,7;13,8;13,9;13,14;13,15;13,16;13,17;14,6;14,7;14,8;14,9;14,14;14,15;14,16;14,17;15,0;15,1;16,0;16,1;17,0;17,1;18,0;18,1;22,11;22,12;22,13;22,14;23,11;23,12;23,13;23,14|D=0,14;12,3;13,3;13,10;14,3;14,10;15,9;20,15;20,16;21,15;21,16;24,3|L=",
        "small|3|B=|D=3,0;4,0|L=4,1;4,2;4,4;4,5;5,1;5,2;5,4;5,5",
        "wide|3|B=2,12;2,13;2,14;2,15;3,12;3,13;3,14;3,15;14,9;14,10;14,11;14,12;15,9;15,10;15,11;15,12|D=2,16;2,17;12,2;13,1;13,2;13,4;14,14;19,13;20,4;21,4;24,8;24,9;24,10|L=0,1;0,2;1,1;1,2;14,3;14,4;15,3;15,4",
        "small|4|B=|D=3,0;3,1;3,3;3,4;4,2;4,3;5,2;5,3|L=4,0;4,1;4,4;4,5;5,0;5,1;5,4;5,5",
        "wide|4|B=3,2;3,3;3,4;3,5;4,2;4,3;4,4;4,5;26,6;26,7;26,8;26,9;27,6;27,7;27,8;27,9|D=5,4;5,5;11,2;12,1;12,2;12,3;12,4;12,11;12,12;13,2;13,3;13,4;13,11;13,12;13,13;13,14;14,2;14,7;14,11;14,12;15,7;15,11;15,15;16,10;16,15;22,9;23,12;23,13;24,12|L=5,7;5,8;6,7;6,8;9,2;9,3;10,2;10,3;13,0;13,1;13,16;13,17;14,0;14,1;14,5;14,6;14,9;14,10;14,13;14,14;14,16;14,17;15,5;15,6;15,9;15,10;15,13;15,14;20,16;20,17;21,16;21,17",
        "small|5|B=4,0;4,1;4,2;4,3;5,0;5,1;5,2;5,3|D=3,0;3,3;3,4;4,4;4,5|L=",
        "wide|5|B=8,2;8,3;9,2;9,3;10,2;10,3;11,2;11,3;14,12;14,13;15,2;15,3;15,4;15,5;15,12;15,13;16,2;16,3;16,4;16,5;16,12;16,13;17,12;17,13;21,3;21,4;22,3;22,4;23,3;23,4;24,3;24,4|D=13,8;13,17;14,11;14,14;14,15;15,6;15,10;15,11|L=15,16;15,17;16,16;16,17",
        "small|6|B=|D=3,0;3,1;3,2;3,3;4,0;4,1;4,4;4,5;5,0;5,1;5,5|L=4,2;4,3;5,2;5,3",
        "wide|6|B=10,11;10,12;10,13;10,14;11,11;11,12;11,13;11,14;13,12;13,13;13,14;13,15;14,12;14,13;14,14;14,15|D=3,13;4,7;4,13;4,14;5,7;5,8;5,14;10,5;10,6;12,7;13,2;13,3;13,4;13,7;13,10;13,11;14,2;14,10;14,16;15,0;15,1;15,10;15,16;15,17|L=0,1;0,2;1,1;1,2;13,0;13,1;14,0;14,1;14,3;14,4;15,3;15,4;15,8;15,9;16,8;16,9;18,16;18,17;19,16;19,17;25,14;25,15;26,14;26,15",
        "small|7|B=|D=3,0;3,1;4,0;4,1|L=4,4;4,5;5,4;5,5",
        "wide|7|B=0,8;0,9;1,8;1,9;2,8;2,9;3,8;3,9;15,0;15,1;15,2;15,3;16,0;16,1;16,2;16,3;23,8;23,9;23,10;23,11;24,8;24,9;24,10;24,11|D=12,2;12,3;13,1;13,2;14,1;14,2|L=4,4;4,5;5,4;5,5;5,11;5,12;6,11;6,12;8,2;8,3;9,2;9,3;13,8;13,9;13,15;13,16;14,8;14,9;14,12;14,13;14,15;14,16;15,12;15,13",
        "small|8|B=4,1;4,2;4,3;4,4;5,1;5,2;5,3;5,4|D=|L=",
        "wide|8|B=13,12;13,13;13,14;13,15;14,12;14,13;14,14;14,15;15,1;15,2;15,8;15,9;16,1;16,2;16,8;16,9;17,1;17,2;17,8;17,9;18,1;18,2;18,8;18,9|D=|L=3,2;3,3;4,2;4,3;14,4;14,5;15,4;15,5;18,13;18,14;19,13;19,14",
        "small|9|B=|D=3,0;3,5;4,0;4,3;5,3|L=4,1;4,2;4,4;4,5;5,1;5,2;5,4;5,5",
        "wide|9|B=13,5;13,6;13,7;13,8;13,11;13,12;13,13;13,14;14,5;14,6;14,7;14,8;14,11;14,12;14,13;14,14;14,16;14,17;15,16;15,17;16,16;16,17;17,16;17,17|D=10,0;13,0;13,1;13,15;13,16;14,1;14,2;14,15;15,11;15,12;15,15;16,12;18,10;25,13|L=25,3;25,4;26,3;26,4",
        "small|10|B=|D=3,1;3,2;3,3;3,4;4,0;4,3;4,4;4,5;5,0;5,3;5,5|L=4,1;4,2;5,1;5,2",
        "wide|10|B=0,4;0,5;0,6;0,7;1,4;1,5;1,6;1,7;5,2;5,3;6,2;6,3;7,2;7,3;8,2;8,3;13,1;13,2;14,1;14,2;14,8;14,9;15,1;15,2;15,8;15,9;15,11;15,12;16,1;16,2;16,8;16,9;16,11;16,12;17,8;17,9;17,11;17,12;18,11;18,12|D=0,11;0,12;1,11;12,9;12,10;12,11;13,6;13,9;13,10;13,11;13,15;13,17;14,12;14,13;14,14;15,0;15,3;15,4;15,13;15,14;16,3;16,15;22,4;27,5|L=9,12;9,13;10,9;10,10;10,12;10,13;11,9;11,10;13,4;13,5;14,4;14,5;14,15;14,16;15,15;15,16;19,15;19,16;20,15;20,16;26,9;26,10;27,9;27,10",
    ];

    private static GridPosition[] ParseSquares(string field) =>
        field.Length == 0
            ? []
            : [.. field.Split(';').Select(pair =>
                {
                    var parts = pair.Split(',');
                    return new GridPosition(int.Parse(parts[0]), int.Parse(parts[1]));
                })];

    private static IEnumerable<(int Seed, string Fixture, GridPosition[] Blocked, GridPosition[] Difficult, GridPosition[] Low)>
        ParsedFingerprints()
    {
        foreach (var line in PreSliceFingerprints)
        {
            var parts = line.Split('|');
            var fixtureName = parts[0];
            var seed = int.Parse(parts[1]);
            var blocked = ParseSquares(parts[2][2..]);
            var difficult = ParseSquares(parts[3][2..]);
            var low = ParseSquares(parts[4][2..]);

            yield return (seed, fixtureName, blocked, difficult, low);
        }
    }

    [Fact]
    public void BoardsAreUnchangedByTheStructureVocabulary()
    {
        foreach (var (seed, fixtureName, blocked, difficult, low) in ParsedFingerprints())
        {
            var field = fixtureName == "small" ? GenerateSmall(seed) : GenerateWide(seed);

            Assert.Equal(
                blocked.OrderBy(s => (s.X, s.Y)),
                field.Blocked.OrderBy(s => (s.X, s.Y)));
            Assert.Equal(
                difficult.OrderBy(s => (s.X, s.Y)),
                field.DifficultTerrain.OrderBy(s => (s.X, s.Y)));
            Assert.Equal(
                low.OrderBy(s => (s.X, s.Y)),
                field.LowObstacles.OrderBy(s => (s.X, s.Y)));
        }
    }

    [Fact]
    public void EveryPieceIsPlacedByTheOpenFieldSite()
    {
        // No site draw exists until S3 — every board this slice can generate is, by the
        // design's own framing (§4.1), the open-field site: dressing only, no structure.
        for (var seed = 1; seed <= 50; seed++)
        {
            foreach (var field in new[] { GenerateSmall(seed), GenerateWide(seed) })
            {
                Assert.All(field.Pieces, piece => Assert.Equal(SiteType.OpenField, piece.PlacedBy));
            }
        }
    }

    [Fact]
    public void NoGapPieceIsEverProducedYet()
    {
        // Gap is forward vocabulary for S3's carved gaps and fords; this slice never
        // threads protectedSquares from a site, so nothing can ever produce one.
        for (var seed = 1; seed <= 50; seed++)
        {
            Assert.DoesNotContain(TerrainPieceKind.Gap, GenerateWide(seed).Pieces.Select(p => p.Kind));
        }
    }

    [Fact]
    public void WallAndLowObstacleFootprintsEachBecomeExactlyOnePiece()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var field = GenerateWide(seed);

            var wallSquaresFromPieces = field.Pieces
                .Where(p => p.Kind == TerrainPieceKind.WallRun)
                .SelectMany(p => p.Squares)
                .ToHashSet();
            var lowSquaresFromPieces = field.Pieces
                .Where(p => p.Kind == TerrainPieceKind.LowObstacleCluster)
                .SelectMany(p => p.Squares)
                .ToHashSet();

            Assert.Equal(field.Blocked.ToHashSet(), wallSquaresFromPieces);
            Assert.Equal(field.LowObstacles.ToHashSet(), lowSquaresFromPieces);

            // All-or-nothing placement (TerrainGenerator's own remarks): every accepted
            // footprint is a whole 2x4, 4x2 or 2x2 rectangle, so every piece is too.
            foreach (var piece in field.Pieces.Where(p => p.Kind is TerrainPieceKind.WallRun or TerrainPieceKind.LowObstacleCluster))
            {
                Assert.Contains(piece.Squares.Count, new[] { 4, 8 });
            }
        }
    }

    [Fact]
    public void EveryDifficultSquareComesFromSomeDifficultRegionPiece()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var field = GenerateWide(seed);

            var difficultFromPieces = field.Pieces
                .Where(p => p.Kind == TerrainPieceKind.DifficultRegion)
                .SelectMany(p => p.Squares)
                .ToHashSet();

            Assert.Equal(field.DifficultTerrain.ToHashSet(), difficultFromPieces);
        }
    }

    [Fact]
    public void ABattlefieldBuiltWithNoPiecesArgumentHasNone() =>
        // Every pre-existing caller (hand-authored fixtures throughout the test suite)
        // still constructs a Battlefield with no pieces argument at all.
        Assert.Empty(new Battlefield(4, 4, blocked: [new GridPosition(1, 1)]).Pieces);
}
