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
    /// <summary>
    /// Returns (MalId, AniDbId, TvdbId?) tuples for the given MAL IDs using a single
    /// self-join query on the Kometa mapping table — the sole source of truth.
    /// TvdbId is null when Kometa has no TVDB entry for the anime's AniDB ID.
    /// </summary>
    Task<IList<(string MalId, string AniDbId, string? TvdbId)>> GetMalMappingsWithTvdbAsync(IEnumerable<string> malIds);
    Task BulkDeleteAllAsync();
    Task BulkInsertAsync(IEnumerable<KometaAnimeMapping> kometaAnimeMappings);
    Task<bool> AnyAsync();
}
