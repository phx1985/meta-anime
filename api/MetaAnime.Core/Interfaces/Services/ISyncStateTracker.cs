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
    void EndSync();

    /// <summary>
    /// Returns the snapshot of all recorded step states while a sync is active.
    /// Returns an empty array when no sync is in progress.
    /// </summary>
    SyncProgressUpdate[] GetActiveState();
}
