using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface ICacheEntryRepository
{
    Task AddAsync(CacheEntry cacheEntry);
    Task<IEnumerable<CacheEntry>> GetAllAsync();
    Task<CacheEntry?> GetAsync(AnimeSource animeSource, string sourceId);
    Task<IEnumerable<CacheEntry>> GetAsync(AnimeSource animeSource);
    /// <summary>Returns all cache entries whose SourceId is in the given list.</summary>
    Task<IEnumerable<CacheEntry>> GetBySourceIdsAsync(IEnumerable<string> sourceIds);
    Task RemoveAsync(string sourceId, AnimeSource animeSource);
    Task BulkDeleteAllAsync();
    Task BulkInsertAsync(IEnumerable<CacheEntry> cacheEntries);
    Task BulkInsertAsync(IEnumerable<ICacheable> cacheables);
    /// <summary>Diff-based merge keyed on (AnimeSource, SourceId); unchanged rows are skipped.</summary>
    Task MergeBySourceAsync(AnimeSource animeSource, IEnumerable<CacheEntry> incoming);
    /// <summary>Batched RawData update for existing rows (one SaveChanges, provider-batched).</summary>
    Task BulkUpdateRawDataAsync(AnimeSource animeSource, IEnumerable<(string SourceId, string RawData)> updates);
    Task BulkDeleteBySourceAsync(AnimeSource animeSource);
}
