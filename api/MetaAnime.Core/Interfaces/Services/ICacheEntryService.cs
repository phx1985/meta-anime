using MetaAnime.Core.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;

namespace MetaAnime.Core.Interfaces.Services
{
    public interface ICacheEntryService
    {
        Task<Result<CacheEntry>> AddAsync(Result<CacheEntry> cacheEntry);
        Task<Result<CacheEntry?>> GetAsync(AnimeSource animeSource, string sourceId);
        Task<Result<IEnumerable<CacheEntry>>> GetAsync(AnimeSource animeSource);
        Task<Result> RemoveAsync(string sourceId, AnimeSource animeSource);
        Task<Result> BulkDeleteAllAsync();
        Task<Result> BulkDeleteBySourceAsync(AnimeSource animeSource);
        Task<Result> BulkInsertAsync(IEnumerable<CacheEntry> cacheEntries);
        Task<Result> BulkInsertAsync(IEnumerable<ICacheable> cacheables);
    }
}
