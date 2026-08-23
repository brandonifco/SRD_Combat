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
/// True when the primary was missing or failed to parse and the <c>.bak</c> was read
/// instead — the caller should tell the player this happened.
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
/// <para>
/// <b>The write is temp-then-rename, never in place.</b> The new content lands fully in
/// a <c>.tmp</c> file in the same directory as the target (so the rename that follows is
/// a same-volume, effectively atomic operation, not a cross-device copy), is flushed to
/// disk, and only then replaces the target. A crash at any point before the rename
/// leaves the old complete file exactly as it was; a crash after leaves the new complete
/// file. There is no window where <c>path</c> holds a partial write.
/// </para>
/// <para>
/// <b>One rolling backup survives every successful write.</b> <see cref="File.Replace(string,string,string,bool)"/>
/// performs the rename and the backup rotation together: the file that <c>path</c>
/// pointed to before the write becomes <c>path</c> + <c>.bak</c>, overwriting whatever
/// backup was there. This is not one atomic filesystem operation on Unix — .NET's
/// implementation is <c>unlink(bak)</c>, then <c>link(path, bak)</c>, then
/// <c>rename(tmp, path)</c> — so a crash between the first two steps really does leave a
/// moment with no <c>.bak</c> on disk. <b>The invariant that actually holds is weaker but
/// sufficient</b>: at every point in that sequence, at least one complete file — the
/// untouched old <c>path</c> (before its rename), the fresh <c>.bak</c>, or the new
/// <c>path</c> (after its rename) — is on disk for <see cref="LoadRun"/> to find. The
/// very first write for a path has nothing to back up, so it is a plain move instead
/// — and any <c>.bak</c> already sitting there (left over from a run whose primary
/// was deleted separately from its backup) is deleted first, so a backup can never
/// predate the primary beside it and get resurrected as that primary's history.
/// </para>
/// <para>
/// <b>Loading falls back to the backup</b> when the primary is missing or fails to parse
/// — a corrupt or truncated primary is not treated as "no save", because the whole point
/// of keeping a backup is to survive exactly that.
/// </para>
/// </remarks>
public static class SaveFile
{
    private static string TempPathFor(string path) => path + ".tmp";

    private static string BackupPathFor(string path) => path + ".bak";

    /// <summary>
    /// Writes <paramref name="json"/> to <paramref name="path"/> atomically, keeping the
    /// file <paramref name="path"/> previously held as exactly one <c>.bak</c> alongside
    /// it. On a first write for <paramref name="path"/> — no primary yet on disk — any
    /// pre-existing <c>.bak</c> is deleted rather than left behind, so it can never
    /// predate the primary this write creates.
    /// </summary>
    public static void Write(string path, string json)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(json);

        var tempPath = TempPathFor(path);

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();

            // Forces the OS to commit the bytes to disk rather than leaving them in a
            // buffer a crash could lose before the rename below makes them visible.
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            // One call does both jobs: path becomes tempPath's content, and whatever
            // path held becomes the backup. On Unix this is unlink(bak), link(path,
            // bak), rename(tmp, path) under the hood, not one syscall — a crash between
            // the first two steps leaves no .bak on disk for a moment. What holds at
            // every point regardless is weaker but enough: the untouched old path, the
            // fresh backup, or the fresh path is always there for LoadRun to find.
            File.Replace(tempPath, path, BackupPathFor(path), ignoreMetadataErrors: true);
        }
        else
        {
            // Nothing to back up on a first write for this path — but a stale .bak can
            // already be sitting here, left over from a run whose primary was deleted
            // separately from its backup. Left untouched, that stale backup would
            // predate the new primary being created right now, and LoadRun's fallback
            // could resurrect it as this run's history if the new primary were ever
            // lost. Clear it first so a backup can never predate the primary beside it.
            var backupPath = BackupPathFor(path);

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(tempPath, path);
        }
    }

    /// <summary>
    /// Reads the save at <paramref name="path"/>, falling back to its <c>.bak</c> if the
    /// primary is missing or unreadable. Never throws; a failure to read either copy
    /// comes back as a <c>null</c> <see cref="SaveLoadResult.Saved"/>, with both
    /// failure reasons reported — a corrupt backup does not hide behind a missing
    /// primary.
    /// </summary>
    public static SaveLoadResult LoadRun(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (TryReadRun(path, out var primary, out var primaryFailure))
        {
            return new SaveLoadResult(primary, UsedBackup: false, PrimaryFailureReason: null, BackupFailureReason: null);
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
