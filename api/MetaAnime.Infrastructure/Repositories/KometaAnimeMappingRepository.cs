using EFCore.BulkExtensions;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.Utilities.Extensions;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public class KometaAnimeMappingRepository(MetaAnimeDbContext dbContext)
    : IKometaAnimeMappingRepository
{
    public async Task<IList<KometaAnimeMapping>> GetAllAsync()
    {
        return await dbContext.KometaAnimeMapping.AsNoTracking().ToListAsync();
    }
    public async Task<IList<KometaAnimeMapping>> GetByAniDbIdAsync(string aniDbId)
    {
        return await dbContext.KometaAnimeMapping
            .AsNoTracking()
            .Where(kam => kam.AniDbId == aniDbId)
            .ToListAsync();
    }
    public async Task<KometaAnimeMapping?> GetByPlatformIdAsync(KometaAnimePlatform animePlatform, string platformId)
    {
        var platformName = animePlatform.GetPlatformName();
        return await dbContext.KometaAnimeMapping
                              .AsNoTracking()
                              .Where(kam => kam.PlatformName == platformName && kam.PlatformId == platformId)
                              .FirstOrDefaultAsync();
    }
    public async Task BulkDeleteAllAsync()
    {
        await dbContext.KometaAnimeMapping.ExecuteDeleteAsync();
    }
    public async Task BulkInsertAsync(IEnumerable<KometaAnimeMapping> kometaAnimeMappings)
    {
        foreach (var kam in kometaAnimeMappings)
        {
            kam.Id = Guid.CreateVersion7();
        }
        await dbContext.BulkInsertAsync(kometaAnimeMappings);
        await dbContext.SaveChangesAsync();
    }

    public async Task<bool> AnyAsync()
    {
        return await dbContext.KometaAnimeMapping.AsNoTracking().AnyAsync();
    }
}
