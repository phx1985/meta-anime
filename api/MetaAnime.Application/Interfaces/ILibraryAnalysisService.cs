using MetaAnime.Application.Contracts.LibraryAnalysis;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Interfaces;

/// <summary>
/// Exposes storage-efficiency analysis of the anime library by aggregating
/// Sonarr series metadata (Phase 1) and per-episode MediaInfo (Phase 2).
/// </summary>
public interface ILibraryAnalysisService
{
    /// <summary>
    /// Phase 1 — fetches all Sonarr series in a single API call and returns
    /// aggregate statistics plus a flat series list (codec/file details are null).
    /// </summary>
    Task<Result<LibraryAnalysisSummary>> GetSummaryAsync();

    /// <summary>
    /// Phase 2 — for each supplied Sonarr series ID, fetches episode files,
    /// computes codec/resolution distributions and returns fully enriched rows.
    /// </summary>
    Task<Result<IList<LibrarySeriesRow>>> EnrichSeriesAsync(IList<int> sonarrIds, CancellationToken cancellationToken = default);
}
