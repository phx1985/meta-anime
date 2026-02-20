using EFCore.BulkExtensions;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories
{
    public class CacheEntryRepository(MetaAnimeDbContext dbContext) : ICacheEntryRepository
    {
        public async Task AddAsync(CacheEntry cacheEntry)
        {
            await dbContext.CacheEntries.AddAsync(cacheEntry);
            await dbContext.SaveChangesAsync();
        }

        public async Task<CacheEntry?> GetAsync(AnimeSource animeSource, string sourceId)
        {
            return await dbContext.CacheEntries.FirstOrDefaultAsync(ce => ce.SourceId == sourceId && ce.AnimeSource == animeSource);
        }

        public async Task<IEnumerable<CacheEntry>> GetAllAsync()
        {
            return await dbContext.CacheEntries.AsNoTracking().ToListAsync();
        }

        public async Task<IEnumerable<CacheEntry>> GetAsync(AnimeSource animeSource)
        {
            return await dbContext.CacheEntries.AsNoTracking().Where(ce => ce.AnimeSource == animeSource).ToListAsync();
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
            SetCacheEntryIds(cacheEntries);
            await dbContext.BulkInsertAsync(cacheEntries);
            await dbContext.SaveChangesAsync();
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
}
