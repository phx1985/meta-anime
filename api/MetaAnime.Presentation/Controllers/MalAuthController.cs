using Asp.Versioning;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/mal/auth")]
public sealed class MalAuthController(
    IMalOAuthService malOAuthService,
    ILogger<MalAuthController> logger) : ControllerBase
{
    /// <summary>
    /// Returns the OAuth authorization URL the user should be redirected to.
    /// </summary>
    [HttpGet("initiate")]
    [Authorize]
    public async Task<ActionResult<MalAuthInitiateResponse>> Initiate()
    {
        var redirectUri = BuildCallbackUri();
        var result = await malOAuthService.GetAuthorizationUrlAsync(redirectUri);

        if (result.IsFailure)
        {
            logger.LogError("MAL OAuth initiation failed: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        return Ok(new MalAuthInitiateResponse(result.Value));
    }

    /// <summary>
    /// OAuth callback — exchanges the authorization code for tokens and redirects to the settings page.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<ActionResult> Callback([FromQuery] string code, [FromQuery] string state)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            logger.LogWarning("MAL OAuth callback received with missing code or state");
            return Redirect("/settings?malError=invalid_callback");
        }

        var redirectUri = BuildCallbackUri();
        var result = await malOAuthService.ExchangeCodeAsync(code, state, redirectUri);

        if (result.IsFailure)
        {
            logger.LogError("MAL OAuth code exchange failed: {Error}", result.Error.Message);
            return Redirect("/settings?malError=exchange_failed");
        }

        logger.LogInformation("MAL OAuth flow completed successfully");
        return Redirect("/settings?malConnected=true");
    }

    /// <summary>Returns whether a MAL access token is currently stored.</summary>
    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<MalAuthStatusResponse>> Status()
    {
        var connected = await malOAuthService.IsConnectedAsync();
        return Ok(new MalAuthStatusResponse(connected));
    }

    /// <summary>Removes the stored MAL tokens, disconnecting the account.</summary>
    [HttpDelete("disconnect")]
    [Authorize]
    public async Task<ActionResult> Disconnect()
    {
        var result = await malOAuthService.DisconnectAsync();

        if (result.IsFailure)
        {
            logger.LogError("MAL disconnect failed: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        logger.LogInformation("MAL account disconnected");
        return NoContent();
    }

    private string BuildCallbackUri()
    {
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}/api/v1/mal/auth/callback";
    }
}

public sealed record MalAuthInitiateResponse(string AuthorizationUrl);
public sealed record MalAuthStatusResponse(bool Connected);
