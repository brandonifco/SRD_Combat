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
/// <para>
/// <b>The write is temp-then-rename, never in place.</b> The new content lands fully in
/// a <c>.tmp</c> file in the same directory as the target (so the rename that follows is
/// a same-volume, effectively atomic operation, not a cross-device copy), is flushed to
/// disk, and only then replaces the target. A crash at any point before the rename
/// leaves the old complete file exactly as it was; a crash after leaves the new complete
/// file. There is no window where <c>path</c> holds a partial write.
/// </para>
/// <para>
/// <b>One rolling backup survives every successful write.</b> The file that <c>path</c>
/// pointed to before the write becomes <c>path</c> + <c>.bak</c>, overwriting whatever
/// backup was there — but not via <see cref="File.Replace(string,string,string,bool)"/>,
/// whose Unix implementation is <c>unlink(bak)</c>, then <c>link(path, bak)</c>, then
/// <c>rename(tmp, path)</c>: a crash between the first two steps discards the existing
/// <c>.bak</c> before the new primary has landed. Instead the rotation is three separate
/// renames, each one a single atomic filesystem operation: the current primary moves to
/// <c>path</c> + <c>.old</c>, the new content moves from <c>.tmp</c> into <c>path</c>,
/// and only then does <c>.old</c> move into <c>path</c> + <c>.bak</c>.
/// </para>
/// <para>
/// <b>The invariant this keeps</b> is not "<c>.bak</c> is never touched before the new
/// primary lands" — <c>path</c> + <c>.old</c> is a real gap, and the content it holds is
/// whatever <c>path</c> held going in, which the caller may already know is corrupt (a
/// fallback load's next write — #367). The invariant is: <b>at every instant, including
/// between any two filesystem operations of either branch, a loadable copy of the most
/// recent successfully completed write exists among the locations <see cref="LoadRun"/>
/// consults, and no copy belonging to a different deleted or prior run can be loaded as
/// current state</b>. The crash-prefix tests enumerate both branches and repeat the
/// sequence after recovery. <see cref="LoadRun"/> checks all three locations in order to
/// honour it — not just the two a caller sees in <see cref="SaveLoadResult"/>. Before the
/// first move, the untouched old <c>path</c> is still there. Between the first and
/// second, <c>path</c> is briefly missing, but <c>.old</c> now holds exactly what
/// <c>path</c> held a moment ago — if that was good, <c>LoadRun</c> finds it there first,
/// fresher than whatever stale <c>.bak</c> a moment ago's write may have left behind
/// (this is what closes the case a plain two-copy fallback cannot: a first write over a
/// corrupt primary leaves the *new* primary healthy but rotates the corrupt content into
/// <c>.bak</c>, and a second write's first rename can then be interrupted with nothing
/// good at <c>path</c> or <c>.bak</c> — only at <c>.old</c>). From the second move on, the
/// new primary is live at <c>path</c> and nothing after that point — including what the
/// third rename does to <c>.bak</c> — can lose it. The very first write for a path has
/// nothing to back up, so it is a plain move instead — and any <c>.bak</c> already sitting
/// there (left over from a run whose primary was deleted separately from its backup) is
/// deleted after the move lands the new primary — never before, so a crash between the
/// two steps still leaves a loadable file — and a backup can then never predate the
/// primary beside it and get resurrected as that primary's history.
/// </para>
/// <para>
/// <b>Loading falls back past the primary</b> to <c>.old</c> and then <c>.bak</c> when the
/// primary is missing or fails to parse — a corrupt or truncated primary is not treated as
/// "no save", because the whole point of keeping a backup is to survive exactly that.
/// <c>.old</c> only ever appears on disk because some earlier write crashed mid-rotation,
/// so checking it costs nothing on the (overwhelmingly common) path where nothing ever
/// has — but it does not vanish the moment that crash passes; a leftover <c>.old</c> can
/// outlive many later writes (the fresh-path branch below never clears one — #394), so
/// this fallback keeps mattering for as long as the file survives, not just in the
/// instant right after a crash.
/// </para>
/// </remarks>
public static class SaveFile
{
    private static string TempPathFor(string path) => path + ".tmp";

    private static string BackupPathFor(string path) => path + ".bak";

    private static string OldPrimaryPathFor(string path) => path + ".old";

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
            // Three separate atomic renames rather than File.Replace — see the class
            // remarks for the full invariant (#367). This unconditionally retires
            // whatever was at `path` into `.bak`, corrupt or not — it does not need to
            // know which, because LoadRun's .old fallback is what actually protects
            // the crash window this rotation opens, not a promise that .bak is always
            // good content.
            var oldPrimaryPath = OldPrimaryPathFor(path);

            File.Move(path, oldPrimaryPath, overwrite: true);
            File.Move(tempPath, path, overwrite: true);
            File.Move(oldPrimaryPath, BackupPathFor(path), overwrite: true);
        }
        else
        {
            // Nothing to back up on a first write for this path — but a stale .bak can
            // already be sitting here, left over from a run whose primary was deleted
            // separately from its backup. Left untouched, that stale backup would
            // predate the new primary being created right now, and LoadRun's fallback
            // could resurrect it as this run's history if the new primary were ever
            // lost. Clear it — but only AFTER the move lands the new primary. The
            // reverse order (delete, then move) opens a window with no loadable file
            // at all, and that window is live: a resume-from-backup whose primary is
            // missing takes this branch on its very first write, so deleting first
            // would destroy the one copy the run was just loaded from. Deleting after
            // keeps "at least one complete file is on disk for LoadRun to find" true
            // at every crash point; the stale-resurrection hazard only re-opens if the
            // just-written primary is itself lost before the delete below runs — a
            // strictly rarer compound than a no-loadable-file window.
            var backupPath = BackupPathFor(path);

            File.Move(tempPath, path);

            // A missing primary can be a genuinely fresh path, or a path whose prior
            // run was deleted separately from its crash residue. Once the new primary
            // is live, neither residue may remain loadable as this run's history. The
            // primary is already the newest complete copy, so these cleanup operations
            // cannot create a no-copy window: a crash before either cleanup still lets
            // LoadRun choose the new primary first.
            var oldPrimaryPath = OldPrimaryPathFor(path);
            if (File.Exists(oldPrimaryPath))
            {
                File.Delete(oldPrimaryPath);
            }

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
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
