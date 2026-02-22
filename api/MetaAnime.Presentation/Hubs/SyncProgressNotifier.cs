using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Sync;
using MetaAnime.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MetaAnime.Presentation.Hubs;

/// <summary>
/// Sends <see cref="SyncProgressUpdate"/> messages to all SignalR clients.
/// </summary>
public class SyncProgressNotifier(IHubContext<SyncHub> hubContext) : ISyncProgressNotifier
{
    public Task NotifyAsync(SyncProgressUpdate update) =>
        hubContext.Clients.All.SendAsync("SyncProgress", update);
}
