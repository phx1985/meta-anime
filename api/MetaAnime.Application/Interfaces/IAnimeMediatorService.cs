using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using MetaAnime.Core.Models;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
namespace MetaAnime.Application.Interfaces;

public interface IAnimeMediatorService
{
    Task<Result<AnimeStatisticsResponse>> GetStatisticsAsync();
    Task<Result<IEnumerable<CoverageGapItemResponse>>> GetCoverageGapAsync(string gapType);
    Task<Result<PagedResponse<IEnumerable<AnimeDetailsResponse>>>> GetAllAsync(int page = 1, int pageSize = 100, AnimeFilters? filters = null, bool enriched = false);
    Task<Result<AnimeDetailsResponse?>> GetByIdAsync(string aniDbId);
    Task<Result<AnimeDetailsResponse?>> GetByMalIdAsync(string malId);
    Task<Result<IEnumerable<SonarrCodecSearchResponse>>> GetSonarrSeriesByCodecAsync(string codec);
    Task<Result<AnimeResponse>> CreateAsync(CreateAnimeRequest request);
    Task<Result<AnimeResponse>> UpdateAsync(Guid id, UpdateAnimeRequest request);
    Task<Result> DeleteAsync(Guid id);
    Task<Result> SyncAllAsync(bool forceRefreshKometaMapping = false);
    Task<Result<IEnumerable<SeasonalAnimeResponse>>> GetSeasonalAnimesAsync(int year, string season);
    Task<Result<SonarrOptionsResponse>> GetSonarrOptionsAsync();
    Task<Result<IList<SonarrLookupItem>>> SearchSonarrLookupAsync(string term);
    Task<Result<SonarrAddResult>> AddSonarrSeriesAsync(int tvdbId, string rootFolderPath, int qualityProfileId, string monitor = "all", bool seasonFolder = true, string? malId = null);
    Task<Result> RemoveSonarrSeriesAsync(int sonarrId);
    Task<Result> UpdateMalListStatusAsync(string malId, string status, int numWatchedEpisodes, int score, string? sourceAniDbId = null);
    Task<Result<FranchiseConstellationResponse>> GetFranchiseConstellationAsync(string malId);
    Task<Result<IList<SonarrTagInfo>>> GetAllSonarrTagsAsync();
    Task<Result<SonarrTagInfo>> CreateSonarrTagAsync(string label);
    // MAL search
    Task<Result<IList<MalSearchResultItem>>> SearchMalAnimeAsync(string query, int limit = 10);
    Task<Result<IEnumerable<SeasonalAnimeResponse>>> SearchMalAnimeWithDetailsAsync(string query, int limit = 100);
    Task<Result<ActivityStatsResponse>> GetActivityStatsAsync(int tzOffsetMinutes = 0);
    Task<Result> EnsureAnimeCastForAnimeAsync(string malId);
    /// <summary>
    /// Batch-resolves titles + poster URLs for MAL ids from local data (Anime table, cached MAL
    /// payloads) with bounded live MAL lookups as a last resort. Ids that cannot be resolved are
    /// simply absent from the result dictionary.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, AnimeCoverInfo>>> ResolveMalCoversAsync(IEnumerable<string> malIds);
}
