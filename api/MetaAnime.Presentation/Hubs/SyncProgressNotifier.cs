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
}
