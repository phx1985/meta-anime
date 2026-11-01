using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Services;

public interface ICacheEntryService
{
    Task<Result<CacheEntry>> AddAsync(Result<CacheEntry> cacheEntry);
    Task<Result<IEnumerable<CacheEntry>>> GetAllAsync();
    Task<Result<CacheEntry?>> GetAsync(AnimeSource animeSource, string sourceId);
    Task<Result<IEnumerable<CacheEntry>>> GetAsync(AnimeSource animeSource);
    Task<Result<IEnumerable<CacheEntry>>> GetBySourceIdsAsync(IEnumerable<string> sourceIds);
    Task<Result> RemoveAsync(string sourceId, AnimeSource animeSource);
    Task<Result> BulkDeleteAllAsync();
    Task<Result> BulkDeleteBySourceAsync(AnimeSource animeSource);
    Task<Result> BulkInsertAsync(IEnumerable<CacheEntry> cacheEntries);
    Task<Result> BulkInsertAsync(IEnumerable<ICacheable> cacheables);
    /// <summary>
    /// Diff-based merge for sync pipelines: inserts new rows, updates rows whose
    /// <see cref="CacheEntry.RawData"/> changed and tombstones rows missing from
    /// <paramref name="incoming"/> for the given source. Unchanged rows are never rewritten.
    /// </summary>
    Task<Result> MergeBySourceAsync(AnimeSource animeSource, IEnumerable<ICacheable> incoming);
    /// <summary>Updates the <see cref="CacheEntry.RawData"/> of existing rows (matched by source + sourceId) in one batched write.</summary>
    Task<Result> BulkUpdateRawDataAsync(AnimeSource animeSource, IEnumerable<(string SourceId, string RawData)> updates);
}
