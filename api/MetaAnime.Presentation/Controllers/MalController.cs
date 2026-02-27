using Asp.Versioning;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Infrastructure.MyAnimeList;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PresentationConstants = MetaAnime.Presentation.Constants;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/mal")]
public sealed class MalController(
    IAnimeMediatorService animeMediatorService,
    IMalTokenService malTokenService,
    ILogger<MalController> logger) : ControllerBase
{
    private static readonly string[] ValidListStatuses =
        ["watching", "completed", "on_hold", "dropped", "plan_to_watch"];

    /// <summary>Adds or updates an anime entry in the authenticated user's MAL list.</summary>
    [HttpPut("list/{animeId}")]
    [Authorize]
    public async Task<ActionResult> UpdateListStatus(
        string animeId,
        [FromBody] MalListStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(animeId))
            return BadRequest(PresentationConstants.Messages.MalAnimeIdRequired);

        if (!ValidListStatuses.Contains(request.Status))
            return BadRequest(PresentationConstants.Messages.MalStatusInvalid);

        try
        {
            var result = await animeMediatorService.UpdateMalListStatusAsync(
                animeId, request.Status, request.NumWatchedEpisodes, request.Score);

            if (result.IsFailure)
            {
                logger.LogError("MAL list update failed for anime '{AnimeId}': {Error}", animeId, result.Error.Message);
                return StatusCode(500, result.Error.Message);
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
            return StatusCode(500, result.Error.Message);
        }

        return NoContent();
    }
}

public sealed record MalListStatusRequest(string Status, int NumWatchedEpisodes, int Score);
