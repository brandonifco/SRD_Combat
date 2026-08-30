using System.Text.Json.Nodes;
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
[Collection("SaveFile filesystem fault injection")]
public sealed class SaveFileTests : IDisposable
{
    private static readonly SrdContent Content = TestContent.Srd;

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

    /// <summary>
    /// A real, loadable save distinguishable from another call by its seed — needed
    /// wherever a test must tell two successive writes' content apart on disk.
    /// <see cref="SomeSaveJson()"/> defaults its run's seed to 0, so two calls produce
    /// byte-identical JSON; the pregenerated party and starting state are otherwise
    /// fully deterministic (<see cref="GauntletRun.Start(SrdContent, IReadOnlyList{LadderStep}?, int, int)"/>).
    /// </summary>
    private static string SomeSaveJson(int seed) => RunSave.ToJson(GauntletRun.Start(Content, seed: seed));

    private void Write(string json)
    {
        if (SaveFile.LoadRun(SavePath).Saved is null)
        {
            SaveFile.BeginNewRun(SavePath, json);
        }
        else
        {
            SaveFile.ContinueWrite(SavePath, json);
        }
    }

    /// <summary>Whether <paramref name="json"/> parses as a save — used to confirm a
    /// setup step actually produced the corrupt content a test needs, rather than
    /// asserting on <see cref="SaveFile"/> behaviour indirectly.</summary>
    private static bool IsLoadableSaveJson(string json)
    {
        try
        {
            RunSave.FromJson(json);
            return true;
        }
        catch (Exception failure) when (failure is System.Text.Json.JsonException or InvalidDataException)
        {
            return false;
        }
    }

    [Fact]
    public void TheFirstWriteHasNoBackupAndLeavesNoTempFileBehind()
    {
        var first = SomeSaveJson(seed: 1);
        Write(first);

        Assert.Equal(first, File.ReadAllText(SavePath));
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

        SaveFile.BeginNewRun(SavePath, SomeSaveJson(seed: 2));

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
        var first = SomeSaveJson(seed: 1);
        var second = SomeSaveJson(seed: 2);
        Write(first);
        Write(second);

        Assert.Equal(second, File.ReadAllText(SavePath));
        Assert.Equal(first, File.ReadAllText(BackupPath));
        Assert.False(File.Exists(TempPath));
    }

    [Fact]
    public void AThirdWriteRotatesTheBackupRatherThanAccumulating()
    {
        var first = SomeSaveJson(seed: 1);
        var second = SomeSaveJson(seed: 2);
        var third = SomeSaveJson(seed: 3);
        Write(first);
        Write(second);
        Write(third);

        // The backup is always exactly what the primary held one write ago, never the
        // whole history.
        Assert.Equal(third, File.ReadAllText(SavePath));
        Assert.Equal(second, File.ReadAllText(BackupPath));
    }

    [Fact]
    public void MissingSavedMemberPropertyIsShownByTheUnloadableSaveMessage()
    {
        var document = JsonNode.Parse(SomeSaveJson())!.AsObject();
        document["members"]![0]!.AsObject().Remove("state");
        Write(document.ToJsonString());

        var loaded = SaveFile.LoadRun(SavePath);
        var message = SaveFile.DescribeUnloadable(SavePath, loaded);

        Assert.Null(loaded.Saved);
        Assert.NotNull(message);
        Assert.Contains("State", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadRunReadsBackAWrittenSaveWithNoFallback()
    {
        Write(SomeSaveJson());

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
        Write(SomeSaveJson());

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
        Write(SomeSaveJson());
        Write(SomeSaveJson());

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
    /// #367's must-fix, found by qc reviewing the first attempt at this fix: a plain
    /// two-copy fallback (primary, then <c>.bak</c>) is not enough on its own. This
    /// walks the exact sequence qc traced:
    /// <list type="bullet">
    /// <item>T0: primary corrupt, <c>.bak</c> good (a torn write, or plain disk
    /// corruption).</item>
    /// <item>T1: a fallback load resumes off <c>.bak</c>.</item>
    /// <item>T2: the next write completes in full — new content lands at <c>path</c>,
    /// and the corrupt primary it displaced is rotated into <c>.bak</c>, overwriting
    /// the good copy T1 just used. End state: good primary, corrupt <c>.bak</c>.</item>
    /// <item>T3: the *following* write's first rename moves that good primary aside to
    /// <c>.old</c> — and crashes there. <c>path</c> is now missing, <c>.bak</c> is
    /// still T2's corrupt content, and the only good copy left is at <c>.old</c>.</item>
    /// </list>
    /// A primary-then-<c>.bak</c> fallback finds nothing loadable at T3 — total loss.
    /// <see cref="LoadRun"/>'s <c>.old</c> fallback is exactly the copy <c>.old</c>
    /// exists to recover.
    /// </summary>
    [Fact]
    public void ACrashAtTheStartOfTheWriteAfterACorruptPrimaryRecoveryStillLoadsTheLatestState()
    {
        // T0: a real corrupt-primary-with-good-backup state.
        Write(SomeSaveJson());
        Write(SomeSaveJson());

        var intact = File.ReadAllText(SavePath);
        File.WriteAllText(SavePath, intact[..(intact.Length / 3)]);

        // T1: confirm the setup actually exercises a fallback load.
        var fallback = SaveFile.LoadRun(SavePath);
        Assert.NotNull(fallback.Saved);
        Assert.True(fallback.UsedBackup, "Setup must actually exercise the fallback-load path (T1).");

        // T2: the next write completes in full — the real SaveFile.Write, not a
        // simulated partial state, so it exercises the actual rotation that leaves
        // corrupt content in .bak.
        var t2Content = SomeSaveJson();
        Write(t2Content);

        Assert.Equal(t2Content, File.ReadAllText(SavePath));
        Assert.True(IsLoadableSaveJson(File.ReadAllText(BackupPath)), "Continuation preserves the valid fallback it selected until the new primary commits.");

        // T3: reproduce the disk state one rename into the *next* write — the good
        // primary from T2 moved aside to `.old`, nothing yet landed at `path`.
        File.Move(SavePath, OldPrimaryPath);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(loaded.Saved);
        Assert.True(loaded.UsedBackup);
        Assert.Null(loaded.PrimaryFailureReason);
        Assert.Equal(t2Content, ContentSerializer.Serialize(loaded.Saved!));

        var run = GauntletRun.Resume(Content, loaded.Saved!);
        Assert.Equal(RunOutcome.InProgress, run.Outcome);
    }

    /// <summary>
    /// Closes qc's finding 2 from the same review as a natural consequence of the
    /// <c>.old</c> fallback, rather than needing a separate fix: on an ordinary write
    /// over a *healthy* primary (no corruption anywhere in the sequence), a crash
    /// between the first two renames used to be recoverable only via <c>.bak</c> — one
    /// write older than the state that was actually live right up until the crash.
    /// <see cref="LoadRun"/>'s <c>.old</c> fallback finds that freshest state instead of
    /// silently rolling the run back a cycle.
    /// </summary>
    [Fact]
    public void ACrashBetweenTheFirstTwoRenamesOnAHealthyPrimaryLoadsTheFreshestStateNotTheOlderBackup()
    {
        // Distinct seeds so the two writes are byte-distinguishable on disk — two
        // default-seed SomeSaveJson() calls are byte-identical (the pregenerated party
        // and starting state are fully deterministic), which would let this test pass
        // whether or not LoadRun's .old fallback actually ran.
        var olderContent = SomeSaveJson(seed: 1);
        var currentContent = SomeSaveJson(seed: 2);
        Write(olderContent);
        Write(currentContent);

        Assert.Equal(currentContent, File.ReadAllText(SavePath));
        Assert.Equal(olderContent, File.ReadAllText(BackupPath));

        // Reproduce the disk state one rename into a third write: the healthy,
        // current primary has been moved aside to `.old`, nothing yet landed at
        // `path`.
        File.Move(SavePath, OldPrimaryPath);

        var loaded = SaveFile.LoadRun(SavePath);

        Assert.NotNull(loaded.Saved);
        Assert.True(loaded.UsedBackup);
        Assert.Equal(currentContent, ContentSerializer.Serialize(loaded.Saved!));

        var run = GauntletRun.Resume(Content, loaded.Saved!);
        Assert.Equal(RunOutcome.InProgress, run.Outcome);
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
        Write(SomeSaveJson());
        Write(SomeSaveJson());

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
        Write(SomeSaveJson());
        Write(SomeSaveJson());

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
        Write(SomeSaveJson());
        Write(SomeSaveJson());

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
        Write(SomeSaveJson());
        Write(SomeSaveJson());

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
        Write(SomeSaveJson());

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
        Write(ContentSerializer.Serialize(seedless));

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
        Write(SomeSaveJson());

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
        Write(SomeSaveJson());
        Write(SomeSaveJson());

        var truncated = File.ReadAllText(SavePath)[..5];
        File.WriteAllText(SavePath, truncated);
        File.WriteAllText(BackupPath, truncated);

        var loaded = SaveFile.LoadRun(SavePath);
        var message = SaveFile.DescribeUnloadable(SavePath, loaded);

        Assert.NotNull(message);
        Assert.Contains(SavePath, message, StringComparison.Ordinal);
        Assert.Contains(BackupPath, message, StringComparison.Ordinal);
    }

    [Fact]
    public void CrashPrefixesOfNewRunAndEveryContinuationSelectionPreserveTheCommittedState()
    {
        var foreign = Save(90);
        var current = Save(1);
        var next = Save(2);
        var afterNext = Save(3);

        AssertCrashMatrix(
            "new-run",
            path =>
            {
                File.WriteAllText(path, foreign);
                File.WriteAllText(path + ".old", foreign);
                File.WriteAllText(path + ".bak", foreign);
            },
            (path, json) => SaveFile.BeginNewRun(path, json),
            SaveFileOperation.NewRunCommitted,
            beforeCommit: null,
            committed: current,
            next: next,
            afterNext: afterNext);

        AssertCrashMatrix(
            "continuing-primary",
            path => EstablishPrimary(path, current),
            (path, json) => SaveFile.ContinueWrite(path, json),
            SaveFileOperation.ContinuationCommitted,
            beforeCommit: current,
            committed: next,
            next: afterNext,
            afterNext: Save(4));

        AssertCrashMatrix(
            "continuing-old-after-corrupt-primary",
            path => EstablishOldFallback(path, current),
            (path, json) => SaveFile.ContinueWrite(path, json),
            SaveFileOperation.ContinuationCommitted,
            beforeCommit: current,
            committed: next,
            next: afterNext,
            afterNext: Save(5));

        AssertCrashMatrix(
            "continuing-backup-after-corrupt-primary",
            path => EstablishBackupFallback(path, current),
            (path, json) => SaveFile.ContinueWrite(path, json),
            SaveFileOperation.ContinuationCommitted,
            beforeCommit: current,
            committed: next,
            next: afterNext,
            afterNext: Save(6));
    }

    [Fact]
    public void NewRunMarkerMasksResidueBeforeItIsRemovedAndContinuationRefusesIt()
    {
        var path = CasePath("marker");
        var foreign = Save(90);
        File.WriteAllText(path, foreign);
        File.WriteAllText(path + ".old", foreign);
        File.WriteAllText(path + ".bak", foreign);

        var crash = InjectAfter(1);
        try
        {
            Assert.Throws<InjectedCrash>(() => SaveFile.BeginNewRun(path, Save(1)));
        }
        finally
        {
            RemoveInjection();
        }

        // This assertion runs before fixture disposal: the injected crash left the real
        // state intact, including the durable marker and every foreign slot it masks.
        Assert.True(File.Exists(path + ".new"));
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".old"));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Null(SaveFile.LoadRun(path).Saved);
        Assert.Throws<InvalidOperationException>(() => SaveFile.ContinueWrite(path, Save(2)));
    }

    [Fact]
    public void ExistingUnmarkedSaveStillLoadsAndAContinuationKeepsItsBackup()
    {
        var path = CasePath("legacy");
        var first = Save(1);
        var second = Save(2);
        SaveFile.BeginNewRun(path, first);
        SaveFile.ContinueWrite(path, second);

        Assert.Equal(second, LoadedJson(path));
        File.Delete(path);
        Assert.Equal(first, LoadedJson(path));
    }

    private void AssertCrashMatrix(
        string name,
        Action<string> setup,
        Action<string, string> write,
        SaveFileOperation commit,
        string? beforeCommit,
        string committed,
        string next,
        string afterNext)
    {
        var template = CasePath(name + "-count");
        setup(template);
        var operations = Capture(() => write(template, committed));
        var commitPrefix = operations.IndexOf(commit) + 1;
        Assert.True(commitPrefix > 0, $"{name} must expose its commit checkpoint.");

        for (var prefix = 1; prefix <= operations.Count; prefix++)
        {
            var source = CasePath($"{name}-source-{prefix}");
            setup(source);
            CrashAfter(prefix, () => write(source, committed));
            var recovered = prefix < commitPrefix ? beforeCommit : committed;
            AssertRecovered(source, recovered);

            for (var nextPrefix = 1; ; nextPrefix++)
            {
                var crossProduct = CasePath($"{name}-source-{prefix}-next-{nextPrefix}");
                setup(crossProduct);
                CrashAfter(prefix, () => write(crossProduct, committed));
                var recoveredForNext = prefix < commitPrefix ? beforeCommit : committed;

                // A pre-commit new-run crash correctly has no run to continue. Recovery
                // starts that new run again to a real commit; only then can its next save
                // be a continuation write.
                if (recoveredForNext is null)
                {
                    SaveFile.BeginNewRun(crossProduct, committed);
                    recoveredForNext = committed;
                }

                var countPath = CasePath($"{name}-source-{prefix}-next-count-{nextPrefix}");
                setup(countPath);
                CrashAfter(prefix, () => write(countPath, committed));
                if (prefix < commitPrefix && beforeCommit is null)
                {
                    SaveFile.BeginNewRun(countPath, committed);
                }

                var nextOperations = Capture(() => SaveFile.ContinueWrite(countPath, next));
                if (nextPrefix > nextOperations.Count)
                {
                    break;
                }

                CrashAfter(nextPrefix, () => SaveFile.ContinueWrite(crossProduct, next));
                var nextCommitPrefix = nextOperations.IndexOf(SaveFileOperation.ContinuationCommitted) + 1;
                AssertRecovered(crossProduct, nextPrefix < nextCommitPrefix ? recoveredForNext : next);
            }
        }

        var completed = CasePath(name + "-completed");
        setup(completed);
        write(completed, committed);
        SaveFile.ContinueWrite(completed, next);
        SaveFile.ContinueWrite(completed, afterNext);
        Assert.Equal(afterNext, LoadedJson(completed));
    }

    private string CasePath(string name)
    {
        var directory = Path.Combine(_directory, name);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "save.json");
    }

    private static void EstablishPrimary(string path, string current)
    {
        SaveFile.BeginNewRun(path, Save(10));
        SaveFile.ContinueWrite(path, current);
    }

    private static void EstablishOldFallback(string path, string current)
    {
        EstablishPrimary(path, current);
        File.Move(path, path + ".old", overwrite: true);
        File.WriteAllText(path, "corrupt primary");
    }

    private static void EstablishBackupFallback(string path, string current)
    {
        EstablishPrimary(path, current);
        File.Copy(path, path + ".bak", overwrite: true);
        File.Delete(path);
        if (File.Exists(path + ".old"))
        {
            File.Delete(path + ".old");
        }
    }

    private static List<SaveFileOperation> Capture(Action action)
    {
        var operations = new List<SaveFileOperation>();
        SaveFile.OperationCompletedForTesting = operations.Add;
        try
        {
            action();
            return operations;
        }
        finally
        {
            RemoveInjection();
        }
    }

    private static void CrashAfter(int prefix, Action action)
    {
        InjectAfter(prefix);
        try
        {
            Assert.Throws<InjectedCrash>(action);
        }
        finally
        {
            // Restore only the hook. Deliberately do not remove any files: assertions
            // must inspect the exact filesystem state the injected crash left behind.
            RemoveInjection();
        }
    }

    private static int InjectAfter(int prefix)
    {
        var seen = 0;
        SaveFile.OperationCompletedForTesting = _ =>
        {
            seen++;
            if (seen == prefix)
            {
                throw new InjectedCrash();
            }
        };
        return prefix;
    }

    private static void RemoveInjection() => SaveFile.OperationCompletedForTesting = null;

    private static void AssertRecovered(string path, string? expected)
    {
        var loaded = SaveFile.LoadRun(path);
        if (expected is null)
        {
            Assert.Null(loaded.Saved);
            return;
        }

        Assert.NotNull(loaded.Saved);
        Assert.Equal(expected, ContentSerializer.Serialize(loaded.Saved!));
    }

    private static string LoadedJson(string path)
    {
        var loaded = SaveFile.LoadRun(path);
        Assert.NotNull(loaded.Saved);
        return ContentSerializer.Serialize(loaded.Saved!);
    }

    private static string Save(int seed) => RunSave.ToJson(GauntletRun.Start(Content, seed: seed));

    private sealed class InjectedCrash : Exception;
}