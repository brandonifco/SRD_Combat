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
