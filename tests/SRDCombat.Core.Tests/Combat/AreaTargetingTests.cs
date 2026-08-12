using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Which squares each area of effect covers.
/// </summary>
/// <remarks>
/// <para>
/// Most of this is a stated interpretation rather than a derivation — the SRD describes
/// areas for a table with a ruler — and <see cref="AreaTargeting"/> is where each reading
/// is written down. These tests pin the readings so a change to one is deliberate.
/// </para>
/// <para>
/// One of them is not an interpretation at all: <b>"An Emanation's origin (creature or
/// object) isn't included in the area of effect unless its creator decides otherwise"</b>
/// — the rules glossary, printed page 181. The engine covered the origin square until
/// that sentence was read, which is why the exclusion has a test of its own.
/// </para>
/// </remarks>
public class AreaTargetingTests
{
    private static readonly Battlefield Field = new(12, 12);

    [Fact]
    public void AnEmanationExcludesItsOriginSquare()
    {
        var origin = new GridPosition(5, 5);

        var covered = AreaTargeting.Cover(
            new EffectArea(AreaShape.Emanation, 10),
            origin,
            origin,
            Field);

        Assert.DoesNotContain(origin, covered);

        // Everything else within the radius is still caught, so the exclusion is the
        // origin square alone and not a hole around it.
        Assert.Contains(new GridPosition(6, 5), covered);
        Assert.Contains(new GridPosition(4, 4), covered);
        Assert.Contains(new GridPosition(7, 5), covered);
        Assert.DoesNotContain(new GridPosition(8, 5), covered);
    }

    [Fact]
    public void ASphereKeepsItsCentreSquare()
    {
        // A Sphere is centred on a chosen point rather than extending from a creature,
        // and the glossary gives it no exclusion — so the shape that looks most like an
        // Emanation deliberately does not share its rule.
        var centre = new GridPosition(5, 5);

        var covered = AreaTargeting.Cover(
            new EffectArea(AreaShape.Sphere, 10),
            new GridPosition(0, 0),
            centre,
            Field);

        Assert.Contains(centre, covered);
    }

    [Fact]
    public void NoShapeThatExtendsFromACreatureCoversThatCreature()
    {
        // The invariant SimpleTacticsPolicy now rests on: with the Emanation exclusion
        // verified, all three creature-origin shapes agree, so a monster can never catch
        // itself in its own breath.
        var origin = new GridPosition(5, 5);
        var aim = new GridPosition(8, 5);

        foreach (var area in new[]
                 {
                     new EffectArea(AreaShape.Emanation, 20),
                     new EffectArea(AreaShape.Cone, 20),
                     new EffectArea(AreaShape.Line, 30, 5),
                 })
        {
            Assert.DoesNotContain(origin, AreaTargeting.Cover(area, origin, aim, Field));
        }
    }

    [Fact]
    public void AConeReachesForwardAndNotBackward()
    {
        var origin = new GridPosition(5, 5);

        var covered = AreaTargeting.Cover(
            new EffectArea(AreaShape.Cone, 15),
            origin,
            new GridPosition(8, 5),
            Field);

        Assert.Contains(new GridPosition(7, 5), covered);
        Assert.DoesNotContain(new GridPosition(3, 5), covered);
    }

    [Fact]
    public void ALineIsAsLongAsPrintedAndNoWider()
    {
        var origin = new GridPosition(0, 5);

        var covered = AreaTargeting.Cover(
            new EffectArea(AreaShape.Line, 30, 5),
            origin,
            new GridPosition(6, 5),
            Field);

        Assert.Contains(new GridPosition(6, 5), covered);
        Assert.DoesNotContain(new GridPosition(7, 5), covered);
        Assert.DoesNotContain(new GridPosition(3, 6), covered);
    }

    [Fact]
    public void ACylinderIsRefusedRatherThanTreatedAsASphere()
    {
        Assert.False(AreaTargeting.CanResolve(AreaShape.Cylinder));
        Assert.Empty(AreaTargeting.Cover(
            new EffectArea(AreaShape.Cylinder, 20),
            new GridPosition(0, 0),
            new GridPosition(5, 5),
            Field));
    }
}
