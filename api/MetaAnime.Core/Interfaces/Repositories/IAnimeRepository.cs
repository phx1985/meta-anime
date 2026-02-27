using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface IAnimeRepository
{
    Task<Anime?> GetAnimeAsync(AnimeSource source, string sourceId);
    Task<Anime?> GetByIdAsync(Guid id);
    Task AddAsync(Anime anime);
    Task BulkInsertAsync(IEnumerable<Anime> animes);
    Task BulkInsertAsync(IEnumerable<IAnimeConvertible> animeConvertibles);
    Task BulkDeleteBySourceAsync(AnimeSource animeSource);
    Task BulkDeleteAllAsync();
    Task DeleteAsync(Guid id);
    Task<IEnumerable<Anime>> GetAllBySourceAsync(AnimeSource source);
    Task<IEnumerable<Anime>> GetAllAsync();
}
