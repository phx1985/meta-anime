using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Services;

public sealed class CacheEntryService(ICacheEntryRepository cacheEntryRepository) : ICacheEntryService
{
    public async Task<Result<CacheEntry>> AddAsync(Result<CacheEntry> cacheEntry)
    {
        try
        {
            await cacheEntryRepository.AddAsync(cacheEntry.Value);
            return Result.Success(cacheEntry.Value);
        }
        catch (Exception ex)
        {
            return Result.Failure<CacheEntry>(new Error(Constants.Errors.AddAsync, ex.Message));
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
            return Result.Failure(new Error(Constants.Errors.BulkInsertAsync, ex.Message));
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
            return Result.Failure(new Error(Constants.Errors.BulkInsertAsync, ex.Message));
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
            return Result.Failure(new Error(Constants.Errors.BulkDeleteAllAsync, ex.Message));
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
            return Result.Failure(new Error(Constants.Errors.BulkDeleteBySourceAsync, ex.Message));
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
            return Result.Failure<CacheEntry?>(new Error(Constants.Errors.GetAsync, ex.Message));
        }
    }

    public async Task<Result<IEnumerable<CacheEntry>>> GetAllAsync()
    {
        try
        {
            var cacheEntries = await cacheEntryRepository.GetAllAsync();
            return Result.Success(cacheEntries);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<CacheEntry>>(new Error(Constants.Errors.GetAllAsync, ex.Message));
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
            return Result.Failure<IEnumerable<CacheEntry>>(new Error(Constants.Errors.GetAsync, ex.Message));
        }
    }

    public async Task<Result<IEnumerable<CacheEntry>>> GetBySourceIdsAsync(IEnumerable<string> sourceIds)
    {
        try
        {
            var cacheEntries = await cacheEntryRepository.GetBySourceIdsAsync(sourceIds);
            return Result.Success(cacheEntries);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<CacheEntry>>(new Error(Constants.Errors.GetBySourceIdsAsync, ex.Message));
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
            return Result.Failure(new Error(Constants.Errors.RemoveAsync, ex.Message));
        }
    }
}
