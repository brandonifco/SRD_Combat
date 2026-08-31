using SRDCombat.Viewer;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="FightScreen.FloatingNumberMotion"/> (#298) — the floating damage/miss
/// text's rise-and-fade curve, extracted the way <see cref="FightScreen.ActiveRingAlpha"/>
/// was for #518: a curve that reads no clock is one two probe runs of identical code
/// cannot disagree about, and pinning its shape here is what would catch a future version
/// that reached for <c>Time.GetTicksMsec()</c> instead of the delta-accumulated elapsed
/// time <c>FloatingNumber.Elapsed</c> actually carries.
/// </summary>
public class FloatingNumberMotionTests
{
    private const double Seconds = 0.9;

    [Fact]
    public void ItStartsAtTheTokenWithFullOpacity()
    {
        var (offsetY, alpha) = FightScreen.FloatingNumberMotion(0);

        Assert.Equal(0f, offsetY, precision: 4);
        Assert.Equal(1f, alpha, precision: 4);
    }

    [Fact]
    public void ItEndsFullyRisenAndFullyFaded()
    {
        var (offsetY, alpha) = FightScreen.FloatingNumberMotion(Seconds);

        Assert.True(offsetY < 0f, "a number rises — its offset should move up (negative Y).");
        Assert.Equal(0f, alpha, precision: 4);
    }

    [Fact]
    public void ItNeverRisesOrFadesPastItsBounds()
    {
        // Sampled well past its own lifetime — a caller that forgets to prune an expired
        // entry must not get a number sailing off past where it settled, or one that
        // pops back to visible.
        var (atLifetime, _) = FightScreen.FloatingNumberMotion(Seconds);
        var (long1, longAlpha) = FightScreen.FloatingNumberMotion(Seconds * 10);

        Assert.Equal(atLifetime, long1, precision: 4);
        Assert.Equal(0f, longAlpha, precision: 4);
    }

    [Fact]
    public void RiseAndFadeAreMonotonic()
    {
        // Legible for its whole short life rather than drawing attention to its own
        // motion (FloatingNumber's remarks) — a straight, unreversed rise and fade.
        var samples = Enumerable.Range(0, 20)
            .Select(step => FightScreen.FloatingNumberMotion(step * Seconds / 19))
            .ToList();

        for (var index = 1; index < samples.Count; index++)
        {
            Assert.True(samples[index].OffsetY <= samples[index - 1].OffsetY);
            Assert.True(samples[index].Alpha <= samples[index - 1].Alpha);
        }
    }

    [Fact]
    public void ItIsAFunctionOfTimeAloneAndHoldsNoState()
    {
        var first = FightScreen.FloatingNumberMotion(0.4);
        _ = FightScreen.FloatingNumberMotion(0.89);
        _ = FightScreen.FloatingNumberMotion(0.0);

        Assert.Equal(first, FightScreen.FloatingNumberMotion(0.4));
    }
}
