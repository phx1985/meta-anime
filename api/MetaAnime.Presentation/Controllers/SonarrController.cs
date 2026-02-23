using Asp.Versioning;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sonarr")]
public class SonarrController(
    IAnimeMediatorService animeMediatorService,
    ILogger<SonarrController> logger) : ControllerBase
{
    /// <summary>
    /// Returns all Sonarr anime series that contain at least one episode file encoded with the
    /// specified video codec (e.g. "h264", "x264", "hevc", "av1").
    /// </summary>
    [HttpGet("series")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<SonarrCodecSearchResponse>>> GetSeriesByCodec(
        [FromQuery] string codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
            return BadRequest(Constants.Messages.CodecParameterRequired);

        var result = await animeMediatorService.GetSonarrSeriesByCodecAsync(codec);

        if (result.IsFailure)
        {
            logger.LogError("Sonarr codec search failed for codec '{Codec}': {Error}", codec, result.Error.Message);
            return StatusCode(500, Constants.Messages.SonarrCodecSearchError);
        }

        return Ok(result.Value);
    }

    /// <summary>Returns root folders, quality profiles and configured anime tag IDs.</summary>
    [HttpGet("options")]
    [Authorize]
    public async Task<ActionResult<SonarrOptionsResponse>> GetOptions()
    {
        var result = await animeMediatorService.GetSonarrOptionsAsync();
        if (result.IsFailure)
            return StatusCode(500, result.Error.Message);
        return Ok(result.Value);
    }

    /// <summary>Searches Sonarr's series lookup by free-text term.</summary>
    [HttpGet("lookup")]
    [Authorize]
    public async Task<ActionResult<IList<SonarrLookupItem>>> Lookup([FromQuery] string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return BadRequest("term is required");

        var result = await animeMediatorService.SearchSonarrLookupAsync(term);
        if (result.IsFailure)
        {
            logger.LogError("Sonarr lookup failed for term '{Term}': {Error}", term, result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }
        return Ok(result.Value);
    }

    /// <summary>Adds an anime series to Sonarr by TVDB ID.</summary>
    [HttpPost("add")]
    [Authorize]
    public async Task<ActionResult> AddSeries([FromBody] SonarrAddSeriesRequest request)
    {
        if (request.TvdbId <= 0)
            return BadRequest("tvdbId must be a positive integer");
        if (string.IsNullOrWhiteSpace(request.RootFolderPath))
            return BadRequest("rootFolderPath is required");

        var result = await animeMediatorService.AddSonarrSeriesAsync(
            request.TvdbId, request.RootFolderPath, request.QualityProfileId);

        if (result.IsFailure)
        {
            logger.LogError("Sonarr add failed for TVDB ID '{TvdbId}': {Error}", request.TvdbId, result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        return Ok(result.Value);
    }
}

public sealed record SonarrAddSeriesRequest(int TvdbId, string RootFolderPath, int QualityProfileId);

