using Asp.Versioning;
using Mediator;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Utilities.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/settings")]
public sealed class SettingsController(
    IMediator mediator,
    IAppConfigBackupService configBackupService,
    IPlexAuthService plexAuthService,
    IMalTokenService malTokenService,
    IAnimeMediatorService animeMediatorService) : ControllerBase
{
    /// <summary>
    /// Returns all settings definitions with their current values.
    /// Sensitive fields are masked.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<SettingDefinition>>> GetAll()
    {
        var result = await mediator.Send(new GetSettingsQuery());
        if (result.IsFailure)
            return this.ToErrorResult(result);

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns the date/time of the last successful sync, or null if never synced.
    /// </summary>
    [HttpGet("sync-status")]
    [Authorize]
    public async Task<ActionResult<SyncStatusResponse>> GetSyncStatus()
    {
        var result = await mediator.Send(new GetSyncStatusQuery());
        if (result.IsFailure)
            return this.ToErrorResult(result);

        return Ok(result.Value);
    }

    /// <summary>
    /// Updates a setting value in the PropertyBag.
    /// </summary>
    [HttpPut("{key}")]
    [Authorize]
    public async Task<ActionResult<PropertyBagResponse>> Update(string key, [FromBody] UpsertPropertyBagRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // The handler performs the write-allowlist enforcement and re-schedules the
        // background jobs when an interval setting changes.
        var result = await mediator.Send(new UpdateSettingCommand(key, request));
        if (result.IsFailure)
            return this.ToErrorResult(result);

        return Ok(result.Value);
    }

    private static readonly JsonSerializerOptions BackupJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Exports the full portable configuration (UNMASKED secrets — handle the
    /// file securely). Admin password hash / JWT secrets are excluded.
    /// </summary>
    [HttpGet("export")]
    [Authorize]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var backup = await configBackupService.ExportAsync(cancellationToken);
        var json = JsonSerializer.Serialize(backup, BackupJson);
        var name = $"meta-anime-config-{DateTime.UtcNow:yyyy-MM-dd}.json";
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", name);
    }

    /// <summary>
    /// Imports a configuration backup. Anonymous access is allowed ONLY while
    /// first-run setup is incomplete (no admin password configured); otherwise a
    /// valid admin JWT is required. Sensitive values are re-encrypted at rest.
    /// Returns fresh provider connectivity so the wizard/UI can prefill state.
    /// </summary>
    [HttpPost("import")]
    [AllowAnonymous]
    public async Task<ActionResult<AppConfigImportResponse>> Import(
        [FromBody] AppConfigBackupDto backup,
        CancellationToken cancellationToken)
    {
        var authStatus = await mediator.Send(new GetAuthStatusQuery());
        if (authStatus.IsFailure)
            return this.ToErrorResult(authStatus);

        var requiresAuth = authStatus.Value.RequiresAuth;
        if (requiresAuth && User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        if (backup.ConfigVersion is > 1)
            return BadRequest(new { error = $"Unsupported config version {backup.ConfigVersion}." });

        var written = await configBackupService.ImportAsync(backup, cancellationToken);

        // Fresh connectivity snapshot so the client can react immediately.
        var plexConnected = await plexAuthService.IsConnectedAsync();
        var malConnected = await malTokenService.IsConnectedAsync();
        var sonarrResult = await animeMediatorService.GetSonarrOptionsAsync();
        var sonarrConnected = sonarrResult.IsSuccess;

        return Ok(new AppConfigImportResponse(written, plexConnected, malConnected, sonarrConnected, requiresAuth));
    }
}
