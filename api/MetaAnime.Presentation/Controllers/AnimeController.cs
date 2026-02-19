using Asp.Versioning;
using MediatR;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/animes")]
    public class AnimeController(IMediator mediator)
        : ControllerBase
    {
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<PagedResponse<IEnumerable<AnimeDetailsResponse>>>> GetAll(
            [FromQuery] int page = 1, 
            [FromQuery] int pageSize = 100,
            [FromQuery] string? title = null,
            [FromQuery] string? source = null,
            [FromQuery] string? status = null,
            [FromQuery] string? genre = null,
            [FromQuery] string? sortBy = null,
            [FromQuery] string? sortOrder = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 100;

            var filters = new AnimeFilters(title, source, status, genre, sortBy, sortOrder);
            var result = await mediator.Send(new GetAllAnimesQuery(page, pageSize, filters));
            
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
                if (result.Error.Code == "Error.NotFound")
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
                if (result.Error.Code == "Error.NotFound")
                {
                    return NotFound(result.Error.Message);
                }
                return StatusCode(500, result.Error.Message);
            }

            return NoContent();
        }

        /// <summary>
        /// Manually triggers a full cache refresh and anime matching across sources.
        /// This operation fetches fresh data from Plex, MAL, and Sonarr APIs, updates the cache,
        /// and performs matching using Kometa mapping data.
        /// </summary>
        /// <param name="forceRefreshKometaMapping">If true, forces a refresh of the Kometa mapping data before matching</param>
        [HttpPost("sync")]
        [Authorize]
        public async Task<IActionResult> SyncAll([FromQuery] bool forceRefreshKometaMapping = false)
        {
            var result = await mediator.Send(new MatchAnimeCommand(forceRefreshKometaMapping));
            
            if (result.IsFailure)
            {
                return StatusCode(500, result.Error.Message);
            }

            return Ok(new { message = "Cache refresh and matching completed successfully" });
        }
    }
}
