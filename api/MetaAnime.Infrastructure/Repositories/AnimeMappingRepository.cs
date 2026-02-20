using EFCore.BulkExtensions;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories
{
    public class AnimeMappingRepository(MetaAnimeDbContext dbContext) : IAnimeMappingRepository
    {
        public async Task BulkDeleteAllAsync()
        {
            await dbContext.AnimeMappings.ExecuteDeleteAsync();
        }

        public async Task BulkInsertAsync(IEnumerable<AnimeMapping> mappings)
        {
            foreach (var mapping in mappings)
                mapping.Id = Guid.CreateVersion7();

            await dbContext.BulkInsertAsync(mappings);
            await dbContext.SaveChangesAsync();
        }

        public async Task<IEnumerable<AnimeMapping>> GetAllAsync(bool includeAnime = false)
        {
            return includeAnime
                ? await dbContext.AnimeMappings.AsNoTracking().Include(am => am.Anime).ToListAsync()
                : await dbContext.AnimeMappings.AsNoTracking().ToListAsync();
        }
    }
}
