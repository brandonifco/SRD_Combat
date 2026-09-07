using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The trip-wire for the buffer promise <c>SiteGenerator.FordRanges</c> makes and does
/// not fully keep (#586, the #412 misattribution-guard pattern — "a corpus invariant
/// asserted nowhere").
/// </summary>
/// <remarks>
/// <para>
/// <c>FordRanges</c>' own doc comment promises every carved gap "at least one square of
/// run on both sides" — a buffer square standing between two gaps when
/// <c>count == 2</c>, so two carved gaps read as two, not one wider gap with no wall
/// between. Its placement loop only rejects <b>overlap</b>
/// (<c>ranges[index].Start &lt;= ranges[index - 1].End</c>); it does not reject
/// <b>adjacency</b> (<c>ranges[index].Start == ranges[index - 1].End + 1</c>, zero
/// squares of run between them). <see cref="SiteGenerator.PlaceCentralWall"/> passes it
/// <c>runLength</c> (four-fifths of the board's height, clamped — 14 on this project's
/// own 28x18 board), split three ways for two gaps: <c>segment = 14 / 3 = 4</c>. At
/// <see cref="SiteGenerator.GapWidth"/> 4 that segment equals the gap width itself, so
/// the two ideal starts land exactly <c>segment</c> apart with zero buffer squares
/// between them rather than the promised one — issue #586's own worked example, and the
/// knockout below reproduces it on <see cref="SiteType.CentralWall"/> exactly.
/// <see cref="SiteGenerator.PlaceCrossing"/> passes the <em>board's full height</em>
/// (18, not 14) instead, so its segment (6) does not run out until a wider gap (6) than
/// <c>CentralWall</c>'s (4) — the same risk, on a longer fuse. Either way, two carved
/// gaps merge into a single gap twice as tall, silently — no crash, no other test
/// failure (qc's review of PR #585, filed as #586).
/// </para>
/// <para>
/// <b>Unreachable today only because of a corpus invariant nothing asserted the merge
/// against before this file</b>: <see cref="SiteGenerator.GapWidth"/> is
/// <c>Math.Max(2, largestSpanSquares)</c>, and <c>largestSpanSquares</c> is
/// <c>EncounterFactory</c>'s wiring of <c>MonsterPool.LargestSpan</c> — 3 today, on the
/// Awakened Tree's Huge space, already pinned on the corpus side by
/// <see cref="LargestSpanTests.TheTierOnePoolsLargestFootprintIsThreeSquares"/>. That
/// test is the half of this trip-wire that watches the corpus; this one is the half
/// that watches the generator's own arithmetic — it sweeps every
/// <c>largestSpanSquares</c> value the pool can field today (1-3, so
/// <see cref="SiteGenerator.GapWidth"/> 2 or 3) and asserts the buffer holds. Both halves
/// are green today. The day either moves — a Gargantuan creature admitted into the
/// pool, or a caller threading a wider <c>largestSpanSquares</c> some other way — is the
/// day this file is the one that catches the silent merge instead of a played fight
/// finding it: either change <c>FordRanges</c>' reject from "no overlap" to "no
/// adjacency" (<c>&lt;= End + 1</c>, the issue's own suggested one-line fix), or defend
/// the current behaviour as acceptable and say so in <c>FordRanges</c>' own doc comment
/// instead of leaving it silent.
/// </para>
/// </remarks>
public class FordRangesGapSeparationTests
{
    // The standard EncounterFactory board, matching SiteGeneratorTests' own fixture.
    private static readonly GridPosition[] PartySpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(8, y))];

    private static readonly GridPosition[] MonsterSpawns =
        [.. Enumerable.Range(7, 4).Select(y => new GridPosition(20, y))];

    private const int Width = 28;
    private const int Height = 18;

    [Theory]
    [InlineData(SiteType.CentralWall)]
    [InlineData(SiteType.Crossing)]
    public void EveryFieldableSpanKeepsAtLeastOneBufferSquareBetweenTwoGaps(SiteType site)
    {
        // Spans 1-3: every largestSpanSquares this project's own content can field today
        // (LargestSpanTests.TheTierOnePoolsLargestFootprintIsThreeSquares). GapWidth
        // floors at 2, so this exercises the two widths (2, 3) actually reachable.
        foreach (var span in new[] { 1, 2, 3 })
        {
            foreach (var layout in new[] { BattleLayout.Columns, BattleLayout.CornerGroups })
            {
                // The buffer assertion below only fires when a draw actually carved two
                // gaps — gapCount rolls 1, or the whole structure is rejected, plenty of
                // the time. Without this counter the sweep could still report green
                // after every single seed skipped the comparison it exists to make,
                // which is exactly the vacuous-pass shape a trip-wire must not have.
                var twoGapPlansObserved = 0;

                for (var seed = 1; seed <= 300; seed++)
                {
                    var plan = SiteGenerator.Place(
                        site, Width, Height, layout, PartySpawns, MonsterSpawns,
                        new SeededRandomSource(seed), span);

                    var gapPieces = plan.Pieces.Where(piece => piece.Kind == TerrainPieceKind.Gap).ToArray();

                    if (gapPieces.Length < 2)
                    {
                        // Placement rejected, or the draw carved only one gap — nothing
                        // to compare.
                        continue;
                    }

                    twoGapPlansObserved++;

                    // Each gap piece's row extent — the axis both CentralWall's and
                    // Crossing's gaps are carved along — sorted so adjacent pairs are
                    // compared in carved order rather than piece-list order.
                    var ranges = gapPieces
                        .Select(piece => (Start: piece.Squares.Min(s => s.Y), End: piece.Squares.Max(s => s.Y)))
                        .OrderBy(range => range.Start)
                        .ToArray();

                    for (var index = 1; index < ranges.Length; index++)
                    {
                        Assert.True(
                            ranges[index].Start > ranges[index - 1].End + 1,
                            $"Site {site} layout {layout} span {span} seed {seed}: gap "
                            + $"[{ranges[index - 1].Start},{ranges[index - 1].End}] and gap "
                            + $"[{ranges[index].Start},{ranges[index].End}] leave no buffer "
                            + "square between them — FordRanges' 'at least one square of run "
                            + "on both sides' promise is broken, and the two gaps read as one "
                            + "wider gap with no wall between (#586).");
                    }
                }

                Assert.True(
                    twoGapPlansObserved > 0,
                    $"Site {site} layout {layout} span {span}: no seed in 1-300 produced a "
                    + "two-gap plan, so the buffer assertion above never ran for this "
                    + "combination — this sweep would go green having checked nothing for "
                    + "it. Widen the seed range (or, if this combination genuinely cannot "
                    + "carve two gaps, drop it from the sweep and say why) rather than trust "
                    + "a pass that never exercised the invariant.");
            }
        }
    }
}
