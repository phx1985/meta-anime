using MetaAnime.Application.DTOs;
using MetaAnime.Core.Enums;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Interfaces
{
    public interface IAnimeMediatorService
    {
        Task<Result<IEnumerable<GetAnimeDto>>> GetAllAsync(int page = 1, int pageSize = 100);
        Task<Result<GetAnimeDto?>> GetByIdAsync(string aniDbId);
        Task<Result<AnimeDto>> CreateAsync(CreateAnimeRequest request);
        Task<Result<AnimeDto>> UpdateAsync(Guid id, UpdateAnimeRequest request);
        Task<Result> DeleteAsync(Guid id);
        Task<Result> SyncAllAsync(bool forceRefreshKometaMapping = false);
    }
}
