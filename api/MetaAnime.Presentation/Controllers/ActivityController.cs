using Hangfire;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Services;
using MetaAnime.Application.Utilities;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Presentation.Controllers;

/// <summary>
/// Activity Hub feed: an append-only log of adds, downloads, completions and sync diffs.
/// </summary>
[ApiController]
[Route("api/v1/activity")]
[Authorize]
public sealed class ActivityController(
    ActivityLogService activityLog,
    ISonarrHistoryEventRepository sonarrHistory,
    IPropertyBagRepository propertyBag,
    IRecurringJobManager recurringJobManager,
    IBackgroundJobClient backgroundJobClient,
    IServiceProvider services) : ControllerBase
{
    /// <summary>
    /// Tolerant alias table: a tab may cover more than one stored type ("adds" is add + remove).
    /// Unknown values fall back to "all" instead of 400 — a bad filter must never break the page.
    /// </summary>
    private static readonly Dictionary<string, ActivityType[]> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["adds"] = [ActivityType.AnimeAdded, ActivityType.AnimeRemoved],
        ["added"] = [ActivityType.AnimeAdded, ActivityType.AnimeRemoved],
        ["animeadded"] = [ActivityType.AnimeAdded],
        ["removed"] = [ActivityType.AnimeRemoved],
        ["episodes"] = [ActivityType.EpisodeDownloaded],
        ["ingested"] = [ActivityType.EpisodeDownloaded],
        ["episodedownloaded"] = [ActivityType.EpisodeDownloaded],
        ["watched"] = [ActivityType.EpisodeWatched],
        ["plex"] = [ActivityType.EpisodeWatched],
        ["episodewatched"] = [ActivityType.EpisodeWatched],
        ["completions"] = [ActivityType.SeasonCompleted],
        ["seasons"] = [ActivityType.SeasonCompleted],
        ["seasoncompleted"] = [ActivityType.SeasonCompleted],
        ["syncs"] = [ActivityType.SyncCompleted],
        ["sync"] = [ActivityType.SyncCompleted],
        ["synccompleted"] = [ActivityType.SyncCompleted],
    };

    [HttpGet("feed")]
    public async Task<ActionResult<ActivityFeedResponse>> GetFeed(
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? period = null,
        [FromQuery] int tzOffset = 0,
        [FromQuery] int? year = null,
        CancellationToken cancellationToken = default)
    {
        ActivityType[]? filter = null;
        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("all", StringComparison.OrdinalIgnoreCase))
            filter = TypeAliases.TryGetValue(type, out var parsed) ? parsed : null;

        // The timeline follows the global range pills; "all"/missing means no time filter.
        DateTimeOffset? from = null;
        if (!string.IsNullOrWhiteSpace(period) && !period.Equals("all", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<ActivityStatsPeriod>(period, ignoreCase: true, out var parsedPeriod) &&
            period.Equals(parsedPeriod.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var startUtc = ActivityStatsCalculator.PeriodStartUtc(parsedPeriod, DateTime.UtcNow, tzOffset, year);
            from = new DateTimeOffset(startUtc, TimeSpan.Zero);
        }

        return Ok(await activityLog.GetFeedAsync(filter, page, pageSize, search, from, cancellationToken));
    }

    public sealed record ActivityConfigResponse(int IntervalMinutes, DateTimeOffset? LastRunAt, bool PlexConfigured);
    public sealed record ActivityConfigRequest(int IntervalMinutes);

    /// <summary>Current activity-polling cadence (0 = manual only).</summary>
    [HttpGet("config")]
    public async Task<ActionResult<ActivityConfigResponse>> GetConfig(CancellationToken cancellationToken)
    {
        var entry = await propertyBag.GetByKeyAsync(SettingsKeys.ActivityRefreshIntervalMinutes);
        var minutes = DownloadStatsCacheJob.NormaliseInterval(
            int.TryParse(entry?.Value, out var parsed) ? parsed : 15);

        var lastRunEntry = await propertyBag.GetByKeyAsync(SettingsKeys.DownloadStatsCacheLastUpdated);
        DateTimeOffset? lastRun = DateTimeOffset.TryParse(lastRunEntry?.Value, out var lastRunAt) ? lastRunAt : null;

        return Ok(await BuildConfigAsync(minutes, lastRun));
    }

    /// <summary>
    /// The "Watched on Plex" tile can only ever move when a server is wired up: say so in the
    /// payload instead of leaving a dead zero that looks like a broken binding.
    /// </summary>
    private async Task<ActivityConfigResponse> BuildConfigAsync(int minutes, DateTimeOffset? lastRun)
    {
        var plexHost = (await propertyBag.GetByKeyAsync(SettingsKeys.PlexHost))?.Value;
        var plexToken = (await propertyBag.GetByKeyAsync(SettingsKeys.PlexToken))?.Value;
        var plexConfigured = !string.IsNullOrWhiteSpace(plexHost) && !string.IsNullOrWhiteSpace(plexToken);

        return new ActivityConfigResponse(minutes, lastRun, plexConfigured);
    }

    /// <summary>
    /// Kicks the activity backfill (Sonarr history + Plex scrobbles) right now — used by the
    /// "never imported" prompt on the hub, so a cold install does not sit on an empty feed.
    /// </summary>
    [HttpPost("refresh")]
    public IActionResult Refresh()
    {
        backgroundJobClient.Enqueue<DownloadStatsCacheJob>(job => job.CacheDownloadStatsAsync());
        return Accepted();
    }

    /// <summary>Changes the cadence and reschedules the recurring job immediately.</summary>
    [HttpPut("config")]
    public async Task<ActionResult<ActivityConfigResponse>> UpdateConfig(
        [FromBody] ActivityConfigRequest request,
        CancellationToken cancellationToken)
    {
        var minutes = DownloadStatsCacheJob.NormaliseInterval(request.IntervalMinutes);
        await propertyBag.UpsertAsync(SettingsKeys.ActivityRefreshIntervalMinutes, minutes.ToString());

        await DownloadStatsCacheJob.ScheduleAsync(recurringJobManager, services, minutes);

        return Ok(await BuildConfigAsync(minutes, null));
    }

    /// <summary>
    /// Years the hub can chart: everything present in the activity log or the download mirror,
    /// newest first. The year picker next to the "Year" pill is built from this.
    /// </summary>
    [HttpGet("years")]
    public async Task<ActionResult<IReadOnlyList<int>>> GetYears(CancellationToken cancellationToken)
    {
        var years = new SortedSet<int> { DateTime.UtcNow.Year };

        try
        {
            var earliest = await sonarrHistory.GetEarliestOccurredAtAsync(cancellationToken);
            if (earliest is not null)
            {
                for (var year = earliest.Value.Year; year <= DateTime.UtcNow.Year; year++) years.Add(year);
            }
        }
        catch { /* mirror unavailable → current year only */ }

        return Ok(years.Reverse().ToList());
    }

    /// <summary>
    /// Multi-series trend for the hub chart (ingested / added / watched / syncs) over the same
    /// calendar buckets as the range pills.
    /// </summary>
    [HttpGet("trend")]
    public async Task<ActionResult<ActivityTrendResponse>> GetTrend(
        [FromQuery] string? period = null,
        [FromQuery] int tzOffset = 0,
        [FromQuery] int? year = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = ActivityStatsPeriod.ThisWeek;
        if (!string.IsNullOrWhiteSpace(period) &&
            Enum.TryParse<ActivityStatsPeriod>(period, ignoreCase: true, out var candidate))
        {
            parsed = candidate;
        }

        return Ok(await activityLog.GetTrendAsync(parsed, tzOffset, year, cancellationToken));
    }

    /// <summary>
    /// Net KPI counters (added − removed, ingested, watched…) for the hub tiles.
    /// <paramref name="period"/> (today|thisWeek|thisMonth|thisYear) pins the window to the same
    /// calendar boundary as the trend chart; without it the rolling <paramref name="days"/> window is used.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ActivitySummaryResponse>> GetSummary(
        [FromQuery] int days = 30,
        [FromQuery] string? period = null,
        [FromQuery] int tzOffset = 0,
        [FromQuery] int? year = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(period) &&
            Enum.TryParse<ActivityStatsPeriod>(period, ignoreCase: true, out var parsed) &&
            period.Equals(parsed.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Ok(await activityLog.GetSummaryAsync(parsed, tzOffset, year, cancellationToken));
        }

        return Ok(await activityLog.GetSummaryAsync(days, cancellationToken));
    }
}
