using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class PropertyBagRepository(MetaAnimeDbContext dbContext) : IPropertyBagRepository
{
    // EF DbContext is not thread-safe, and request handlers fan out concurrent tasks that
    // all read settings through this repository (e.g. AnimeMediatorService.Fetch*Async runs
    // one task per mapping and per provider). Without serialization those reads overlap on
    // the shared scoped context and EF throws "A second operation was started on this
    // context instance". One async lock per scoped instance keeps every access strictly
    // sequential — same safety profile as awaiting the calls one by one.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<PropertyBag?> GetByKeyAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            return await dbContext.PropertyBag.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IEnumerable<PropertyBag>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await dbContext.PropertyBag.AsNoTracking().ToListAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(string key, string value)
    {
        await _lock.WaitAsync();
        try
        {
            var existing = await dbContext.PropertyBag.FirstOrDefaultAsync(p => p.Key == key);
            var now = DateTime.UtcNow;

            if (existing is null)
            {
                await dbContext.PropertyBag.AddAsync(new PropertyBag
                {
                    Key = key,
                    Value = value,
                    CreationDate = now,
                    UpdateDate = now
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdateDate = now;
            }

            await dbContext.SaveChangesAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            await dbContext.PropertyBag.Where(p => p.Key == key).ExecuteDeleteAsync();
        }
        finally
        {
            _lock.Release();
        }
    }
}
