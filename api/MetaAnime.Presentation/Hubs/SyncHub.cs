using MetaAnime.Core.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;

namespace MetaAnime.Hubs;

/// <summary>
/// SignalR hub that broadcasts real-time sync progress to connected clients.
/// On connection, replays the current sync snapshot so that late joiners and
/// page-refresh clients immediately see the in-progress step states.
/// Anonymous access — no sensitive data is pushed through this hub.
/// </summary>
public sealed class SyncHub(ISyncStateTracker stateTracker) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var snapshot = stateTracker.GetActiveState();
        foreach (var update in snapshot)
            await Clients.Caller.SendAsync("SyncProgress", update);

        await base.OnConnectedAsync();
    }
}
