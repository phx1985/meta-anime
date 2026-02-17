using MetaAnime.Core.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;

namespace MetaAnime.Application.Services
{
    public class CacheEntryService(ICacheEntryRepository cacheEntryRepository) : ICacheEntryService
    {
        public async Task<Result> AddAsync(Result<CacheEntry> cacheEntry)
        {
            try
            {
                await cacheEntryRepository.AddAsync(cacheEntry.Value);
                return Result.Success(cacheEntry);
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Error.AddAsync", ex.Message));
            }
        }

        public async Task<Result> BulkInsertAsync(IEnumerable<CacheEntry> cacheEntries)
        {
            try
            {
                await cacheEntryRepository.BulkInsertAsync(cacheEntries);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Error.BulkInsertAsync", ex.Message));
            }
        }

        public async Task<Result> BulkInsertAsync(IEnumerable<ICacheable> cacheables)
        {
            try
            {
                await cacheEntryRepository.BulkInsertAsync(cacheables);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Error.BulkInsertAsync", ex.Message));
            }
        }

        public async Task<Result> BulkDeleteAllAsync()
        {
            try
            {
                await cacheEntryRepository.BulkDeleteAllAsync();
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Error.BulkDeleteAllAsync", ex.Message));
            }
        }

        public async Task<Result> BulkDeleteBySourceAsync(AnimeSource animeSource)
        {
            try
            {
                await cacheEntryRepository.BulkDeleteBySourceAsync(animeSource);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Error.BulkDeleteBySourceAsync", ex.Message));
            }
        }

        public async Task<Result<CacheEntry?>> GetAsync(AnimeSource animeSource, string sourceId)
        {
            try
            {
                var cacheEntry = await cacheEntryRepository.GetAsync(animeSource, sourceId);
                return Result.Success(cacheEntry);
            }
            catch (Exception ex)
            {
                return Result.Failure<CacheEntry?>(new Error("Error.GetAsync", ex.Message));
            }
        }

        public async Task<Result<IEnumerable<CacheEntry>>> GetAsync(AnimeSource animeSource)
        {
            try
            {
                var cacheEntries = await cacheEntryRepository.GetAsync(animeSource);
                return Result.Success(cacheEntries);
            }
            catch (Exception ex)
            {
                return Result.Failure<IEnumerable<CacheEntry>>(new Error("Error.GetAsync", ex.Message));
            }
        }

        public async Task<Result> RemoveAsync(string sourceId, AnimeSource animeSource)
        {
            try
            {
                await cacheEntryRepository.RemoveAsync(sourceId, animeSource);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Error.RemoveAsync", ex.Message));
            }
        }
    }
}
