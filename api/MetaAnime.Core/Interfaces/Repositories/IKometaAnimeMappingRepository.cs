using MetaAnime.Domain.Enums;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface IKometaAnimeMappingRepository
{
    Task<IList<KometaAnimeMapping>> GetAllAsync();
    Task<IList<KometaAnimeMapping>> GetByAniDbIdAsync(string aniDbId);
    Task<KometaAnimeMapping?> GetByPlatformIdAsync(KometaAnimePlatform animePlatform, string platformId);
    /// <summary>Returns all mappings for the given platform whose PlatformId is in <paramref name="platformIds"/>.</summary>
    Task<IList<KometaAnimeMapping>> GetByPlatformIdsAsync(KometaAnimePlatform animePlatform, IEnumerable<string> platformIds);
    /// <summary>Returns all mappings for the given platform whose AniDbId is in <paramref name="aniDbIds"/>.</summary>
    Task<IList<KometaAnimeMapping>> GetByAniDbIdsAndPlatformAsync(KometaAnimePlatform animePlatform, IEnumerable<string> aniDbIds);
    Task BulkDeleteAllAsync();
    Task BulkInsertAsync(IEnumerable<KometaAnimeMapping> kometaAnimeMappings);
    Task<bool> AnyAsync();
}
