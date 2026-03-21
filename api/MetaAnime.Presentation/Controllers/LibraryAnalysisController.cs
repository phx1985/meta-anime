using Asp.Versioning;
using MetaAnime.Application.Contracts.LibraryAnalysis;
using MetaAnime.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers;

/// <summary>
/// Exposes library storage analysis — Phase 1 returns aggregate series stats,
/// Phase 2 enriches selected series with per-episode MediaInfo codec/resolution data.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/library-analysis")]
public sealed class LibraryAnalysisController(
    ILibraryAnalysisService libraryAnalysisService,
    ILogger<LibraryAnalysisController> logger) : ControllerBase
{
    /// <summary>
    /// Phase 1 — returns a summary of all Sonarr anime series with size/count
    /// statistics. Codec and episode-file details are null (use POST /enrich).
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<LibraryAnalysisSummary>> GetSummary()
    {
        var result = await libraryAnalysisService.GetSummaryAsync();
        if (result.IsFailure)
        {
            logger.LogError("Failed to get library analysis: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }
        return Ok(result.Value);
    }

    /// <summary>
    /// Phase 2 — fetches episode files for the specified Sonarr series IDs and
    /// returns enriched rows with codec distributions and individual file details.
    /// </summary>
    [HttpPost("enrich")]
    [Authorize]
    public async Task<ActionResult<IList<LibrarySeriesRow>>> EnrichSeries(
        [FromBody] EnrichSeriesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SonarrIds is not { Count: > 0 })
            return BadRequest("At least one Sonarr series ID is required.");

        if (request.SonarrIds.Count > 1000)
            return BadRequest("Maximum 1000 series IDs allowed per request.");

        var result = await libraryAnalysisService.EnrichSeriesAsync(request.SonarrIds, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogError("Failed to enrich library series: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }
        return Ok(result.Value);
    }
}

/// <summary>Request body for the Phase 2 enrichment endpoint.</summary>
public sealed record EnrichSeriesRequest(IList<int> SonarrIds);
