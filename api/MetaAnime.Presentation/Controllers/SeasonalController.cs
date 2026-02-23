using Asp.Versioning;
using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seasonal")]
public class SeasonalController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<PagedResponse<IEnumerable<SeasonalAnimeResponse>>>> GetSeasonal(
        [FromQuery] int year,
        [FromQuery] string season,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        if (string.IsNullOrWhiteSpace(season))
            return BadRequest("season is required (winter, spring, summer, fall)");

        var validSeasons = new[] { "winter", "spring", "summer", "fall" };
        if (!validSeasons.Contains(season.ToLower()))
            return BadRequest("season must be one of: winter, spring, summer, fall");

        if (year < 1917 || year > DateTime.UtcNow.Year + 1)
            return BadRequest($"year must be between 1917 and {DateTime.UtcNow.Year + 1}");

        var result = await mediator.Send(new GetSeasonalAnimesQuery(year, season.ToLower(), page, pageSize));

        if (result.IsFailure)
            return StatusCode(500, result.Error.Message);

        return Ok(result.Value);
    }
}
