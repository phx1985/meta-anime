using MetaAnime.Application.Contracts;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Interfaces
{
    public interface IAnimeMediatorService
    {
        Task<Result<IEnumerable<AnimeDetailsResponse>>> GetAllAsync(int page = 1, int pageSize = 100);
        Task<Result<AnimeDetailsResponse?>> GetByIdAsync(string aniDbId);
        Task<Result<AnimeResponse>> CreateAsync(CreateAnimeRequest request);
        Task<Result<AnimeResponse>> UpdateAsync(Guid id, UpdateAnimeRequest request);
        Task<Result> DeleteAsync(Guid id);
        Task<Result> SyncAllAsync(bool forceRefreshKometaMapping = false);
    }
}
