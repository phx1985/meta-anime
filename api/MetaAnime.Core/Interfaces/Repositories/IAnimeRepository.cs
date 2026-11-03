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
    /// <summary>
    /// Diff-based merge keyed on (AnimeSource, SourceId): inserts new rows with fresh
    /// <see cref="Guid.CreateVersion7"/> ids, updates changed rows in place (ids stay stable)
    /// and tombstones rows missing from <paramref name="incoming"/>. Unchanged rows are never rewritten.
    /// </summary>
    Task MergeBySourceAsync(AnimeSource animeSource, IEnumerable<Anime> incoming);
    Task<IEnumerable<Anime>> GetAllBySourceAsync(AnimeSource source);
    Task<IEnumerable<Anime>> GetAllAsync();
    Task<IEnumerable<Anime>> GetManyBySourceIdsAsync(AnimeSource source, IEnumerable<string> sourceIds);
}
