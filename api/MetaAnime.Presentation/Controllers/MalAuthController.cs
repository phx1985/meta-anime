using Asp.Versioning;
using Mediator;
using MetaAnime.Application.Queries;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Utilities.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/mal/auth")]
public sealed class MalAuthController(
    IMediator mediator,
    IMalOAuthService malOAuthService,
    ILogger<MalAuthController> logger) : ControllerBase
{
    /// <summary>
    /// Returns the OAuth authorization URL the user should be redirected to.
    /// The caller must supply the redirect URI (the Angular SPA route that will handle the callback).
    /// </summary>
    [HttpGet("initiate")]
    [Authorize]
    public async Task<ActionResult<MalAuthInitiateResponse>> Initiate([FromQuery] string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            return BadRequest("redirectUri query parameter is required.");

        var result = await malOAuthService.GetAuthorizationUrlAsync(redirectUri);

        if (result.IsFailure)
        {
            logger.LogError("MAL OAuth initiation failed: {Error}", result.Error.Message);
            return this.ToErrorResult(result);
        }

        return Ok(new MalAuthInitiateResponse(result.Value));
    }

    /// <summary>
    /// SPA callback — the Angular frontend posts the code and state here after MAL redirects
    /// back to the frontend URL.  The backend exchanges the code for tokens and returns the result.
    /// </summary>
    [HttpPost("callback")]
    [Authorize]
    public async Task<ActionResult<MalCallbackResponse>> Callback([FromBody] MalCallbackRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.State))
        {
            logger.LogWarning("MAL OAuth SPA callback received with missing code or state");
            return BadRequest("code and state are required.");
        }

        var redirectUriResult = await mediator.Send(new GetMalOAuthRedirectUriQuery());
        if (redirectUriResult.IsFailure)
        {
            logger.LogError("MAL OAuth SPA callback: failed to read stored redirectUri: {Error}", redirectUriResult.Error.Message);
            return StatusCode(500, "OAuth session expired. Please restart the authorization flow.");
        }

        var redirectUri = redirectUriResult.Value;
        if (string.IsNullOrWhiteSpace(redirectUri) ||
            !Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttps && parsedUri.Scheme != Uri.UriSchemeHttp))
        {
            logger.LogError("MAL OAuth SPA callback: stored redirectUri not found or invalid");
            return StatusCode(500, "OAuth session expired. Please restart the authorization flow.");
        }

        var result = await malOAuthService.ExchangeCodeAsync(body.Code, body.State, redirectUri);

        if (result.IsFailure)
        {
            logger.LogError("MAL OAuth code exchange failed: {Error}", result.Error.Message);
            return StatusCode(500, "Authorization failed. Please try connecting again.");
        }

        logger.LogInformation("MAL OAuth flow completed successfully");
        return Ok(new MalCallbackResponse(true));
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
            return this.ToErrorResult(result);
        }

        logger.LogInformation("MAL account disconnected");
        return NoContent();
    }
}

public sealed record MalAuthInitiateResponse(string AuthorizationUrl);
public sealed record MalAuthStatusResponse(bool Connected);
public sealed record MalCallbackRequest(string Code, string State);
public sealed record MalCallbackResponse(bool Connected);
