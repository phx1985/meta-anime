using Asp.Versioning;
using MetaAnime.Application.Interfaces;
using MetaAnime.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/mal")]
public sealed class MalController(
    IAnimeMediatorService animeMediatorService,
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
            return BadRequest(Constants.Messages.MalAnimeIdRequired);

        if (!ValidListStatuses.Contains(request.Status))
            return BadRequest(Constants.Messages.MalStatusInvalid);

        var result = await animeMediatorService.UpdateMalListStatusAsync(
            animeId, request.Status, request.NumWatchedEpisodes, request.Score);

        if (result.IsFailure)
        {
            logger.LogError("MAL list update failed for anime '{AnimeId}': {Error}", animeId, result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        return NoContent();
    }
}

public sealed record MalListStatusRequest(string Status, int NumWatchedEpisodes, int Score);
