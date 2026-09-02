using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The trip-wire for the layout invariants <see cref="TerrainGenerator.ContestedRegions"/>
/// rests on (#452, the #412 misattribution-guard pattern). Kept in its own class, not
/// appended to <see cref="TerrainGeneratorTests"/>, so it does not collide with the S4
/// dressing work landing on that file in parallel.
/// </summary>
/// <remarks>
/// <para>
/// #452 was filed against the #451-era reading, which derived the contested band from
/// <c>partySpawns[0].X</c> / <c>monsterSpawns[0].X</c> — a single representative spawn.
/// Reading the current <see cref="TerrainGenerator.ContestedRegions"/> (post-#585) shows
/// that reading is already gone: the method's own remarks state it now reads "the
/// <em>extent</em> of each side's reserved squares (min/max over every square any body
/// on that side occupies), never a single representative square". So per #452's own
/// acceptance criteria ("if the rebase... already generalized the derivation, the
/// trip-wire pins the new invariant instead"), this pins the <b>min/max</b> shape, not
/// the retired single-X one.
/// </para>
/// <para>
/// The min/max derivation still rests on two things nothing asserts, straight from real
/// <see cref="EncounterFactory"/> output rather than hand-built fixtures, so a change to
/// <c>EncounterFactory.PlaceSides</c> or <c>SpawnPlacement.Fit</c> that breaks either one
/// is caught here instead of silently degrading the contested-ground bias:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Columns and CornerGroups:</b> the party's and the monsters' occupied X ranges are
/// disjoint (one side's extent lies wholly east or west of the other's). Violating this
/// does not crash <c>ContestedRegions</c> — it falls back to the whole board, silently,
/// for every fight from then on, which is exactly the invisible-misplacement shape #452
/// and the #412 pattern are about.
/// </item>
/// <item>
/// <b>Surrounded:</b> the party's reserved squares fill their own bounding box solid —
/// the "contiguous block" <c>ContestedRegions</c>' four-strip framing assumes when it
/// excludes the whole bounding box from the ring.
/// </item>
/// </list>
/// <para>
/// Design doc §9 (deployment zones, a later slice) is the named, scheduled source of the
/// counter-example: it retires single-file columns for 2-3-deep block regions, which
/// could either leave both invariants intact (deeper blocks, still disjoint, still
/// solid) or break them (interleaved zones; a block with a deliberate gap). Either
/// failure message below says which decision that forces.
/// </para>
/// </remarks>
public class ContestedRegionsInvariantTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void ColumnsAndCornerGroupsKeepEachSidesOccupiedColumnsDisjoint()
    {
        // Level 3: below it every fight is Columns by design (EncounterFactoryTests.
        // BelowLevelThreeEveryFightOpensAsColumns), and from it all three layouts are in
        // play (FromLevelThreeEveryLayoutIsDrawnSometimes measures both Columns and
        // CornerGroups appearing well inside 60 seeds), so 200 seeds at level 3 gives
        // both branches real, repeated exercise rather than one lucky draw.
        var party = PregeneratedParty.Build(Content, level: 3);
        var checkedLayouts = new HashSet<BattleLayout>();

        foreach (var seed in Enumerable.Range(1, 200))
        {
            var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(seed));

            if (fight.Layout is not (BattleLayout.Columns or BattleLayout.CornerGroups))
            {
                continue;
            }

            checkedLayouts.Add(fight.Layout);

            var partyReserved = Reserved(fight, PregeneratedParty.SideId).ToArray();
            var monsterReserved = Reserved(fight, EncounterFactory.MonsterSideId).ToArray();

            var partyMinX = partyReserved.Min(square => square.X);
            var partyMaxX = partyReserved.Max(square => square.X);
            var monsterMinX = monsterReserved.Min(square => square.X);
            var monsterMaxX = monsterReserved.Max(square => square.X);

            var disjoint = partyMaxX < monsterMinX || monsterMaxX < partyMinX;

            Assert.True(
                disjoint,
                $"Seed {seed}, layout {fight.Layout}: party occupies X [{partyMinX},{partyMaxX}], "
                + $"monsters occupy X [{monsterMinX},{monsterMaxX}] — the two ranges overlap. "
                + "TerrainGenerator.ContestedRegions reads the Columns/CornerGroups band from "
                + "each side's X extent and needs those extents disjoint, or it silently falls "
                + "back to the whole-board rectangle — no crash, no other test failure, just a "
                + "lost contested-ground bias for every fight of this shape from then on (design "
                + "doc §4.6/§9's own reading, #452, the #412 misattribution-guard pattern). "
                + "Design doc §9 (deployment zones) is the scheduled source of this counter-"
                + "example, retiring single-file columns for 2-3-deep block regions. The decision "
                + "this failure forces: either re-derive the contested band from the actual spawn "
                + "geometry so it copes with overlapping/interleaved zones, or accept the "
                + "whole-board fallback as correct for the new shape and say so in "
                + "ContestedRegions' own doc comment instead of leaving it silent.");
        }

        Assert.True(
            checkedLayouts.IsSupersetOf(new[] { BattleLayout.Columns, BattleLayout.CornerGroups }),
            "Seeds 1-200 at level 3 did not draw both Columns and CornerGroups, so this trip-wire "
            + $"only exercised {string.Join(", ", checkedLayouts)} — widen the seed sweep rather "
            + "than trust a pass that never reached the other branch.");
    }

    [Fact]
    public void SurroundedKeepsThePartyASolidBlock()
    {
        var checkedAny = false;

        foreach (var fight in SurroundedFights(minimumMonsters: 1, sampleCount: 10))
        {
            checkedAny = true;

            var partyReserved = Reserved(fight, PregeneratedParty.SideId).Distinct().ToArray();

            var minX = partyReserved.Min(square => square.X);
            var maxX = partyReserved.Max(square => square.X);
            var minY = partyReserved.Min(square => square.Y);
            var maxY = partyReserved.Max(square => square.Y);
            var boundingBoxArea = (maxX - minX + 1) * (maxY - minY + 1);

            Assert.True(
                partyReserved.Length == boundingBoxArea,
                $"Party reserved squares ({partyReserved.Length}) do not fill their own bounding "
                + $"box solid ({boundingBoxArea} squares, X [{minX},{maxX}] Y [{minY},{maxY}]). "
                + "TerrainGenerator.ContestedRegions frames Surrounded's four ring strips around "
                + "the party's bounding box on the reading that the bounding box IS the party's "
                + "occupied block (its own remarks: the strips 'frame the party's own bounding "
                + "box'). A gap inside that box is ground the strip-framing wrongly excludes from "
                + "the contested-ground bias, silently, with nothing else failing (#452, the #412 "
                + "misattribution-guard pattern). Design doc §9 (deployment zones) is the "
                + "scheduled source of this counter-example. The decision this failure forces: "
                + "either frame the strips around the party's actual occupied squares instead of "
                + "its bounding box, or accept the gap as acceptable slack and say so in "
                + "ContestedRegions' own doc comment instead of leaving it silent.");
        }

        Assert.True(checkedAny, "No seed drew a Surrounded fight — widen the seed sweep.");
    }

    private static IEnumerable<GridPosition> Reserved(Fight fight, string sideId) => fight.Encounter.Combatants
        .Where(combatant => combatant.SideId == sideId)
        .SelectMany(combatant => combatant.Space.Squares());

    /// <summary>Up to <paramref name="sampleCount"/> distinct Surrounded fights, drawn from a seed sweep.</summary>
    private static IEnumerable<Fight> SurroundedFights(int minimumMonsters, int sampleCount)
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var found = 0;

        foreach (var seed in Enumerable.Range(1, 200))
        {
            if (found >= sampleCount)
            {
                yield break;
            }

            var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, new SeededRandomSource(seed));

            if (fight.Layout == BattleLayout.Surrounded && fight.Built.Monsters.Count >= minimumMonsters)
            {
                found++;
                yield return fight;
            }
        }
    }
}
