using Asp.Versioning;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Presentation;
using MetaAnime.Utilities.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/sonarr")]
public sealed class SonarrController(
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
            return this.ToErrorResult(result, statusCode: StatusCodes.Status500InternalServerError, message: Constants.Messages.SonarrCodecSearchError);
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
            return this.ToErrorResult(result);
        return Ok(result.Value);
    }

    /// <summary>Searches Sonarr's series lookup by free-text term.</summary>
    [HttpGet("lookup")]
    [Authorize]
    public async Task<ActionResult<IList<SonarrLookupItem>>> Lookup([FromQuery] string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return BadRequest(Constants.Messages.SonarrTermRequired);

        var result = await animeMediatorService.SearchSonarrLookupAsync(term);
        if (result.IsFailure)
        {
            logger.LogError("Sonarr lookup failed for term '{Term}': {Error}", term, result.Error.Message);
            return this.ToErrorResult(result);
        }
        return Ok(result.Value);
    }

    /// <summary>Adds an anime series to Sonarr by TVDB ID.</summary>
    [HttpPost("add")]
    [Authorize]
    public async Task<ActionResult<SonarrAddResult>> AddSeries([FromBody] SonarrAddSeriesRequest request)
    {
        if (request.TvdbId <= 0)
            return BadRequest(Constants.Messages.SonarrTvdbIdRequired);
        if (string.IsNullOrWhiteSpace(request.RootFolderPath))
            return BadRequest(Constants.Messages.SonarrRootFolderRequired);
        if (!SonarrAddSeriesRequest.ValidMonitorTypes.Contains(request.Monitor))
            return BadRequest($"Invalid monitor type '{request.Monitor}'. Valid values: {string.Join(", ", SonarrAddSeriesRequest.ValidMonitorTypes)}.");

        var result = await animeMediatorService.AddSonarrSeriesAsync(
            request.TvdbId, request.RootFolderPath, request.QualityProfileId,
            request.Monitor, request.SeasonFolder, request.MalId);

        if (result.IsFailure)
        {
            logger.LogError("Sonarr add failed for TVDB ID '{TvdbId}': {Error}", request.TvdbId, result.Error.Message);
            return this.ToErrorResult(result);
        }

        return Ok(result.Value);
    }

    /// <summary>Removes a series from Sonarr by its numeric series ID (used for rollback; does not delete files).</summary>
    [HttpDelete("series/{sonarrId:int}")]
    [Authorize]
    public async Task<ActionResult> RemoveSeries(int sonarrId)
    {
        var result = await animeMediatorService.RemoveSonarrSeriesAsync(sonarrId);
        if (result.IsFailure)
        {
            logger.LogError("Sonarr remove failed for series '{SonarrId}': {Error}", sonarrId, result.Error.Message);
            return this.ToErrorResult(result);
        }
        return NoContent();
    }

    /// <summary>Returns all tags defined in the connected Sonarr instance.</summary>
    [HttpGet("tags")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<SonarrTagDto>>> GetTags()
    {
        var result = await animeMediatorService.GetAllSonarrTagsAsync();
        if (result.IsFailure)
        {
            logger.LogError("Failed to fetch Sonarr tags: {Error}", result.Error.Message);
            return this.ToErrorResult(result);
        }
        return Ok(result.Value.Select(t => new SonarrTagDto(t.Id, t.Label)));
    }

    /// <summary>Creates a new tag in the connected Sonarr instance.</summary>
    [HttpPost("tags")]
    [Authorize]
    public async Task<ActionResult<SonarrTagDto>> CreateTag([FromBody] CreateSonarrTagRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return BadRequest("Tag label is required.");

        var result = await animeMediatorService.CreateSonarrTagAsync(request.Label.Trim());
        if (result.IsFailure)
        {
            logger.LogError("Failed to create Sonarr tag '{Label}': {Error}", request.Label, result.Error.Message);
            return this.ToErrorResult(result);
        }
        return Ok(new SonarrTagDto(result.Value.Id, result.Value.Label));
    }
}

public sealed record SonarrTagDto(int Id, string Label);
public sealed record CreateSonarrTagRequest(string Label);

public sealed record SonarrAddSeriesRequest(int TvdbId, string RootFolderPath, int QualityProfileId, string Monitor = "all", bool SeasonFolder = true, string? MalId = null)
{
    public static readonly IReadOnlySet<string> ValidMonitorTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "all", "future", "missing", "existing", "recent",
        "pilot", "firstSeason", "lastSeason",
        "monitorSpecials", "unmonitorSpecials", "none",
    };
};

