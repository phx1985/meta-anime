using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services
{
    public interface IAnimeMatchingService
    {
        public Task<Result> MatchAsync(bool forceRefreshKometaMapping = false);
    }
}
