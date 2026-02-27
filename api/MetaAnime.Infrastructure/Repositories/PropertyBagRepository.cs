using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class PropertyBagRepository(MetaAnimeDbContext dbContext) : IPropertyBagRepository
{
    public async Task<PropertyBag?> GetByKeyAsync(string key)
    {
        return await dbContext.PropertyBag.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key);
    }

    public async Task<IEnumerable<PropertyBag>> GetAllAsync()
    {
        return await dbContext.PropertyBag.AsNoTracking().ToListAsync();
    }

    public async Task UpsertAsync(string key, string value)
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

    public async Task DeleteAsync(string key)
    {
        await dbContext.PropertyBag.Where(p => p.Key == key).ExecuteDeleteAsync();
    }
}
