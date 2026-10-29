using Asp.Versioning;
using Hangfire;
using Mediator;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/settings")]
public sealed class SettingsController(
    IMediator mediator,
    IRecurringJobManager recurringJobManager,
    IAppConfigBackupService configBackupService,
    IPropertyBagRepository propertyBagRepository,
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
            return StatusCode(500, result.Error.Message);

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
            return StatusCode(500, result.Error.Message);

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

        var result = await mediator.Send(new UpsertPropertyBagCommand(key, request));

        if (result.IsFailure)
            return StatusCode(500, result.Error.Message);

        // Re-schedule the sync job whenever the sync interval setting changes
        if (key == SettingsKeys.AppSyncInterval)
        {
            await AnimeSyncJob.ScheduleAsync(recurringJobManager, HttpContext.RequestServices);
        }
        else if (key == SettingsKeys.DownloadStatsCacheInterval)
        {
            await DownloadStatsCacheJob.ScheduleAsync(recurringJobManager, HttpContext.RequestServices);
        }

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
        var hashEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
        var requiresAuth = !string.IsNullOrWhiteSpace(hashEntry?.Value);
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
