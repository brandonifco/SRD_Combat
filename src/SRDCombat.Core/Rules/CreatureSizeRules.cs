using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// What a creature's printed size means on the grid.
/// </summary>
/// <remarks>
/// <para>
/// The SRD's Creature Size and Space table (printed page 14): Tiny 2½ by 2½ feet (four
/// per square), Small and Medium 1 square, Large 4 squares (2 by 2), Huge 9 squares
/// (3 by 3), Gargantuan 16 squares (4 by 4). Every space is square, so the table is one
/// number per size — the length of a side, in squares.
/// </para>
/// <para>
/// <b>One deliberate divergence from print, signed off by the designer on
/// 2026-08-25 (#429): the Tiny "4 per square" space is not modelled — a Tiny creature
/// occupies its square alone.</b> Sub-square positions would ripple through the entire
/// grid model (positions, distance, areas, cover, both clients) for exactly three
/// creatures in the tier-1 pool — the Flying Snake, the Sphinx of Wonder and the
/// Will-o'-Wisp — and no builder path ever stacks Tiny creatures, so the case the rule
/// exists for cannot arise. The printed <em>pass-through-Tiny</em> clause is a separate
/// rule and is unaffected by this divergence: see <see cref="MovementRules"/>, where a
/// Tiny creature's square is walked through and is not Difficult Terrain.
/// </para>
/// <para>
/// The table is generalized to N by N rather than special-cased at Large because Huge is
/// already fielded (the Awakened Tree, CR 2) and the generalization costs nothing.
/// Gargantuan never appears at CR 4 or below anywhere in the corpus, and falls out free.
/// </para>
/// </remarks>
public static class CreatureSizeRules
{
    /// <summary>
    /// How many squares on a side this creature's space is: 1 for Tiny through Medium,
    /// 2 for Large, 3 for Huge, 4 for Gargantuan.
    /// </summary>
    public static int SpaceSpanSquares(CreatureSize size) => size switch
    {
        CreatureSize.Large => 2,
        CreatureSize.Huge => 3,
        CreatureSize.Gargantuan => 4,
        _ => 1,
    };
}
