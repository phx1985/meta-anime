using Asp.Versioning;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Infrastructure.Plex.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/plex/auth")]
public sealed class PlexAuthController(
    IPlexAuthService plexAuthService,
    IPlexAnimeSourceService<PlexMedia> plexAnimeSourceService,
    ILogger<PlexAuthController> logger) : ControllerBase
{
    /// <summary>
    /// Creates a Plex pin and returns the auth URL the user should be redirected to.
    /// </summary>
    [HttpPost("initiate")]
    [Authorize]
    public async Task<ActionResult<PlexAuthInitiateResponse>> Initiate()
    {
        var result = await plexAuthService.InitiatePinAsync();

        if (result.IsFailure)
        {
            logger.LogError("Plex OAuth pin creation failed: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        var pin = result.Value;
        return Ok(new PlexAuthInitiateResponse(pin.Id, pin.AuthUrl));
    }

    /// <summary>
    /// Polls plex.tv for the given pin. Returns connected=true once the user has authorized.
    /// </summary>
    [HttpPost("poll/{pinId:int}")]
    [Authorize]
    public async Task<ActionResult<PlexAuthPollResponse>> Poll(int pinId)
    {
        var result = await plexAuthService.PollPinAsync(pinId);

        if (result.IsFailure)
        {
            logger.LogError("Plex pin poll failed for pin {PinId}: {Error}", pinId, result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        if (result.Value)
        {
            logger.LogInformation("Plex OAuth flow completed successfully for pin {PinId}", pinId);
        }

        return Ok(new PlexAuthPollResponse(result.Value));
    }

    /// <summary>Returns whether a Plex auth token is currently stored.</summary>
    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<PlexAuthStatusResponse>> Status()
    {
        var connected = await plexAuthService.IsConnectedAsync();
        return Ok(new PlexAuthStatusResponse(connected));
    }

    /// <summary>Removes the stored Plex auth token, disconnecting the account.</summary>
    [HttpDelete("disconnect")]
    [Authorize]
    public async Task<ActionResult> Disconnect()
    {
        var result = await plexAuthService.DisconnectAsync();

        if (result.IsFailure)
        {
            logger.LogError("Plex disconnect failed: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        logger.LogInformation("Plex account disconnected");
        return NoContent();
    }

    /// <summary>Returns the list of available library sections from the connected Plex server.</summary>
    [HttpGet("libraries")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<PlexLibraryResponse>>> GetLibraries()
    {
        var result = await plexAnimeSourceService.GetLibrariesAsync();

        if (result.IsFailure)
        {
            logger.LogError("Failed to fetch Plex libraries: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        return Ok(result.Value.Select(l => new PlexLibraryResponse(l.Key, l.Title, l.Type)));
    }
}

public sealed record PlexAuthInitiateResponse(int PinId, string AuthUrl);
public sealed record PlexAuthPollResponse(bool Connected);
public sealed record PlexAuthStatusResponse(bool Connected);
public sealed record PlexLibraryResponse(string Key, string Title, string Type);
