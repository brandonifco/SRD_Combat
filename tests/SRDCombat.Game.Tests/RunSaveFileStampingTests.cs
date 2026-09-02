using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Game;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The one <see cref="RunSaveTests"/> case that stamps a save through the real
/// <see cref="SaveFile"/> seam, kept apart so <see cref="RunSaveTests"/> itself can run
/// in parallel.
/// </summary>
/// <remarks>
/// <see cref="SaveFile"/>'s fault seam is process-wide (see
/// <c>SaveFileTestCollection</c>), so any test that touches a real filesystem
/// <c>SaveFile</c> call must be serialised against the fault injection — hence the
/// collection attribute. This was the sole such test inside <see cref="RunSaveTests"/>;
/// moving it here lets that class shed the <c>DisableParallelization</c> collection and
/// overlap the rest of the suite. Behaviour is unchanged — same seed, same assertions.
/// </remarks>
[Collection("SaveFile filesystem fault injection")]
public class RunSaveFileStampingTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    /// <summary>A run with one fight behind it, so the save holds real progress.</summary>
    /// <remarks>
    /// A copy of <c>RunSaveTests.RunWithHistory</c>: pure in-memory, and duplicated rather
    /// than shared so this filesystem-serialised test stays self-contained in its own
    /// collection.
    /// </remarks>
    private static GauntletRun RunWithHistory(int seed = 11)
    {
        var run = GauntletRun.Start(Content);
        var random = new SeededRandomSource(seed);

        run.PrepareForNext(random);
        var fight = run.BeginNext(random);
        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
        run.CompleteFight(fight);

        // The premise every test here rests on: the seed clears the first fight, so the
        // save holds a run worth resuming. If this fires, pick another seed.
        Assert.Equal(RunOutcome.InProgress, run.Outcome);

        return run;
    }

    /// <summary>
    /// A save missing both #286's and #287's fields — the exact shape of a save
    /// written before either landed — loads, resumes, and is fully stamped with real
    /// values for both: the content version by the resolve <see cref="GauntletRun.ToSave"/>
    /// does on any autosave, the seed immediately by <see cref="GauntletRun.AdoptSeed"/>
    /// itself (#361).
    /// </summary>
    [Fact]
    public void ASaveMissingBothSeedAndContentVersionLoadsAndIsFullyStampedAfterOneAutosave()
    {
        var saved = RunWithHistory().ToSave() with { Seed = null, ContentVersion = null };
        var savePath = Path.Combine(Path.GetTempPath(), $"srdcombat-adoptseed-test-{Guid.NewGuid():N}.json");

        try
        {
            SaveFile.BeginNewRun(savePath, ContentSerializer.Serialize(saved));
            var loaded = SaveFile.LoadRun(savePath);

            Assert.NotNull(loaded.Saved);
            Assert.Null(loaded.Saved!.Seed);
            Assert.Null(loaded.Saved.ContentVersion);

            var run = GauntletRun.Resume(Content, loaded.Saved);
            Assert.Equal(RunOutcome.InProgress, run.Outcome);

            run.AdoptSeed(20260823, savePath);

            var reloaded = SaveFile.LoadRun(savePath);
            Assert.NotNull(reloaded.Saved);
            Assert.Equal(20260823, reloaded.Saved!.Seed);
            Assert.Equal(Content.ContentFingerprint, reloaded.Saved.ContentVersion);
        }
        finally
        {
            File.Delete(savePath);
            File.Delete(savePath + ".tmp");
            File.Delete(savePath + ".new");
            File.Delete(savePath + ".bak");
            File.Delete(savePath + ".old");
        }
    }
}
