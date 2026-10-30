using Asp.Versioning;
using Mediator;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Infrastructure.MyAnimeList;
using MetaAnime.Utilities.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MalConstants = MetaAnime.Infrastructure.MyAnimeList.Constants;
using PresentationConstants = MetaAnime.Presentation.Constants;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/mal")]
public sealed class MalController(
    IMediator mediator,
    IAnimeMediatorService animeMediatorService,
    IMalTokenService malTokenService,
    ILogger<MalController> logger) : ControllerBase
{
    private static readonly string[] ValidListStatuses =
        ["watching", "completed", "on_hold", "dropped", "plan_to_watch"];

    /// <summary>Returns the franchise constellation (related anime) for the given MAL anime ID.</summary>
    [HttpGet("franchise/{malId}")]
    [Authorize]
    public async Task<ActionResult> GetFranchiseConstellation(string malId)
    {
        if (string.IsNullOrWhiteSpace(malId))
            return BadRequest(PresentationConstants.Messages.MalAnimeIdRequired);

        try
        {
            var result = await animeMediatorService.GetFranchiseConstellationAsync(malId);
            if (result.IsFailure)
            {
                logger.LogError("Franchise constellation fetch failed for anime '{MalId}': {Error}", malId, result.Error.Message);
                return this.ToErrorResult(result);
            }

            return Ok(result.Value);
        }
        catch (MalAuthenticationException ex)
        {
            logger.LogWarning("MAL authentication failure when fetching franchise for '{MalId}': {Message}", malId, ex.Message);
            return Unauthorized(new { code = "MAL.AuthenticationFailed", message = ex.Message });
        }
    }

    /// <summary>Adds or updates an anime entry in the authenticated user's MAL list.</summary>
    [HttpPut("list/{animeId}")]
    [Authorize]
    public async Task<ActionResult> UpdateListStatus(
        string animeId,
        [FromBody] MalListStatusRequest request,
        [FromQuery] string? sourceAniDbId = null)
    {
        if (string.IsNullOrWhiteSpace(animeId))
            return BadRequest(PresentationConstants.Messages.MalAnimeIdRequired);
        if (!ValidListStatuses.Contains(request.Status))
            return BadRequest(PresentationConstants.Messages.MalStatusInvalid);

        try
        {
            var result = await animeMediatorService.UpdateMalListStatusAsync(
                animeId, request.Status, request.NumWatchedEpisodes, request.Score, sourceAniDbId);

            if (result.IsFailure)
            {
                logger.LogError("MAL list update failed for anime '{AnimeId}': {Error}", animeId, result.Error.Message);
                return this.ToErrorResult(result);
            }
        }
        catch (MalAuthenticationException ex)
        {
            logger.LogWarning("MAL authentication failure when updating list status for '{AnimeId}': {Message}", animeId, ex.Message);
            return Unauthorized(new { code = "MAL.AuthenticationFailed", message = ex.Message });
        }

        return NoContent();
    }

    /// <summary>Attempts to refresh the MAL access token using the stored refresh token.</summary>
    [HttpPost("token/refresh")]
    [Authorize]
    public async Task<ActionResult> RefreshToken()
    {
        var result = await malTokenService.RefreshTokenAsync();
        if (result.IsFailure)
        {
            logger.LogError("MAL token refresh failed: {Error}", result.Error.Message);
            return this.ToErrorResult(result);
        }

        return NoContent();
    }

    /// <summary>Searches MAL for anime matching the given query (used for manual gap mapping).</summary>
    [HttpGet("search")]
    [Authorize]
    public async Task<ActionResult> SearchAnime([FromQuery] string q, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query parameter 'q' is required.");

        try
        {
            var result = await animeMediatorService.SearchMalAnimeAsync(q, limit);
            if (result.IsFailure)
            {
                if (result.Error.Code != MalConstants.Errors.InvalidSearchQuery)
                    logger.LogError("MAL anime search failed for query '{Query}': {Error}", q, result.Error.Message);
                return this.ToErrorResult(result);
            }
            return Ok(result.Value);
        }
        catch (MalAuthenticationException ex)
        {
            logger.LogWarning("MAL authentication failure when searching for '{Query}': {Message}", q, ex.Message);
            return Unauthorized(new { code = "MAL.AuthenticationFailed", message = ex.Message });
        }
    }

    /// <summary>Searches MAL for anime by title with full fields — returns SeasonalAnimeResponse suitable for the search page.</summary>
    [HttpGet("anime-search")]
    [Authorize]
    public async Task<ActionResult> SearchAnimeWithDetails([FromQuery] string q, [FromQuery] int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query parameter 'q' is required.");
        if (q.Trim().Length < 3)
            return BadRequest(PresentationConstants.Messages.MalSearchQueryTooShort);

        try
        {
            var result = await animeMediatorService.SearchMalAnimeWithDetailsAsync(q.Trim(), limit);
            if (result.IsFailure)
            {
                // The raw query can be long enough but still too short once MAL-hostile characters are stripped.
                if (result.Error.Code != MalConstants.Errors.InvalidSearchQuery)
                    logger.LogError("MAL anime search (details) failed for query '{Query}': {Error}", q, result.Error.Message);
                return this.ToErrorResult(result);
            }
            return Ok(result.Value);
        }
        catch (MalAuthenticationException ex)
        {
            logger.LogWarning("MAL authentication failure when searching (details) for '{Query}': {Message}", q, ex.Message);
            return Unauthorized(new { code = "MAL.AuthenticationFailed", message = ex.Message });
        }
    }

    /// <summary>Returns all user-confirmed AniDbId cross-links, enriched with titles.</summary>
    [HttpGet("crosslinks")]
    [Authorize]
    public async Task<ActionResult> GetCrossLinks()
    {
        var result = await mediator.Send(new GetCrossLinksQuery());
        if (result.IsFailure)
            return this.ToErrorResult(result);

        return Ok(result.Value);
    }

    /// <summary>Removes a user-confirmed AniDbId cross-link and its bridge mapping.</summary>
    [HttpDelete("crosslink")]
    [Authorize]
    public async Task<ActionResult> RemoveCrossLink(
        [FromQuery] string sourceAniDbId,
        [FromQuery] string malId)
    {
        if (string.IsNullOrWhiteSpace(sourceAniDbId) || string.IsNullOrWhiteSpace(malId))
            return BadRequest("Both sourceAniDbId and malId are required.");

        var result = await mediator.Send(new RemoveCrossLinkCommand(sourceAniDbId, malId));
        if (result.IsFailure)
            return this.ToErrorResult(result);

        return NoContent();
    }
}

public sealed record MalListStatusRequest(string Status, int NumWatchedEpisodes, int Score);
