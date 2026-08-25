using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Game;

namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="SaveFile"/>'s file-safety contract: a write that never leaves a torn file
/// on disk, exactly one rolling backup, and a load that survives a torn or missing
/// primary by falling back to that backup.
/// </summary>
/// <remarks>
/// Most of these operate on plain strings rather than real save JSON — <see
/// cref="SaveFile"/> does not care what it is writing, so exercising the file mechanics
/// with arbitrary content keeps the distinction between successive writes unambiguous.
/// The tests that exercise <see cref="SaveFile.LoadRun"/> use a real <see
/// cref="SavedRun"/>, because those need something <see cref="RunSave.FromJson"/> will
/// actually accept.
/// </remarks>
public sealed class SaveFileTests : IDisposable
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "srdcombat-savefile-tests", Guid.NewGuid().ToString("N"));

    public SaveFileTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string SavePath => Path.Combine(_directory, "save.json");

    private string BackupPath => SavePath + ".bak";

    private string TempPath => SavePath + ".tmp";

    private string OldPrimaryPath => SavePath + ".old";

    /// <summary>A real, loadable save — a fresh run, nothing cleared yet.</summary>
    private static string SomeSaveJson() => RunSave.ToJson(GauntletRun.Start(Content));

    [Fact]
    public void TheFirstWriteHasNoBackupAndLeavesNoTempFileBehind()
    {
        SaveFile.Write(SavePath, "first save");

        Assert.Equal("first save", File.ReadAllText(SavePath));
        Assert.False(File.Exists(BackupPath), "Nothing existed to back up yet.");
        Assert.False(File.Exists(TempPath), "The temp file must not survive a successful write.");
    }

    /// <summary>
    /// The bug QC caught reviewing #331 (#332): a stale <c>.bak</c> left over from a run
    /// whose primary was deleted separately must not survive a first write for that same
    /// path — otherwise it would predate the fresh primary and could be resurrected as
    /// this new run's history if the fresh primary were ever lost.
    /// </summary>
    [Fact]
    public void AFirstWriteDeletesAStaleBackupSoItCannotPredateTheNewPrimary()
    {
        File.WriteAllText(BackupPath, "stale run's backup");

        SaveFile.Write(SavePath, "new run's first save");

        Assert.False(File.Exists(BackupPath), "A first write must clear a stale backup, not leave it beside the new primary.");

        // Lose the fresh primary the way a crash or a deleted file would, and confirm
        // the stale run does not come back from a backup that should no longer exist.
        File.Delete(SavePath);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.Null(loaded.Saved);
        Assert.False(loaded.UsedBackup);
    }

    [Fact]
    public void ASecondWriteKeepsTheFirstAsExactlyOneBackup()
    {
        SaveFile.Write(SavePath, "first save");
        SaveFile.Write(SavePath, "second save");

        Assert.Equal("second save", File.ReadAllText(SavePath));
        Assert.Equal("first save", File.ReadAllText(BackupPath));
        Assert.False(File.Exists(TempPath));
    }

    [Fact]
    public void AThirdWriteRotatesTheBackupRatherThanAccumulating()
    {
        SaveFile.Write(SavePath, "first save");
        SaveFile.Write(SavePath, "second save");
        SaveFile.Write(SavePath, "third save");

        // The backup is always exactly what the primary held one write ago, never the
        // whole history.
        Assert.Equal("third save", File.ReadAllText(SavePath));
        Assert.Equal("second save", File.ReadAllText(BackupPath));
    }

    [Fact]
    public void LoadRunReadsBackAWrittenSaveWithNoFallback()
    {
        SaveFile.Write(SavePath, SomeSaveJson());

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(loaded.Saved);
        Assert.False(loaded.UsedBackup);
        Assert.Null(loaded.PrimaryFailureReason);
    }

    /// <summary>
    /// The acceptance test for #287's full flow, the way both clients actually run
    /// it: <see cref="SaveFile.LoadRun"/> only validates the file's own structure, so
    /// a save written against one content build loads there without complaint —
    /// content-dependent checks are <see cref="GauntletRun.Resume"/>'s, where the save
    /// actually meets the (different) loaded content, and it refuses there rather
    /// than crashing or silently proceeding.
    /// </summary>
    [Fact]
    public void ResumingASaveLoadedAgainstDifferentContentRefusesRatherThanCrashing()
    {
        SaveFile.Write(SavePath, SomeSaveJson());

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(loaded.Saved);
        Assert.False(loaded.UsedBackup);
        Assert.Null(loaded.PrimaryFailureReason);

        // A real second content build, not the same one with a field poked — fewer
        // monsters is enough to change ContentFingerprint, since it hashes the whole
        // id roster.
        var differentContent = new SrdContent(
            [.. Content.Monsters.Skip(1)],
            Content.Weapons,
            Content.Armor,
            Content.Species,
            Content.Backgrounds,
            Content.Classes,
            Content.Spells,
            Content.MagicItems);

        var failure = Assert.Throws<InvalidDataException>(
            () => GauntletRun.Resume(differentContent, loaded.Saved!));

        Assert.Contains("different content", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadRunReportsNothingWhenNeitherCopyExists()
    {
        var loaded = SaveFile.LoadRun(SavePath);

        Assert.Null(loaded.Saved);
        Assert.False(loaded.UsedBackup);
        Assert.Null(loaded.PrimaryFailureReason);
    }

    /// <summary>
    /// The acceptance test: a truncated primary (what a crash mid-write, or plain disk
    /// corruption, would leave) with an intact backup still lets the run load — and load
    /// as something the engine can actually resume, not merely parse.
    /// </summary>
    [Fact]
    public void ATornPrimaryFallsBackToAnIntactBackupAndTheRunLoads()
    {
        SaveFile.Write(SavePath, SomeSaveJson());
        SaveFile.Write(SavePath, SomeSaveJson());

        var intact = File.ReadAllText(SavePath);
        File.WriteAllText(SavePath, intact[..(intact.Length / 3)]);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(loaded.Saved);
        Assert.True(loaded.UsedBackup);
        Assert.NotNull(loaded.PrimaryFailureReason);

        var run = GauntletRun.Resume(Content, loaded.Saved!);

        Assert.Equal(RunOutcome.InProgress, run.Outcome);
    }

    /// <summary>
    /// #367's functional case: the write immediately after a fallback load takes the
    /// branch that used to call <c>File.Replace</c> over a corrupt primary. The happy
    /// path must still rotate cleanly — new content live, the old (corrupt) primary
    /// retired into the backup slot, no scratch files left behind.
    /// </summary>
    [Fact]
    public void AWriteOverACorruptPrimaryRotatesItIntoTheBackupSlotAndLeavesNoScratchFiles()
    {
        SaveFile.Write(SavePath, "first save");
        SaveFile.Write(SavePath, "second save");

        var intact = File.ReadAllText(SavePath);
        File.WriteAllText(SavePath, intact[..(intact.Length / 3)]);
        var corruptPrimary = File.ReadAllText(SavePath);

        SaveFile.Write(SavePath, "third save");

        Assert.Equal("third save", File.ReadAllText(SavePath));
        Assert.Equal(corruptPrimary, File.ReadAllText(BackupPath));
        Assert.False(File.Exists(TempPath));
        Assert.False(File.Exists(OldPrimaryPath));
    }

    /// <summary>
    /// #367's torn-write acceptance test: after a fallback load (primary corrupt,
    /// <c>.bak</c> holds the copy the load actually used), the *next* <see
    /// cref="SaveFile.Write"/> must never destroy that good backup before its own new
    /// primary is proven on disk. The old code took <c>File.Replace</c>'s Unix path —
    /// <c>unlink(bak)</c>, <c>link(path, bak)</c>, <c>rename(tmp, path)</c> — which
    /// discards the backup at the very first of those three steps, long before the new
    /// primary exists. This reproduces exactly that disk state — the corrupt primary
    /// already moved aside, nothing yet landed at <c>path</c> — the way the rest of this
    /// fixture simulates a torn write by reproducing its resulting disk state directly,
    /// and confirms the good backup a fallback load already used is still there and
    /// still resumable.
    /// </summary>
    [Fact]
    public void ACrashAfterMovingAsideACorruptPrimaryStillLeavesTheFallbackBackupLoadable()
    {
        SaveFile.Write(SavePath, SomeSaveJson());
        SaveFile.Write(SavePath, SomeSaveJson());

        var intact = File.ReadAllText(SavePath);
        File.WriteAllText(SavePath, intact[..(intact.Length / 3)]);

        var fallback = SaveFile.LoadRun(SavePath);
        Assert.NotNull(fallback.Saved);
        Assert.True(fallback.UsedBackup, "Setup must actually exercise the fallback-load path.");

        // Reproduce the disk state one rename into the next write: the corrupt primary
        // has been moved aside (SaveFile's first rename), but the new content has not
        // landed at `path` yet (its second rename). This is the exact moment
        // File.Replace's unlink(bak) used to destroy the good backup.
        File.Move(SavePath, OldPrimaryPath);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(loaded.Saved);
        Assert.True(loaded.UsedBackup, "The primary is missing mid-rotation; only the backup can serve it.");
        Assert.Null(loaded.PrimaryFailureReason);

        var run = GauntletRun.Resume(Content, loaded.Saved!);
        Assert.Equal(RunOutcome.InProgress, run.Outcome);
    }

    /// <summary>
    /// The next rename in the same sequence: once the new primary has landed at
    /// <c>path</c>, it must be loadable on its own — nothing that happens to the backup
    /// afterward (the final rename, retiring the old corrupt primary into <c>.bak</c>)
    /// can put the run at risk.
    /// </summary>
    [Fact]
    public void ACrashAfterTheNewPrimaryLandsIsLoadableEvenBeforeTheBackupRotationCompletes()
    {
        SaveFile.Write(SavePath, SomeSaveJson());
        SaveFile.Write(SavePath, SomeSaveJson());

        var intact = File.ReadAllText(SavePath);
        File.WriteAllText(SavePath, intact[..(intact.Length / 3)]);

        var newContent = SomeSaveJson();
        File.WriteAllText(TempPath, newContent);

        // The first two renames of the corrupt-primary branch, stopping before the
        // third (the backup rotation).
        File.Move(SavePath, OldPrimaryPath);
        File.Move(TempPath, SavePath);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(loaded.Saved);
        Assert.False(loaded.UsedBackup, "The new primary is live at `path`; it must not need the backup.");

        var run = GauntletRun.Resume(Content, loaded.Saved!);
        Assert.Equal(RunOutcome.InProgress, run.Outcome);
    }

    [Fact]
    public void AMissingPrimaryFallsBackToTheBackupToo()
    {
        SaveFile.Write(SavePath, SomeSaveJson());
        SaveFile.Write(SavePath, SomeSaveJson());

        File.Delete(SavePath);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(loaded.Saved);
        Assert.True(loaded.UsedBackup);

        // Missing is not the same failure as corrupt — nothing to report about a file
        // that was never there.
        Assert.Null(loaded.PrimaryFailureReason);
    }

    [Fact]
    public void NeitherCopyReadableReportsNoSaveRatherThanThrowing()
    {
        SaveFile.Write(SavePath, SomeSaveJson());
        SaveFile.Write(SavePath, SomeSaveJson());

        var truncated = File.ReadAllText(SavePath)[..5];
        File.WriteAllText(SavePath, truncated);
        File.WriteAllText(BackupPath, truncated);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.Null(loaded.Saved);
        Assert.False(loaded.UsedBackup);
        Assert.NotNull(loaded.PrimaryFailureReason);
        Assert.NotNull(loaded.BackupFailureReason);
    }

    /// <summary>
    /// The bug QC caught: a missing primary used to report "no save" even when a corrupt
    /// backup was sitting right there. A missing primary must not swallow a genuine
    /// backup failure.
    /// </summary>
    [Fact]
    public void AMissingPrimaryWithACorruptBackupSurfacesTheBackupsFailure()
    {
        SaveFile.Write(SavePath, SomeSaveJson());

        var truncated = File.ReadAllText(SavePath)[..5];
        File.WriteAllText(BackupPath, truncated);
        File.Delete(SavePath);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.Null(loaded.Saved);
        Assert.False(loaded.UsedBackup);
        Assert.Null(loaded.PrimaryFailureReason);
        Assert.NotNull(loaded.BackupFailureReason);
    }

    /// <summary>
    /// #361: adopting a legacy seed must survive a quit before the next cleared
    /// fight's autosave. <see cref="GauntletRun.AdoptSeed"/> writes through
    /// <see cref="SaveFile"/> immediately, so a reload with no fight cleared in
    /// between still sees the adopted seed rather than rolling a second one.
    /// </summary>
    [Fact]
    public void AdoptingASeedSurvivesAReloadWithNoFightClearedInBetween()
    {
        var seedless = GauntletRun.Start(Content).ToSave() with { Seed = null };
        SaveFile.Write(SavePath, ContentSerializer.Serialize(seedless));

        var loaded = SaveFile.LoadRun(SavePath);
        var run = GauntletRun.Resume(Content, loaded.Saved!);

        Assert.Equal(0, run.Seed);

        run.AdoptSeed(20260823, SavePath);

        // No fight cleared and no explicit save call here — AdoptSeed's own write must
        // already be on disk for this to see it.
        var reloaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(reloaded.Saved);
        Assert.Equal(20260823, reloaded.Saved!.Seed);
    }

    [Fact]
    public void DescribeUnloadableIsNullWhenNeitherFileEverExisted()
    {
        var loaded = SaveFile.LoadRun(SavePath);

        Assert.Null(SaveFile.DescribeUnloadable(SavePath, loaded));
    }

    /// <summary>
    /// The composed message for exactly the bug QC caught: it must name the backup's
    /// own failure, not just report the primary as missing and stop there.
    /// </summary>
    [Fact]
    public void DescribeUnloadableNamesACorruptBackupEvenWhenThePrimaryIsJustMissing()
    {
        SaveFile.Write(SavePath, SomeSaveJson());

        var truncated = File.ReadAllText(SavePath)[..5];
        File.WriteAllText(BackupPath, truncated);
        File.Delete(SavePath);

        var loaded = SaveFile.LoadRun(SavePath);
        var message = SaveFile.DescribeUnloadable(SavePath, loaded);

        Assert.NotNull(message);
        Assert.Contains(BackupPath, message, StringComparison.Ordinal);
        Assert.Contains("missing", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeUnloadableNamesBothFailuresWhenBothCopiesAreCorrupt()
    {
        SaveFile.Write(SavePath, SomeSaveJson());
        SaveFile.Write(SavePath, SomeSaveJson());

        var truncated = File.ReadAllText(SavePath)[..5];
        File.WriteAllText(SavePath, truncated);
        File.WriteAllText(BackupPath, truncated);

        var loaded = SaveFile.LoadRun(SavePath);
        var message = SaveFile.DescribeUnloadable(SavePath, loaded);

        Assert.NotNull(message);
        Assert.Contains(SavePath, message, StringComparison.Ordinal);
        Assert.Contains(BackupPath, message, StringComparison.Ordinal);
    }
}
