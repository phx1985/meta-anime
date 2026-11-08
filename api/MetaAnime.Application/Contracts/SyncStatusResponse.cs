namespace MetaAnime.Application.Contracts;

/// <summary>
/// Snapshot of the sync engine, served by <c>GET /api/v1/settings/sync-status</c>.
/// The running flags come from the in-memory tracker so a page refresh can restore the
/// exact UI state while a job is still executing.
/// </summary>
public record SyncStatusResponse(
    DateTimeOffset? LastSyncDate,
    bool IsSyncRunning = false,
    bool IsActivitySyncRunning = false,
    DateTimeOffset? LastActivitySyncDate = null);
