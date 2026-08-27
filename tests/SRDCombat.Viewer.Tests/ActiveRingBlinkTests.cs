using SRDCombat.Viewer;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="FightScreen.ActiveRingAlpha"/> (#494, #518) — the active ring's blink
/// curve, extracted from the drawing property so it can be pinned at all.
/// </summary>
/// <remarks>
/// These exist because of how #518 was found. The blink shipped reading the clock with
/// no probe guard, which made two runs of identical code produce different captures —
/// and #327's entire behaviour-preservation net is byte-identical captures. Nothing
/// went red, because nothing was watching this curve. The guard itself lives in
/// <c>ActiveRingNow</c> and still is not reachable headless (it needs a live node);
/// what is pinned here is the curve's shape, which is where the cadence Brandon
/// approved actually lives.
/// </remarks>
public class ActiveRingBlinkTests
{
    private const float Floor = 0.25f;
    private const double Period = 1.2;

    [Fact]
    public void TheRingIsNeverFullyInvisible()
    {
        // The floor is a deliberate design call, not a rounding artefact: a hard on/off
        // blink read as the ring vanishing, which is worse than the static ring it
        // replaced. Sampled across several periods rather than at chosen points.
        for (var step = 0; step < 500; step++)
        {
            var alpha = FightScreen.ActiveRingAlpha(step * Period / 100.0);

            Assert.InRange(alpha, Floor - 0.0001f, 1.0001f);
        }
    }

    [Fact]
    public void TheCycleReachesBothItsFloorAndFullOpacity()
    {
        var samples = Enumerable.Range(0, 240)
            .Select(step => FightScreen.ActiveRingAlpha(step * Period / 240.0))
            .ToList();

        Assert.Equal(Floor, samples.Min(), precision: 2);
        Assert.Equal(1f, samples.Max(), precision: 2);
    }

    [Fact]
    public void ItStartsDimAndBrightensRatherThanFading()
    {
        // Phase matters: the ring should brighten into view. If the shift is dropped the
        // curve starts at full and fades, which reads as something switching off.
        Assert.Equal(Floor, FightScreen.ActiveRingAlpha(0), precision: 2);
        Assert.True(FightScreen.ActiveRingAlpha(Period / 4) > FightScreen.ActiveRingAlpha(0));
    }

    [Fact]
    public void TheCurveRepeatsOnTheStatedPeriod()
    {
        // The period is the number Brandon approved by eye (1.2 s). A test that pins the
        // shape but not the period would let the cadence drift silently.
        foreach (var offset in new[] { 0.0, 0.3, 0.61, 0.97 })
        {
            Assert.Equal(
                FightScreen.ActiveRingAlpha(offset),
                FightScreen.ActiveRingAlpha(offset + Period),
                precision: 4);

            Assert.Equal(
                FightScreen.ActiveRingAlpha(offset),
                FightScreen.ActiveRingAlpha(offset + (Period * 7)),
                precision: 3);
        }
    }

    [Fact]
    public void ItIsAFunctionOfTimeAloneAndHoldsNoState()
    {
        // Called out of order, the same input gives the same answer — the property this
        // whole issue is about. A stored-timer implementation would fail here.
        var first = FightScreen.ActiveRingAlpha(0.8);
        _ = FightScreen.ActiveRingAlpha(3.14159);
        _ = FightScreen.ActiveRingAlpha(0.0);

        Assert.Equal(first, FightScreen.ActiveRingAlpha(0.8), precision: 6);
    }
}
