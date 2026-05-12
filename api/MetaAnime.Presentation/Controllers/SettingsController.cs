using Asp.Versioning;
using Hangfire;
using Mediator;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using MetaAnime.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/settings")]
public sealed class SettingsController(IMediator mediator, IRecurringJobManager recurringJobManager) : ControllerBase
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
}
