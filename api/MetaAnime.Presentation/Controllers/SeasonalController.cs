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

    /// <summary>
    /// "For you" deck: the season ranked against the user's own watched/scored titles.
    /// Shares the 15-minute seasonal cache with <see cref="GetSeasonal"/> — no extra MAL call.
    /// </summary>
    [HttpGet("suggestions")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<SeasonalSuggestionResponse>>> GetSuggestions(
        [FromQuery] int year,
        [FromQuery] string season,
        [FromQuery] int limit = 16,
        [FromQuery] string scope = "current",
        [FromQuery] int? fromYear = null,
        [FromQuery] string? fromSeason = null,
        [FromQuery] int? toYear = null,
        [FromQuery] string? toSeason = null,
        [FromQuery] bool excludeChinese = false,
        [FromQuery] string? excludeGenres = null)
    {
        if (string.IsNullOrWhiteSpace(season))
            return BadRequest(Constants.Messages.SeasonRequired);

        var validSeasons = (string[]) ["winter", "spring", "summer", "fall"];
        if (!validSeasons.Contains(season.ToLower()))
            return BadRequest(Constants.Messages.SeasonInvalid);

        if (year < 1917 || year > DateTime.UtcNow.Year + 1)
            return BadRequest(Constants.Messages.YearInvalid);

        var validScopes = (string[]) ["current", "last2", "last4", "custom"];
        if (!validScopes.Contains(scope.ToLower()))
            return BadRequest("Invalid scope. Expected one of: current, last2, last4, custom.");

        var result = await mediator.Send(new GetSeasonalSuggestionsQuery(
            year, season.ToLower(), limit, scope.ToLower(),
            fromYear, fromSeason?.ToLower(), toYear, toSeason?.ToLower(),
            excludeChinese, excludeGenres));

        if (result.IsFailure)
            return this.ToErrorResult(result);

        return Ok(result.Value);
    }
}
