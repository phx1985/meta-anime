using Asp.Versioning;
using Hangfire;
using Mediator;
using ApplicationConstants = MetaAnime.Application.Constants;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/animes")]
public sealed class AnimeController(IMediator mediator, IBackgroundJobClient backgroundJobClient)
    : ControllerBase
{
    [HttpGet("statistics")]
    [Authorize]
    public async Task<ActionResult<AnimeStatisticsResponse>> GetStatistics()
    {
        var result = await mediator.Send(new GetAnimeStatisticsQuery());

        if (result.IsFailure)
        {
            return StatusCode(500, result.Error.Message);
        }

        return Ok(result.Value);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<PagedResponse<IEnumerable<AnimeDetailsResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? title = null,
        [FromQuery] string? source = null,
        [FromQuery] string[]? status = null,
        [FromQuery] string[]? genre = null,
        [FromQuery] string? genreOperator = "or",
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        [FromQuery] bool enriched = false,
        [FromQuery] string[]? airingStatus = null,
        [FromQuery] bool? hasFile = null,
        [FromQuery] string[]? codec = null,
        [FromQuery] string[]? mediaType = null,
        [FromQuery] string[]? theme = null,
        [FromQuery] string? themeOperator = "or",
        [FromQuery] string[]? demographic = null,
        [FromQuery] string? demographicOperator = "or")
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 100;

        var filters = new AnimeFilters(title, source, status, genre, genreOperator, sortBy, sortOrder, airingStatus, hasFile, codec, mediaType, theme, themeOperator, demographic, demographicOperator);
        var result = await mediator.Send(new GetAllAnimesQuery(page, pageSize, filters, enriched));

        if (result.IsFailure)
        {
            return StatusCode(500, result.Error.Message);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<AnimeDetailsResponse>> GetById(string id)
    {
        var result = await mediator.Send(new GetAnimeByIdQuery(id));

        if (result.IsFailure)
        {
            return StatusCode(500, result.Error.Message);
        }

        if (result.Value == null)
        {
            return NotFound();
        }

        return Ok(result.Value);
    }

    [HttpGet("by-mal/{malId}")]
    [Authorize]
    public async Task<ActionResult<AnimeDetailsResponse>> GetByMalId(string malId)
    {
        var result = await mediator.Send(new GetAnimeByMalIdQuery(malId));

        if (result.IsFailure)
        {
            return StatusCode(500, result.Error.Message);
        }

        if (result.Value == null)
        {
            return NotFound();
        }

        return Ok(result.Value);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<AnimeResponse>> Create([FromBody] CreateAnimeRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await mediator.Send(new CreateAnimeCommand(request));

        if (result.IsFailure)
        {
            return StatusCode(500, result.Error.Message);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<ActionResult<AnimeResponse>> Update(Guid id, [FromBody] UpdateAnimeRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await mediator.Send(new UpdateAnimeCommand(id, request));

        if (result.IsFailure)
        {
            if (result.Error.Code == ApplicationConstants.Errors.NotFound)
            {
                return NotFound(result.Error.Message);
            }
            return StatusCode(500, result.Error.Message);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await mediator.Send(new DeleteAnimeCommand(id));

        if (result.IsFailure)
        {
            if (result.Error.Code == ApplicationConstants.Errors.NotFound)
            {
                return NotFound(result.Error.Message);
            }
            return StatusCode(500, result.Error.Message);
        }

        return NoContent();
    }

    /// <summary>
    /// Manually triggers a full cache refresh and anime matching across sources.
    /// The job is enqueued via Hangfire and runs in the background; SignalR broadcasts
    /// progress to connected clients. Kometa mappings are always refreshed.
    /// </summary>
    [HttpPost("sync")]
    [Authorize]
    public IActionResult SyncAll()
    {
        backgroundJobClient.Enqueue<AnimeSyncJob>(job => job.SyncAnimeMetadata(true));

        return Accepted(new { message = "Sync started" });
    }
}
