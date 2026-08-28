namespace SRDCombat.Game;

internal enum SaveFileOperation
{
    NewRunMarkerCreated,
    NewRunStagingWritten,
    NewRunPrimaryRemoved,
    NewRunOldRemoved,
    NewRunBackupRemoved,
    NewRunCommitted,
    ContinuationStagingWritten,
    ContinuationPrimaryMovedAside,
    ContinuationCommitted,
    ContinuationPriorMovedToBackup,
}
