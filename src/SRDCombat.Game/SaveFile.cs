using System.Text.Json;

namespace SRDCombat.Game;

/// <summary>
/// What <see cref="SaveFile.LoadRun"/> found: the save if either copy could be read, and
/// where it came from.
/// </summary>
/// <param name="Saved">
/// The parsed save, or <c>null</c> if neither the primary file nor its backup could be
/// read.
/// </param>
/// <param name="UsedBackup">
/// True when the primary was missing or failed to parse and a fallback copy was read
/// instead — either the rolling <c>.bak</c>, or the <c>.old</c> a write leaves behind
/// when it crashes mid-rotation (see <see cref="SaveFile"/>'s remarks) — the caller
/// should tell the player this happened.
/// </param>
/// <param name="PrimaryFailureReason">
/// Why the primary was rejected, when it existed but did not parse. <c>null</c> when the
/// primary loaded fine, or when it was simply absent (a fresh path, not a torn write).
/// </param>
/// <param name="BackupFailureReason">
/// Why the backup was rejected, when it existed but did not parse. Only ever set when
/// <see cref="Saved"/> is <c>null</c> — a readable backup is used, not reported as a
/// failure. <c>null</c> when the backup was simply absent.
/// </param>
public sealed record SaveLoadResult(
    SavedRun? Saved,
    bool UsedBackup,
    string? PrimaryFailureReason,
    string? BackupFailureReason);

/// <summary>
/// Gets a run's save to and from disk without ever leaving a torn file behind.
/// <see cref="RunSave"/> owns the JSON shape; this owns getting bytes onto and off the
/// disk safely, and is the one path both clients call — there is no second writer.
/// </summary>
/// <remarks>
/// <b>Commit</b> is the successful same-directory rename of a fully written and flushed
/// staging file into <paramref name="path"/>. It is not method return: from that rename
/// onward the new primary is authoritative and cleanup cannot change <see cref="LoadRun"/>.
/// <see cref="BeginNewRun"/> first creates a durable, distinct <c>.new</c> marker/staging
/// file; while it exists loading returns no save and ignores all old slots.
/// <see cref="ContinueWrite"/> refuses that marker and preserves the exact valid primary,
/// <c>.old</c>, or <c>.bak</c> it selected until commit.
/// </remarks>
public static class SaveFile
{
    private static string TempPathFor(string path) => path + ".tmp";

    private static string NewRunPathFor(string path) => path + ".new";

    private static string BackupPathFor(string path) => path + ".bak";

    private static string OldPrimaryPathFor(string path) => path + ".old";

    /// <summary>Begins a distinct run, masking and removing every old save slot before
    /// its staging file commits into the primary.</summary>
    public static void BeginNewRun(string path, string json)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(json);

        var newRunPath = NewRunPathFor(path);
        WriteAndFlush(newRunPath, "new run pending");
        OnOperationCompleted(SaveFileOperation.NewRunMarkerCreated);
        WriteAndFlush(newRunPath, json);
        OnOperationCompleted(SaveFileOperation.NewRunStagingWritten);
        DeleteIfPresent(path, SaveFileOperation.NewRunPrimaryRemoved);
        DeleteIfPresent(OldPrimaryPathFor(path), SaveFileOperation.NewRunOldRemoved);
        DeleteIfPresent(BackupPathFor(path), SaveFileOperation.NewRunBackupRemoved);
        File.Move(newRunPath, path, overwrite: true);
        OnOperationCompleted(SaveFileOperation.NewRunCommitted);
    }

    /// <summary>Writes the next state of an existing run without overwriting its selected
    /// valid copy before the new primary commits.</summary>
    public static void ContinueWrite(string path, string json)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(json);

        if (File.Exists(NewRunPathFor(path)))
        {
            throw new InvalidOperationException($"Cannot continue '{path}' while a new run is incomplete.");
        }

        var selected = SelectedCopy(path) ?? throw new InvalidOperationException(
            $"Cannot continue '{path}' because it has no loadable committed save.");
        var tempPath = TempPathFor(path);
        WriteAndFlush(tempPath, json);
        OnOperationCompleted(SaveFileOperation.ContinuationStagingWritten);

        var oldPrimaryPath = OldPrimaryPathFor(path);
        if (selected == SaveCopy.Primary)
        {
            File.Move(path, oldPrimaryPath, overwrite: true);
            OnOperationCompleted(SaveFileOperation.ContinuationPrimaryMovedAside);
        }

        File.Move(tempPath, path, overwrite: true);
        OnOperationCompleted(SaveFileOperation.ContinuationCommitted);

        if (selected is SaveCopy.Primary or SaveCopy.Old)
        {
            File.Move(oldPrimaryPath, BackupPathFor(path), overwrite: true);
            OnOperationCompleted(SaveFileOperation.ContinuationPriorMovedToBackup);
        }
    }

    /// <summary>
    /// Reads the save at <paramref name="path"/>, falling back first to <c>.old</c> and
    /// then to <c>.bak</c> if the primary is missing or unreadable. Never throws; a
    /// failure to read every copy comes back as a <c>null</c> <see cref="SaveLoadResult.Saved"/>,
    /// with both public failure reasons reported — a corrupt backup does not hide behind
    /// a missing primary.
    /// </summary>
    /// <remarks>
    /// <c>.old</c> is checked silently, ahead of <c>.bak</c>: it only ever appears on disk
    /// because some earlier write crashed partway through its rotation (see the class
    /// remarks) — it does not necessarily mean *this* load is racing a crash, since a
    /// leftover <c>.old</c> can survive many writes afterward (#394). Whenever it is
    /// there, it holds whatever <c>path</c> held immediately before the write that left
    /// it started — always at least as fresh as <c>.bak</c>, and sometimes the only
    /// loadable copy left. Its
    /// own failure (if it exists but does not parse) is not surfaced through
    /// <see cref="SaveLoadResult"/> the way the primary's and backup's are: it is a
    /// crash-recovery implementation detail, not a copy either client shows the player.
    /// </remarks>
    public static SaveLoadResult LoadRun(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (File.Exists(NewRunPathFor(path)))
        {
            return new SaveLoadResult(null, UsedBackup: false, PrimaryFailureReason: null, BackupFailureReason: null);
        }

        if (TryReadRun(path, out var primary, out var primaryFailure))
        {
            return new SaveLoadResult(primary, UsedBackup: false, PrimaryFailureReason: null, BackupFailureReason: null);
        }

        if (TryReadRun(OldPrimaryPathFor(path), out var old, out _))
        {
            return new SaveLoadResult(old, UsedBackup: true, PrimaryFailureReason: primaryFailure, BackupFailureReason: null);
        }

        if (TryReadRun(BackupPathFor(path), out var backup, out var backupFailure))
        {
            return new SaveLoadResult(backup, UsedBackup: true, PrimaryFailureReason: primaryFailure, BackupFailureReason: null);
        }

        return new SaveLoadResult(null, UsedBackup: false, PrimaryFailureReason: primaryFailure, BackupFailureReason: backupFailure);
    }

    /// <summary>
    /// A one-line, player-facing explanation of why <see cref="LoadRun"/> came back with
    /// nothing to resume, or <c>null</c> when there is nothing to explain — neither file
    /// ever existed, so the caller should show its own "start a new run" message instead
    /// (the two clients' flag syntax differs — <c>--save &lt;path&gt;</c> versus
    /// <c>--save=&lt;path&gt;</c> — so that hint stays client-owned). Shared so a torn
    /// primary and a corrupt backup are worded identically by both, rather than drifting
    /// into two phrasings — and so a corrupt backup is never left unmentioned just
    /// because the primary happened to be missing rather than corrupt.
    /// </summary>
    /// <param name="path">The primary path <see cref="LoadRun"/> was given.</param>
    /// <param name="result">A result whose <see cref="SaveLoadResult.Saved"/> is <c>null</c>.</param>
    public static string? DescribeUnloadable(string path, SaveLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(result);

        if (result.PrimaryFailureReason is null && result.BackupFailureReason is null)
        {
            // Neither file exists — a fresh path, not a torn write.
            return null;
        }

        var primaryPart = result.PrimaryFailureReason is { } primaryReason
            ? $"'{path}': {primaryReason}"
            : $"'{path}' is missing";

        var backupPart = result.BackupFailureReason is { } backupReason
            ? $"'{BackupPathFor(path)}': {backupReason}"
            : $"'{BackupPathFor(path)}' is missing";

        return $"Cannot load a save. Primary {primaryPart}. Backup {backupPart}.";
    }

    private enum SaveCopy
    {
        Primary,
        Old,
        Backup,
    }

    private static SaveCopy? SelectedCopy(string path)
    {
        if (TryReadRun(path, out _, out _))
        {
            return SaveCopy.Primary;
        }

        if (TryReadRun(OldPrimaryPathFor(path), out _, out _))
        {
            return SaveCopy.Old;
        }

        return TryReadRun(BackupPathFor(path), out _, out _) ? SaveCopy.Backup : null;
    }

    private static void WriteAndFlush(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(contents);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteIfPresent(string path, SaveFileOperation operation)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
        OnOperationCompleted(operation);
    }

    internal static Action<SaveFileOperation>? OperationCompletedForTesting { get; set; }

    private static void OnOperationCompleted(SaveFileOperation operation) =>
        OperationCompletedForTesting?.Invoke(operation);

    private static bool TryReadRun(string path, out SavedRun? saved, out string? failureReason)
    {
        saved = null;
        failureReason = null;

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            saved = RunSave.FromJson(File.ReadAllText(path));
            return true;
        }
        catch (Exception failure) when (failure is JsonException or InvalidDataException or IOException)
        {
            failureReason = failure.Message;
            return false;
        }
    }
}
