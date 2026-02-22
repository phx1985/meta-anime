using Microsoft.AspNetCore.SignalR;

namespace MetaAnime.Hubs;

/// <summary>
/// SignalR hub that broadcasts real-time sync progress to connected clients.
/// Anonymous access — no sensitive data is pushed through this hub.
/// </summary>
public class SyncHub : Hub { }
