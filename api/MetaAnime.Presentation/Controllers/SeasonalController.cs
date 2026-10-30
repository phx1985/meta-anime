using Asp.Versioning;
using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using MetaAnime.Presentation;
using MetaAnime.Utilities.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/seasonal")]
public sealed class SeasonalController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<SeasonalAnimeResponse>>> GetSeasonal(
        [FromQuery] int year,
        [FromQuery] string season)
    {
        if (string.IsNullOrWhiteSpace(season))
            return BadRequest(Constants.Messages.SeasonRequired);

        var validSeasons = (string[]) ["winter", "spring", "summer", "fall"];
        if (!validSeasons.Contains(season.ToLower()))
            return BadRequest(Constants.Messages.SeasonInvalid);

        if (year < 1917 || year > DateTime.UtcNow.Year + 1)
            return BadRequest(Constants.Messages.YearInvalid);

        var result = await mediator.Send(new GetSeasonalAnimesQuery(year, season.ToLower()));

        if (result.IsFailure)
            return this.ToErrorResult(result);

        return Ok(result.Value);
    }
}
