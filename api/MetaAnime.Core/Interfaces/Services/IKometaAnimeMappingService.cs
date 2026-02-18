using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Services
{
    public interface IKometaAnimeMappingService
    {
        Task<Result<IList<KometaAnimeMapping>>> GetAllAsync();
        Task<Result<IList<KometaAnimeMapping>>> GetByAniDbIdAsync(string aniDbId);
        Task<Result<KometaAnimeMapping?>> GetByPlatformIdAsync(KometaAnimePlatform animePlatform, string platformId);
        Task<Result> BulkDeleteAllAsync();
        Task<Result> BulkInsertAsync(IEnumerable<KometaAnimeMapping> kometaAnimeMappings);
        Task<Result<bool>> AnyAsync();
    }
}
