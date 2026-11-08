using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Sync;
using MetaAnime.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MetaAnime.Presentation.Hubs;

/// <summary>
/// Sends <see cref="SyncProgressUpdate"/> messages to all SignalR clients and records
/// each update in <see cref="ISyncStateTracker"/> so that reconnecting clients can
/// receive the current sync snapshot via <see cref="SyncHub.OnConnectedAsync"/>.
/// </summary>
public sealed class SyncProgressNotifier(
    IHubContext<SyncHub> hubContext,
    ISyncStateTracker stateTracker) : ISyncProgressNotifier
{
    public Task NotifyAsync(SyncProgressUpdate update)
    {
        stateTracker.UpdateStep(update);
        return hubContext.Clients.All.SendAsync("SyncProgress", update);
    }

    public Task NotifySyncFailedAsync(string message) =>
        hubContext.Clients.All.SendAsync("syncError", new { message, failedAt = DateTimeOffset.UtcNow });

    public Task NotifyActivitySyncStartedAsync()
    {
        stateTracker.SetActivitySyncRunning(true);
        return hubContext.Clients.All.SendAsync("activitySyncStarted", new { startedAt = DateTimeOffset.UtcNow });
    }

    public Task NotifyActivitySyncFinishedAsync(bool succeeded)
    {
        stateTracker.SetActivitySyncRunning(false, succeeded);
        return hubContext.Clients.All.SendAsync(
            succeeded ? "activitySyncDone" : "activitySyncFailed",
            new { completedAt = DateTimeOffset.UtcNow, errorCode = succeeded ? null : "ERR_ACTIVITY_SYNC_FAILED" });
    }
}
