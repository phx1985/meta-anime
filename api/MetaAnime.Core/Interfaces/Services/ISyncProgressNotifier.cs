using MetaAnime.Core.Sync;

namespace MetaAnime.Core.Interfaces.Services;

public interface ISyncProgressNotifier
{
    Task NotifyAsync(SyncProgressUpdate update);

    /// <summary>Broadcasts a terminal sync failure so clients can raise a notification.</summary>
    Task NotifySyncFailedAsync(string message);

    /// <summary>Marks the activity refresh as running and tells clients.</summary>
    Task NotifyActivitySyncStartedAsync();

    /// <summary>Clears the activity refresh flag and reports the outcome.</summary>
    Task NotifyActivitySyncFinishedAsync(bool succeeded);
}
