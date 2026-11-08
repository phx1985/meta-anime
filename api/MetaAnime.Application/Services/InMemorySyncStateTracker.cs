using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Sync;

namespace MetaAnime.Application.Services;

/// <summary>
/// In-memory singleton that holds the real-time sync state for the duration of a single sync run.
/// Allows newly connected (or reconnected) SignalR clients to receive the current step snapshot.
/// Thread-safe via <see cref="Lock"/>.
/// </summary>
public sealed class InMemorySyncStateTracker : ISyncStateTracker
{
    private readonly Dictionary<string, SyncProgressUpdate> _steps = new();
    private bool _active;
    private bool _activityRunning;
    private DateTimeOffset? _lastSyncDate;
    private DateTimeOffset? _lastActivitySyncDate;
    private readonly Lock _lock = new();

    public bool IsFullSyncRunning { get { lock (_lock) return _active; } }

    public bool IsActivitySyncRunning { get { lock (_lock) return _activityRunning; } }

    public DateTimeOffset? LastSyncDate { get { lock (_lock) return _lastSyncDate; } }

    public DateTimeOffset? LastActivitySyncDate { get { lock (_lock) return _lastActivitySyncDate; } }

    public void BeginSync()
    {
        lock (_lock)
        {
            _steps.Clear();
            _active = true;
        }
    }

    public void UpdateStep(SyncProgressUpdate update)
    {
        lock (_lock)
        {
            if (_active) _steps[update.StepKey] = update;
        }
    }

    public void EndSync(bool success = true)
    {
        lock (_lock)
        {
            _active = false;
            if (success) _lastSyncDate = DateTimeOffset.UtcNow;
        }
    }

    public void SetActivitySyncRunning(bool isRunning, bool succeeded = true)
    {
        lock (_lock)
        {
            _activityRunning = isRunning;
            if (!isRunning) _lastActivitySyncDate = DateTimeOffset.UtcNow;
        }
    }

    public SyncProgressUpdate[] GetActiveState()
    {
        lock (_lock)
        {
            return _active ? [.. _steps.Values] : [];
        }
    }
}
