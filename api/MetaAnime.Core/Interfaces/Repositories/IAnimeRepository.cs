using MetaAnime.Core.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Models;

namespace MetaAnime.Core.Interfaces.Repositories
{
    public interface IAnimeRepository
    {
        Task<Anime?> GetAnimeAsync(AnimeSource source, string sourceId);
        Task BulkInsertAsync(IEnumerable<Anime> animes);
        Task BulkInsertAsync(IEnumerable<IAnimeConvertible> animeConvertibles);
        Task BulkDeleteBySourceAsync(AnimeSource animeSource);
        Task BulkDeleteAllAsync();
        Task<IEnumerable<Anime>> GetAllBySourceAsync(AnimeSource source);
        Task<IEnumerable<Anime>> GetAllAsync();
    }
}
