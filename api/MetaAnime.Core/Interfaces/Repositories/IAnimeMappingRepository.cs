using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories
{
    public interface IAnimeMappingRepository
    {
        Task<IEnumerable<AnimeMapping>> GetAllAsync(bool includeAnime = false);
        Task BulkDeleteAllAsync();
        Task BulkInsertAsync(IEnumerable<AnimeMapping> mappings);
    }
}
