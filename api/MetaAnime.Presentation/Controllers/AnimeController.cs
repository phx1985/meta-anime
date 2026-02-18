using Asp.Versioning;
using MediatR;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/animes")]
    public class AnimeController(IMediator mediator, IAnimeMatchingService animeMatchingService)
        : ControllerBase
    {
        [HttpGet]
        [Authorize]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
        public async Task<ActionResult<IEnumerable<AnimeDetailsResponse>>> GetAll()
        {
            await animeMatchingService.MatchAsync();
            var result = await mediator.Send(new GetAllAnimesQuery());
            
            if (result.IsFailure)
            {
                return StatusCode(500, result.Error.Message);
            }

            return Ok(result.Value);
        }

        [HttpGet("{id}")]
        [Authorize]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
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
    }
}
