using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IAnimeMatchingService
{
    Task<Result> MatchAsync(bool forceRefreshKometaMapping = false, ISyncProgressNotifier? progressNotifier = null);
}
