using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;

namespace MetaAnime.Core.Interfaces.Services
{
    public interface IAnimeMappingService
    {
        Task<Result<IEnumerable<AnimeMapping>>> GetAllAsync(bool includeAnime = false);
        Task<Result> BulkDeleteAllAsync();
        Task<Result> BulkInsertAsync(IEnumerable<AnimeMapping> mappings);
    }
}
