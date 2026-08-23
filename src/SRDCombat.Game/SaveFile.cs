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
public sealed record SaveLoadResult(SavedRun? Saved, bool UsedBackup, string? PrimaryFailureReason);

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
/// performs the rename and the backup rotation as a single operation: the file that
/// <c>path</c> pointed to before the write becomes <c>path</c> + <c>.bak</c>, overwriting
/// whatever backup was there. The very first write for a path has nothing to back up, so
/// it is a plain move instead.
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
    /// it.
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
            // path held becomes the backup. There is no gap between them for a crash to
            // land in.
            File.Replace(tempPath, path, BackupPathFor(path), ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    /// <summary>
    /// Reads the save at <paramref name="path"/>, falling back to its <c>.bak</c> if the
    /// primary is missing or unreadable. Never throws; a failure to read either copy
    /// comes back as a <c>null</c> <see cref="SaveLoadResult.Saved"/>.
    /// </summary>
    public static SaveLoadResult LoadRun(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (TryReadRun(path, out var primary, out var primaryFailure))
        {
            return new SaveLoadResult(primary, UsedBackup: false, PrimaryFailureReason: null);
        }

        if (TryReadRun(BackupPathFor(path), out var backup, out _))
        {
            return new SaveLoadResult(backup, UsedBackup: true, PrimaryFailureReason: primaryFailure);
        }

        return new SaveLoadResult(null, UsedBackup: false, PrimaryFailureReason: primaryFailure);
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
