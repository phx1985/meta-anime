using MetaAnime.Application.DTOs;
using MetaAnime.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers
{
    [ApiController]
    [Route("api/animes")]
    public class AnimeController(IAnimeMediatorService animeMediatorService)
        : ControllerBase
    {
        [HttpGet]
        [Authorize]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
        public async Task<ActionResult<PaginatedResult<GetAnimeDto>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            var result = await animeMediatorService.GetAllAsync(page, pageSize);
            
            if (result.IsFailure)
            {
                return StatusCode(500, result.Error.Message);
            }

            return Ok(result.Value);
        }

        [HttpGet("{id}")]
        [Authorize]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
        public async Task<ActionResult<GetAnimeDto>> GetById(string id)
        {
            var result = await animeMediatorService.GetByIdAsync(id);
            
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
        public async Task<ActionResult<AnimeDto>> Create([FromBody] CreateAnimeRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await animeMediatorService.CreateAsync(request);
            
            if (result.IsFailure)
            {
                return StatusCode(500, result.Error.Message);
            }

            return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<ActionResult<AnimeDto>> Update(Guid id, [FromBody] UpdateAnimeRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await animeMediatorService.UpdateAsync(id, request);
            
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
            var result = await animeMediatorService.DeleteAsync(id);
            
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

        [HttpPost("sync")]
        [Authorize]
        public async Task<IActionResult> Sync([FromQuery] bool forceRefreshKometaMapping = false)
        {
            var result = await animeMediatorService.SyncAllAsync(forceRefreshKometaMapping);
            
            if (result.IsFailure)
            {
                return StatusCode(500, result.Error.Message);
            }

            return Ok(new { message = "Sync completed successfully" });
        }
    }
}
