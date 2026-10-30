using Asp.Versioning;
using Mediator;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using MetaAnime.Core.Settings;
using MetaAnime.Utilities.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/auth")]
public sealed class AuthController(
    IMediator mediator,
    IOptions<AuthenticationSettings> authSettings) : ControllerBase
{
    /// <summary>Returns whether admin password protection is currently enabled.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthStatusResponse>> GetStatus()
    {
        var result = await mediator.Send(new GetAuthStatusQuery());
        if (result.IsFailure)
            return this.ToErrorResult(result);

        return Ok(result.Value);
    }

    /// <summary>
    /// Authenticates with the admin password and returns a signed JWT.
    /// Returns 401 when the password is wrong or auth is not configured.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await mediator.Send(new LoginCommand(request.Password));
        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                MetaAnime.Application.Constants.Errors.AuthNotConfigured => Unauthorized("Authentication is not configured."),
                MetaAnime.Application.Constants.Errors.AuthInvalidPassword => Unauthorized("Invalid password."),
                _ => this.ToErrorResult(result),
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Sets or replaces the admin password. Requires the current password when one exists.
    /// Calling this endpoint when no password is set creates the password (first-time setup).
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await mediator.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword));
        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                MetaAnime.Application.Constants.Errors.AuthInvalidPassword => Unauthorized("Current password is incorrect."),
                MetaAnime.Application.Constants.Errors.AuthEmptyPassword => BadRequest("New password cannot be empty."),
                _ => this.ToErrorResult(result),
            };
        }

        return NoContent();
    }

    /// <summary>
    /// Sets the admin password during the local setup wizard. First-run only:
    /// once a password exists this endpoint always rejects (409), so an anonymous
    /// caller can never overwrite an existing password.
    /// Returns a signed JWT so subsequent wizard steps can authenticate immediately.
    /// </summary>
    [HttpPost("setup-password")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> SetupPassword([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await mediator.Send(new SetupPasswordCommand(request.Password));
        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                MetaAnime.Application.Constants.Errors.AuthPasswordAlreadySet => this.ToErrorResult(result),
                MetaAnime.Application.Constants.Errors.AuthEmptyPassword => BadRequest("Password cannot be empty."),
                _ => this.ToErrorResult(result),
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Removes the admin password, returning the application to public (no-auth) mode.
    /// Requires the current password for confirmation.
    /// </summary>
    [HttpPost("disable-auth")]
    [Authorize]
    public async Task<IActionResult> DisableAuth([FromBody] DisableAuthRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await mediator.Send(new DisableAuthCommand(request.CurrentPassword));
        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                MetaAnime.Application.Constants.Errors.AuthNotEnabled => BadRequest("Authentication is not currently enabled."),
                MetaAnime.Application.Constants.Errors.AuthInvalidPassword => Unauthorized("Current password is incorrect."),
                _ => this.ToErrorResult(result),
            };
        }

        return NoContent();
    }

    /// <summary>
    /// Returns the runtime API key for authenticated clients. This is the ONLY way
    /// to obtain the key once an admin password exists (the unauthenticated
    /// /assets/config.json route never returns it in protected mode).
    /// </summary>
    [HttpGet("runtime-key")]
    [Authorize]
    public ActionResult<RuntimeKeyResponse> GetRuntimeKey()
    {
        return Ok(new RuntimeKeyResponse(authSettings.Value.ApiKey));
    }
}
