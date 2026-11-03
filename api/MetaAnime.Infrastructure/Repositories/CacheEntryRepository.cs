using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class CacheEntryRepository(MetaAnimeDbContext dbContext) : ICacheEntryRepository
{
    public async Task AddAsync(CacheEntry cacheEntry)
    {
        await dbContext.CacheEntries.AddAsync(cacheEntry);
        await dbContext.SaveChangesAsync();
    }

    public async Task<CacheEntry?> GetAsync(AnimeSource animeSource, string sourceId)
    {
        return await dbContext.CacheEntries.AsNoTracking().FirstOrDefaultAsync(ce => ce.SourceId == sourceId && ce.AnimeSource == animeSource);
    }

    public async Task<IEnumerable<CacheEntry>> GetAllAsync()
    {
        return await dbContext.CacheEntries.AsNoTracking().ToListAsync();
    }

    public async Task<IEnumerable<CacheEntry>> GetAsync(AnimeSource animeSource)
    {
        return await dbContext.CacheEntries.AsNoTracking().Where(ce => ce.AnimeSource == animeSource).ToListAsync();
    }

    public async Task<IEnumerable<CacheEntry>> GetBySourceIdsAsync(IEnumerable<string> sourceIds)
    {
        var idList = sourceIds.ToList();
        return await dbContext.CacheEntries
            .AsNoTracking()
            .Where(ce => idList.Contains(ce.SourceId))
            .ToListAsync();
    }

    public async Task RemoveAsync(string sourceId, AnimeSource animeSource)
    {
        var cacheEntry = await GetAsync(animeSource, sourceId);
        if (cacheEntry != null)
        {
            dbContext.CacheEntries.Remove(cacheEntry);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task BulkDeleteAllAsync()
    {
        await dbContext.CacheEntries.ExecuteDeleteAsync();
    }

    public async Task BulkInsertAsync(IEnumerable<CacheEntry> cacheEntries)
    {
        var entryList = cacheEntries.ToList();
        SetCacheEntryIds(entryList);
        await dbContext.BulkCopyAsync(entryList);
    }

    public async Task BulkInsertAsync(IEnumerable<ICacheable> cacheables)
    {
        var cacheEntries = cacheables.Select(c => c.ToCacheEntry()).ToList();
        await BulkInsertAsync(cacheEntries);
    }

    public async Task BulkDeleteBySourceAsync(AnimeSource animeSource)
    {
        await dbContext.CacheEntries.Where(ce => ce.AnimeSource == animeSource).ExecuteDeleteAsync();
    }

    /// <inheritdoc />
    public async Task MergeBySourceAsync(AnimeSource animeSource, IEnumerable<CacheEntry> incoming)
    {
        var incomingList = incoming.ToList();
        var existing = await dbContext.CacheEntries
            .Where(ce => ce.AnimeSource == animeSource)
            .ToListAsync();

        var existingBySourceId = existing
            .GroupBy(ce => ce.SourceId)
            .ToDictionary(g => g.Key, g => g.First());
        var matchedSourceIds = new HashSet<string>(StringComparer.Ordinal);
        var toInsert = new List<CacheEntry>();

        foreach (var inc in incomingList)
        {
            matchedSourceIds.Add(inc.SourceId);
            if (!existingBySourceId.TryGetValue(inc.SourceId, out var ex))
            {
                toInsert.Add(inc);
                continue;
            }

            // Diff on RawData only: rows whose serialized payload is unchanged are never rewritten.
            if (!string.Equals(ex.RawData, inc.RawData, StringComparison.Ordinal))
            {
                ex.RawData = inc.RawData;
            }
        }

        var staleIds = existing
            .Where(ce => !matchedSourceIds.Contains(ce.SourceId))
            .Select(ce => ce.Id)
            .ToList();
        if (staleIds.Count > 0)
        {
            await dbContext.CacheEntries.Where(ce => staleIds.Contains(ce.Id)).ExecuteDeleteAsync();
        }

        await dbContext.SaveChangesAsync();

        if (toInsert.Count > 0)
            await BulkInsertAsync(toInsert);
    }

    /// <inheritdoc />
    public async Task BulkUpdateRawDataAsync(AnimeSource animeSource, IEnumerable<(string SourceId, string RawData)> updates)
    {
        var map = updates.ToDictionary(u => u.SourceId, u => u.RawData, StringComparer.Ordinal);
        if (map.Count == 0)
            return;

        var ids = map.Keys.ToList();
        var rows = await dbContext.CacheEntries
            .Where(ce => ce.AnimeSource == animeSource && ids.Contains(ce.SourceId))
            .ToListAsync();

        foreach (var row in rows)
        {
            if (map.TryGetValue(row.SourceId, out var rawData))
                row.RawData = rawData;
        }

        // One SaveChanges: the provider batches the row updates into few commands.
        await dbContext.SaveChangesAsync();
    }

    #region Private Methods
    private static void SetCacheEntryIds(IEnumerable<CacheEntry> cacheEntries)
    {
        foreach (var cacheEntry in cacheEntries)
        {
            cacheEntry.Id = Guid.CreateVersion7();
        }
    }
    #endregion
}
