using MetaAnime.Application.DTOs;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Interfaces
{
    public interface IAnimeMediatorService
    {
        Task<Result<IEnumerable<GetAnimeDto>>> GetAllAsync();
        Task<Result> SyncAllAsync(bool forceRefreshKometaMapping = false);
    }
}
