using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;

namespace MetaAnime.Core.Interfaces.Services;

public interface IAnimeMappingService
{
    Task<Result<IEnumerable<AnimeMapping>>> GetAllAsync(bool includeAnime = false);
    Task<Result<(List<string> AniDbIds, int TotalCount)>> GetPagedAniDbIdsAsync(
        int page, int pageSize,
        string? titleFilter = null,
        AnimeSource? sourceFilter = null,
        string? sortBy = null,
        bool sortDescending = false);
    Task<Result<List<string>>> GetAllFilteredAniDbIdsAsync(
        string? titleFilter = null,
        AnimeSource? sourceFilter = null);
    Task<Result<IEnumerable<AnimeMapping>>> GetByAniDbIdsAsync(IEnumerable<string> aniDbIds);
    Task<Result<string?>> GetAniDbIdByMalIdAsync(string malId);
    Task<Result> BulkDeleteAllAsync();
    Task<Result> BulkInsertAsync(IEnumerable<AnimeMapping> mappings);
    /// <summary>
    /// Diff-based merge keyed on (AnimeId, AniDbId): inserts new pairs and tombstones stale
    /// pairs. Unchanged pairs are never rewritten.
    /// </summary>
    Task<Result> MergeAsync(IEnumerable<AnimeMapping> mappings);
}
