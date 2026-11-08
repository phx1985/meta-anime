using MetaAnime.Core.Sync;

namespace MetaAnime.Core.Interfaces.Services;

/// <summary>
/// Tracks the in-progress sync state in memory so that clients reconnecting mid-sync
/// can receive a snapshot of the current step states via SignalR on connect.
/// </summary>
public interface ISyncStateTracker
{
    /// <summary>Marks the start of a new sync run and clears any previous state.</summary>
    void BeginSync();

    /// <summary>Records the latest state of a single step during an active sync.</summary>
    void UpdateStep(SyncProgressUpdate update);

    /// <summary>Marks the sync run as finished (success or terminal error).</summary>
    void EndSync(bool success = true);

    /// <summary>True while a metadata sync is executing.</summary>
    bool IsFullSyncRunning { get; }

    /// <summary>True while the activity-stats refresh job is executing.</summary>
    bool IsActivitySyncRunning { get; }

    /// <summary>When the last metadata sync finished successfully.</summary>
    DateTimeOffset? LastSyncDate { get; }

    /// <summary>When the activity-stats refresh last finished (any outcome).</summary>
    DateTimeOffset? LastActivitySyncDate { get; }

    /// <summary>Flags the activity refresh as running/finished and stamps its completion time.</summary>
    void SetActivitySyncRunning(bool isRunning, bool succeeded = true);

    /// <summary>
    /// Returns the snapshot of all recorded step states while a sync is active.
    /// Returns an empty array when no sync is in progress.
    /// </summary>
    SyncProgressUpdate[] GetActiveState();
}
