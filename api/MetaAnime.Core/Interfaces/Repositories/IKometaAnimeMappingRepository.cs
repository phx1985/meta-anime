using MetaAnime.Core.Enums;
using MetaAnime.Core.Models;

namespace MetaAnime.Core.Interfaces.Repositories
{
    public interface IKometaAnimeMappingRepository
    {
        Task<IList<KometaAnimeMapping>> GetAllAsync();
        Task<IList<KometaAnimeMapping>> GetByAniDbIdAsync(string aniDbId);
        Task<KometaAnimeMapping?> GetByPlatformIdAsync(KometaAnimePlatform animePlatform, string platformId);
        Task BulkDeleteAllAsync();
        Task BulkInsertAsync(IEnumerable<KometaAnimeMapping> kometaAnimeMappings);
        Task<bool> AnyAsync();
    }
}
