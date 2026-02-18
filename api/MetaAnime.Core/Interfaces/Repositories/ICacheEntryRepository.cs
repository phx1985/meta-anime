using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories
{
    public interface ICacheEntryRepository
    {
        Task AddAsync(CacheEntry cacheEntry);
        Task<CacheEntry?> GetAsync(AnimeSource animeSource, string sourceId);
        Task<IEnumerable<CacheEntry>> GetAsync(AnimeSource animeSource);
        Task RemoveAsync(string sourceId, AnimeSource animeSource);
        Task BulkDeleteAllAsync();
        Task BulkInsertAsync(IEnumerable<CacheEntry> cacheEntries);
        Task BulkInsertAsync(IEnumerable<ICacheable> cacheables);
        Task BulkDeleteBySourceAsync(AnimeSource animeSource);
    }
}
