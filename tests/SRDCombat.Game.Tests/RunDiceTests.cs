namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="RunDice.SeedFor"/>: a fixed SplitMix64 finalizer, pinned so its output can
/// never silently drift out from under a save that depends on it reproducing forever.
/// </summary>
public class RunDiceTests
{
    /// <summary>
    /// Exact values, computed independently outside this codebase and cross-checked
    /// against the shipped implementation. If any of these ever changes, every saved
    /// run's future fights change with it — that is exactly the drift this test exists
    /// to catch before it reaches a save file.
    /// </summary>
    [Theory]
    [InlineData(12345, 0, 724758815)]
    [InlineData(12345, 1, -2092316505)]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 0, -1796633444)]
    [InlineData(2026, 7, -1831060626)]
    public void SeedForIsPinned(int runSeed, int cleared, int expected)
    {
        Assert.Equal(expected, RunDice.SeedFor(runSeed, cleared));
    }

    [Fact]
    public void DifferentFightsInTheSameRunGetDifferentSeeds()
    {
        var seeds = Enumerable.Range(0, 30).Select(cleared => RunDice.SeedFor(12345, cleared)).ToArray();

        Assert.Equal(seeds.Length, seeds.Distinct().Count());
    }

    [Fact]
    public void SameFightIndexInDifferentRunsGetsDifferentSeeds()
    {
        Assert.NotEqual(RunDice.SeedFor(1, 5), RunDice.SeedFor(2, 5));
    }

    [Fact]
    public void SeedForIsPureAndDeterministic()
    {
        Assert.Equal(RunDice.SeedFor(999, 3), RunDice.SeedFor(999, 3));
    }
}
