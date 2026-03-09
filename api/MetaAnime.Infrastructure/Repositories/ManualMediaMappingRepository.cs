using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class ManualMediaMappingRepository(MetaAnimeDbContext dbContext) : IManualMediaMappingRepository
{
    public async Task<ManualMediaMapping?> GetByPlexSourceIdAsync(string plexSourceId)
        => await dbContext.ManualMediaMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.PlexSourceId == plexSourceId);

    public async Task<IList<ManualMediaMapping>> GetAllAsync()
        => await dbContext.ManualMediaMappings
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

    public async Task AddAsync(ManualMediaMapping mapping)
    {
        mapping.Id = Guid.CreateVersion7();
        await dbContext.ManualMediaMappings.AddAsync(mapping);
        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await dbContext.ManualMediaMappings
            .Where(m => m.Id == id)
            .ExecuteDeleteAsync();
    }
}
