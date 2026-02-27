using MetaAnime.Core.Sync;

namespace MetaAnime.Core.Interfaces.Services;

public interface ISyncProgressNotifier
{
    Task NotifyAsync(SyncProgressUpdate update);
}
