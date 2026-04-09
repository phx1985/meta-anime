using Asp.Versioning;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/auth")]
public sealed class AuthController(
    IPropertyBagRepository repo,
    IAdminAuthService authService) : ControllerBase
{
    /// <summary>Returns whether admin password protection is currently enabled.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthStatusResponse>> GetStatus()
    {
        var entry = await repo.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
        return Ok(new AuthStatusResponse(!string.IsNullOrWhiteSpace(entry?.Value)));
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

        var hashEntry = await repo.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
        if (string.IsNullOrWhiteSpace(hashEntry?.Value))
            return Unauthorized("Authentication is not configured.");

        if (!authService.VerifyPassword(request.Password, hashEntry.Value))
            return Unauthorized("Invalid password.");

        var secret = await GetOrCreateJwtSecretAsync();
        var lifetime = await GetSessionLifetimeAsync();
        var token = authService.GenerateJwt(secret, lifetime);
        return Ok(new LoginResponse(token));
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

        var hashEntry = await repo.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
        var existingHash = hashEntry?.Value;

        // If a password already exists, verify it first.
        if (!string.IsNullOrWhiteSpace(existingHash))
        {
            if (!authService.VerifyPassword(request.CurrentPassword, existingHash))
                return Unauthorized("Current password is incorrect.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest("New password cannot be empty.");

        var newHash = authService.HashPassword(request.NewPassword);
        await repo.UpsertAsync(SettingsKeys.AdminPasswordHash, newHash);
        return NoContent();
    }

    /// <summary>
    /// Sets (or overwrites) the admin password during the local setup wizard.
    /// This endpoint is always anonymous — it is intended exclusively for wizard use.
    /// Returns a signed JWT so subsequent wizard steps can authenticate immediately.
    /// </summary>
    [HttpPost("setup-password")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> SetupPassword([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Password cannot be empty.");

        var hash = authService.HashPassword(request.Password);
        await repo.UpsertAsync(SettingsKeys.AdminPasswordHash, hash);

        var secret = await GetOrCreateJwtSecretAsync();
        var lifetime = await GetSessionLifetimeAsync();
        var token = authService.GenerateJwt(secret, lifetime);
        return Ok(new LoginResponse(token));
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

        var hashEntry = await repo.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
        if (string.IsNullOrWhiteSpace(hashEntry?.Value))
            return BadRequest("Authentication is not currently enabled.");

        if (!authService.VerifyPassword(request.CurrentPassword, hashEntry.Value))
            return Unauthorized("Current password is incorrect.");

        await repo.DeleteAsync(SettingsKeys.AdminPasswordHash);
        return NoContent();
    }

    private async Task<string> GetOrCreateJwtSecretAsync()
    {
        var existing = await repo.GetByKeyAsync(SettingsKeys.JwtSecret);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Value))
            return existing.Value;

        // Generate a cryptographically-random 256-bit secret on first use.
        var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await repo.UpsertAsync(SettingsKeys.JwtSecret, secret);
        return secret;
    }

    private async Task<TimeSpan> GetSessionLifetimeAsync()
    {
        var entry = await repo.GetByKeyAsync(SettingsKeys.SessionLifetimeDays);
        if (int.TryParse(entry?.Value, out var days) && days >= 1 && days <= 365)
            return TimeSpan.FromDays(days);
        return TimeSpan.FromDays(7);
    }
}
