using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Services
{
    public interface IAnimeService
    {
        Task<Result<Anime?>> GetAnimeAsync(AnimeSource source, string sourceId);
        Task<Result<Anime?>> GetByIdAsync(Guid id);
        Task<Result> BulkInsertAsync(IEnumerable<Anime> animes);
        Task<Result> BulkInsertAsync(IEnumerable<IAnimeConvertible> animeConvertibles);
        Task<Result> BulkDeleteBySourceAsync(AnimeSource animeSource);
        Task<Result> BulkDeleteAllAsync();
        Task<Result> DeleteAsync(Guid id);
        Task<Result<IEnumerable<Anime>>> GetAllBySourceAsync(AnimeSource source);
        Task<Result<IEnumerable<Anime>>> GetAllAsync();
        Task<Result<IEnumerable<Anime>>> RefreshData();
    }
}
