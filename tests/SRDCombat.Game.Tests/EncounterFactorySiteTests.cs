using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The battlefield-overhaul S3 slice threaded all the way through
/// <see cref="EncounterFactory"/>: a site drawn between layout and terrain, its structure
/// respected by every downstream guarantee the ladder already relies on.
/// </summary>
/// <remarks>
/// Issue #436's acceptance criteria, each with its own test below: span-aware
/// connectivity over a seed sweep across every layout and warband counts; every carved
/// gap/ford at least the threaded span; a sample of built fights resolving under
/// <see cref="SimpleTacticsPolicy.RunToCompletion"/>; and the one severe risk a
/// battlefield-generation slice can carry — a stall — checked directly rather than swept
/// (CLAUDE.md's checkpoint policy, and issue #436's superseding comment of 2026-08-28).
/// </remarks>
public class EncounterFactorySiteTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    private static IEnumerable<Fight> Sweep(int fromSeed, int toSeed, int level, bool horde = false)
    {
        var party = PregeneratedParty.Build(Content, level);

        for (var seed = fromSeed; seed <= toSeed; seed++)
        {
            yield return EncounterFactory.Build(
                Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(seed), horde: horde);
        }
    }

    private static HashSet<GridPosition> ReservedSquares(Fight fight) =>
        [.. fight.Encounter.Combatants.SelectMany(combatant => combatant.Space.Squares())];

    private static int LargestSpan(Fight fight) =>
        fight.Encounter.Combatants.Select(combatant => combatant.Stats.SpaceSpanSquares).DefaultIfEmpty(1).Max();

    [Fact]
    public void EveryBoardStaysConnectedAtItsThreadedSpanAcrossLevelsLayoutsAndWarbands()
    {
        var fights = Sweep(1, 150, level: 1)
            .Concat(Sweep(1, 150, level: 3))
            .Concat(Sweep(1, 150, level: 5))
            .Concat(Sweep(1, 60, level: 3, horde: true))
            .Concat(Sweep(1, 60, level: EncounterFactory.HordeMinimumLevel, horde: true));

        foreach (var fight in fights)
        {
            var field = fight.Encounter.Battlefield;
            var impassable = field.Blocked.Concat(field.LowObstacles).ToArray();
            var reserved = ReservedSquares(fight);
            var span = LargestSpan(fight);

            Assert.True(
                GridConnectivity.StaysConnected(impassable, [], reserved, field.Width, field.Height, span),
                $"Layout {fight.Layout}, {fight.Built.Monsters.Count} monsters: board did not stay connected "
                + $"at span {span}.");
        }
    }

    [Fact]
    public void EveryCarvedGapOrFordIsAtLeastTheThreadedSpanWide()
    {
        var fights = Sweep(1, 200, level: 1)
            .Concat(Sweep(1, 200, level: 3))
            .Concat(Sweep(1, 100, level: 3, horde: true));

        var checkedAny = false;

        foreach (var fight in fights)
        {
            var span = LargestSpan(fight);
            var expected = Math.Max(2, span);

            var gapPieces = fight.Encounter.Battlefield.Pieces.Where(p => p.Kind == TerrainPieceKind.Gap).ToArray();

            foreach (var gap in gapPieces)
            {
                checkedAny = true;

                // A wall's gap is a single column of rows; a crossing's ford is several
                // columns of rows — either way, "width" is the row count per column.
                var byColumn = gap.Squares.GroupBy(square => square.X);

                Assert.All(byColumn, column => Assert.True(
                    column.Count() >= expected,
                    $"Layout {fight.Layout}: a gap/ford column had {column.Count()} squares, expected >= {expected}."));
            }
        }

        Assert.True(checkedAny, "No gap or ford piece was drawn across the whole sweep.");
    }

    /// <summary>
    /// The first seed in <c>1..limit</c> whose built fight's site-drawn-or-not matches
    /// <paramref name="requireSite"/> — a cheap build-only scan (no policy run), so the
    /// severe-risk sample below can pick a handful of genuinely varied, genuinely
    /// structural fights rather than running the whole policy over a wide sweep, which is
    /// the difference between a sample and the full-range sweeps the checkpoint policy
    /// (CLAUDE.md, Standing conventions) reserves for the re-baselining checkpoint.
    /// </summary>
    private static Fight? FirstBuild(
        int level, bool horde, bool requireSite, int limit = 300)
    {
        var party = PregeneratedParty.Build(Content, level);

        for (var seed = 1; seed <= limit; seed++)
        {
            var fight = EncounterFactory.Build(
                Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(seed), horde: horde);

            var hasSite = fight.Encounter.Battlefield.Pieces.Any(
                p => p.PlacedBy is SiteType.CentralWall or SiteType.Crossing);

            if (hasSite == requireSite)
            {
                return fight;
            }
        }

        return null;
    }

    [Fact]
    public void ASampleOfBuiltFightsResolvesUnderTheTacticsPolicyWithNoStall()
    {
        // The one severe risk battlefield generation carries (CLAUDE.md's checkpoint
        // policy): a structure narrow enough, or placed badly enough, that the bot's own
        // pathing stalls instead of resolving. A small, genuinely varied sample — every
        // level this game plays, horde included, structural sites and open field both —
        // each run to completion (or the round limit) under the same policy the ladder
        // itself uses. "A sample" per issue #436, not the canonical seed ranges: a PR
        // does not run those (CLAUDE.md's checkpoint policy).
        var candidates = new[]
        {
            FirstBuild(level: 1, horde: false, requireSite: true),
            FirstBuild(level: 1, horde: false, requireSite: false),
            FirstBuild(level: 3, horde: false, requireSite: true),
            FirstBuild(level: 3, horde: false, requireSite: false),
            FirstBuild(level: 5, horde: false, requireSite: true),
            FirstBuild(level: 5, horde: false, requireSite: false),
            FirstBuild(level: EncounterFactory.HordeMinimumLevel, horde: true, requireSite: true),
            FirstBuild(level: EncounterFactory.HordeMinimumLevel, horde: true, requireSite: false),
        };

        var fights = candidates.Where(fight => fight is not null).Select(fight => fight!).ToArray();
        var siteFights = fights.Count(fight => fight.Encounter.Battlefield.Pieces.Any(
            p => p.PlacedBy is SiteType.CentralWall or SiteType.Crossing));

        Assert.True(fights.Length >= 6, $"Only {fights.Length} of 8 candidate fights were found.");
        Assert.True(siteFights >= 3, $"Only {siteFights} sampled fights drew a structural site.");

        foreach (var fight in fights)
        {
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);

            Assert.True(
                fight.Encounter.IsComplete,
                $"Layout {fight.Layout}, {fight.Built.Monsters.Count} monsters, "
                + $"sites {string.Join(",", fight.Encounter.Battlefield.Pieces.Select(p => p.PlacedBy).Distinct())}: "
                + "hit the round limit instead of resolving.");
        }
    }

    [Fact]
    public void SurroundedNeverDrawsACentralWallOrCrossingThroughTheFullPipeline()
    {
        // The flagged implementer's-choice reading (SiteGenerator's own remarks):
        // Surrounded re-rolls to open field rather than drawing arcs, all the way
        // through EncounterFactory, not just at the unit level.
        var party = PregeneratedParty.Build(Content, level: EncounterFactory.HordeMinimumLevel);
        var found = false;

        for (var seed = 1; seed <= 400; seed++)
        {
            var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(seed));

            if (fight.Layout != BattleLayout.Surrounded)
            {
                continue;
            }

            found = true;

            Assert.DoesNotContain(
                fight.Encounter.Battlefield.Pieces,
                p => p.PlacedBy is SiteType.CentralWall or SiteType.Crossing);
        }

        Assert.True(found, "No Surrounded fight drawn in 400 seeds.");
    }

    // Board dumps: P/M are spawns, W/R/F/X are the site's own structure (wall, ruined
    // low-obstacle stretch, gap/ford, difficult band), #/o/~ are ordinary dressing, and .
    // is empty. Captured once from the actual generator output and pinned here exactly
    // as issue #436 asks — one crossing and one central-wall board for a layout whose
    // site placement reading differs (Columns' "middle third" versus CornerGroups' whole
    // open band, design §4.6). Surrounded carries no fixture: see the test above and
    // SiteGenerator's own remarks for why no board of this shape exists to pin.
    private const string ColumnsCrossingSeed5 =
        ".............XXX............\n" +
        ".............XXX~...........\n" +
        "oo......####.XXX...##.......\n" +
        "oo......####.XXX...##.......\n" +
        ".............XXX...##.......\n" +
        ".............XXX...##.......\n" +
        ".............XXX............\n" +
        "........P....XXX....M.......\n" +
        "........P....FFF....M.......\n" +
        "........P....FFF............\n" +
        "........P....XXX............\n" +
        ".............XXX###.........\n" +
        ".............XXX###.........\n" +
        ".............XXX............\n" +
        "..####.......XXX###.........\n" +
        "..####.......XXX###.........\n" +
        ".............XXX............\n" +
        ".............XXX............";

    private const string ColumnsCentralWallSeed3 =
        "...................oo.......\n" +
        "..............~oo..oo......~\n" +
        ".............W.oo..........~\n" +
        ".............W.............~\n" +
        ".............W......~~......\n" +
        ".............W..............\n" +
        ".............W..............\n" +
        "........P....W......M.......\n" +
        "........P....F..............\n" +
        "........P....F.oo...........\n" +
        "........P....W.oo...........\n" +
        ".............W..............\n" +
        ".............R..............\n" +
        ".............R.##...........\n" +
        ".............R.##...........\n" +
        ".............R.##...........\n" +
        "...............##...........\n" +
        "............................";

    private const string CornerGroupsCrossingSeed15 =
        "...............XXXX...oo....\n" +
        "...............XXXX.M.oo....\n" +
        "...............XXXX.M.~.....\n" +
        "............oo.XXXX...~.....\n" +
        "............oo.XXXX.........\n" +
        "...............FFFF.........\n" +
        "............oo.FFFF.........\n" +
        "........P...oo.XXXX.........\n" +
        "........P....~.XXXX.........\n" +
        "........P......XXXX....~....\n" +
        "........P....~.XXXX....~....\n" +
        "...............FFFF.........\n" +
        "...............FFFF.........\n" +
        "...............XXXX.........\n" +
        "..........####.XXXX.........\n" +
        "..........####.XXXX.M.......\n" +
        "...............XXXX.M.......\n" +
        "...............XXXX.........";

    private const string CornerGroupsCentralWallSeed40 =
        "............................\n" +
        "....................M.......\n" +
        "....................M.......\n" +
        ".................##.........\n" +
        "..............W..##.........\n" +
        "..............W..##.........\n" +
        "..............W..##.........\n" +
        "........P.....W.............\n" +
        "........P.....W.............\n" +
        "........P.....W.............\n" +
        "........P.....F....oo.......\n" +
        "...oo.........F....oo.......\n" +
        "...oo.........W.............\n" +
        "..............W.............\n" +
        "..............W.##..........\n" +
        "..............W.##..M.......\n" +
        "..............W.##..M.......\n" +
        "..............W.##..........";

    private static string Dump(Fight fight, SiteType site)
    {
        var field = fight.Encounter.Battlefield;
        var party = fight.Encounter.Combatants
            .Where(c => c.SideId == PregeneratedParty.SideId).Select(c => c.Position).ToHashSet();
        var monsters = fight.Encounter.Combatants
            .Where(c => c.SideId == EncounterFactory.MonsterSideId).Select(c => c.Position).ToHashSet();

        var siteWall = field.Pieces.Where(p => p.PlacedBy == site && p.Kind == TerrainPieceKind.WallRun)
            .SelectMany(p => p.Squares).ToHashSet();
        var siteLow = field.Pieces.Where(p => p.PlacedBy == site && p.Kind == TerrainPieceKind.LowObstacleCluster)
            .SelectMany(p => p.Squares).ToHashSet();
        var siteDifficult = field.Pieces.Where(p => p.PlacedBy == site && p.Kind == TerrainPieceKind.DifficultRegion)
            .SelectMany(p => p.Squares).ToHashSet();
        var siteGap = field.Pieces.Where(p => p.PlacedBy == site && p.Kind == TerrainPieceKind.Gap)
            .SelectMany(p => p.Squares).ToHashSet();

        var rows = new List<string>();

        for (var y = 0; y < field.Height; y++)
        {
            var row = new System.Text.StringBuilder();

            for (var x = 0; x < field.Width; x++)
            {
                var pos = new GridPosition(x, y);

                row.Append(
                    party.Contains(pos) ? 'P'
                    : monsters.Contains(pos) ? 'M'
                    : siteGap.Contains(pos) ? 'F'
                    : siteWall.Contains(pos) ? 'W'
                    : siteLow.Contains(pos) ? 'R'
                    : siteDifficult.Contains(pos) ? 'X'
                    : field.Blocked.Contains(pos) ? '#'
                    : field.LowObstacles.Contains(pos) ? 'o'
                    : field.DifficultTerrain.Contains(pos) ? '~'
                    : '.');
            }

            rows.Add(row.ToString());
        }

        return string.Join('\n', rows);
    }

    [Fact]
    public void ColumnsPinsOneCrossingAndOneCentralWallBoard()
    {
        var party = PregeneratedParty.Build(Content, level: 1);

        var crossing = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(5));
        var wall = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(3));

        Assert.Equal(ColumnsCrossingSeed5, Dump(crossing, SiteType.Crossing));
        Assert.Equal(ColumnsCentralWallSeed3, Dump(wall, SiteType.CentralWall));
    }

    [Fact]
    public void CornerGroupsPinsOneCrossingAndOneCentralWallBoard()
    {
        var party = PregeneratedParty.Build(Content, level: EncounterFactory.HordeMinimumLevel);

        var crossing = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(15));
        var wall = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(40));

        Assert.Equal(BattleLayout.CornerGroups, crossing.Layout);
        Assert.Equal(BattleLayout.CornerGroups, wall.Layout);
        Assert.Equal(CornerGroupsCrossingSeed15, Dump(crossing, SiteType.Crossing));
        Assert.Equal(CornerGroupsCentralWallSeed40, Dump(wall, SiteType.CentralWall));
    }
}
