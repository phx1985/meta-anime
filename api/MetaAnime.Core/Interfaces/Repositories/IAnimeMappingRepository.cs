using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;

namespace MetaAnime.Core.Interfaces.Repositories
{
    public interface IAnimeMappingRepository
    {
        Task<IEnumerable<AnimeMapping>> GetAllAsync(bool includeAnime = false);
        /// <summary>
        /// Returns a page of distinct AniDbIds (filtered by title/source at SQL level) plus the total count.
        /// Pagination, filtering and sorting all happen inside the database — no in-memory scan needed.
        /// </summary>
        Task<(List<string> AniDbIds, int TotalCount)> GetPagedAniDbIdsAsync(
            int page, int pageSize,
            string? titleFilter = null,
            AnimeSource? sourceFilter = null,
            string? sortBy = null,
            bool sortDescending = false);
        /// <summary>
        /// Returns all distinct AniDbIds matching the optional title/source filter without pagination.
        /// Used by the slow path (genre/status post-filter) to load only the pre-filtered subset.
        /// </summary>
        Task<List<string>> GetAllFilteredAniDbIdsAsync(
            string? titleFilter = null,
            AnimeSource? sourceFilter = null);
        /// <summary>
        /// Returns all AnimeMapping rows (with their Anime) for the given AniDbIds.
        /// </summary>
        Task<IEnumerable<AnimeMapping>> GetByAniDbIdsAsync(IEnumerable<string> aniDbIds);
        Task BulkDeleteAllAsync();
        Task BulkInsertAsync(IEnumerable<AnimeMapping> mappings);
    }
}
