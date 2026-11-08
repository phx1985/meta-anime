using Hangfire;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Application.Services;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace MetaAnime.Application.BackgroundJobs;

public sealed class DownloadStatsCacheJob(
    IAnimeMediatorService animeMediatorService,
    IPropertyBagRepository propertyBagRepository,
    ISyncProgressNotifier progressNotifier,
    ActivityLogService activityLog,
    PlexWatchHistoryPoller plexWatchHistoryPoller,
    ICacheEntryRepository cacheEntryRepository,
    ILogger<DownloadStatsCacheJob> logger)
{
    /// <summary>Binary size, matching what the activity tile shows (1 GiB = 1024³).</summary>
    /// <summary>Sonarr timestamps are UTC wall-clock values stored as unspecified DateTimes.</summary>
    private static DateTimeOffset? ToOffset(DateTime value)
        => value == default ? null : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Syncs new Sonarr download events into the local history mirror, then rebuilds the
    /// stats snapshot served by <c>/api/v1/animes/activity-stats</c>.
    /// <para>
    /// The sync is incremental: Sonarr is only asked for events newer than the newest one we
    /// already stored (minus a two-hour overlap). The expensive part of the old implementation
    /// — paging <c>api/v3/history</c>, which sorts Sonarr's whole history table per page — now
    /// happens once, on the cold start, instead of on every cycle.
    /// </para>
    /// </summary>
    public async Task CacheDownloadStatsAsync()
    {
        logger.LogInformation(Constants.LogMessages.DownloadStatsCacheJob.Starting, DateTime.UtcNow);

        // The UI mirrors this flag through SignalR + GET /sync-status, so a refresh mid-run
        // still shows the spinner.
        await progressNotifier.NotifyActivitySyncStartedAsync();
        var succeeded = false;

        try
        {
            // Plex first, and independently of Sonarr: scrobbles must keep flowing when the
            // Sonarr mirror is unreachable (the early return below used to swallow them).
            await plexWatchHistoryPoller.PollAsync();

            var sync = await animeMediatorService.RefreshDownloadHistoryAsync();
            if (sync.IsFailure)
            {
                logger.LogError(Constants.LogMessages.DownloadStatsCacheJob.Error + ": {ErrorMessage}", sync.Error.Message);
                return;
            }

            // Recent events only: the 30-day aggregation stays in the stats cache, NOT in the
            // log. A cold mirror holds thousands of imports and dumping them would bury the page
            // under a "+4858 more episodes imported" card.
            // Sonarr history covers the whole instance, not just the tagged anime library.
            // The cache only holds series this app considers anime (Sonarr:AnimeTags), so a
            // series missing from it must stay out — this is an anime app, not a Plex/Sonarr log.
            var animeSeriesIds = await LoadAnimeSeriesIdsAsync();

            var recent = sync.Value.NewEvents
                .Where(e => e.OccurredAt >= DateTime.UtcNow.AddDays(-7))
                .Where(e => animeSeriesIds.Count == 0 || animeSeriesIds.Contains(e.SeriesId.ToString()))
                .OrderByDescending(e => e.OccurredAt)
                .Take(15)
                .ToList();

            if (recent.Count > 0)
            {
                var posters = await LoadSonarrPostersAsync();

                // Episodes of one series landing together (S01E01 + S01E02) read as one event.
                foreach (var group in recent
                             .GroupBy(e => e.SeriesId)
                             .OrderByDescending(g => g.Max(e => e.OccurredAt)))
                {
                    var episodes = group.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
                    var totalBytes = episodes.Sum(e => e.SizeBytes);
                    var first = episodes[0];
                    var title = string.IsNullOrWhiteSpace(first.SeriesTitle) ? $"Series {first.SeriesId}" : first.SeriesTitle;
                    var poster = posters.GetValueOrDefault(first.SeriesId.ToString());

                    var subtitle = episodes.Count == 1
                        ? $"S{first.SeasonNumber:D2}E{first.EpisodeNumber:D2} · {FormatSize(totalBytes)}"
                        : $"S{first.SeasonNumber:D2}E{first.EpisodeNumber:D2}–E{episodes[^1].EpisodeNumber:D2} ({episodes.Count} episodes) · {FormatSize(totalBytes)}";

                    await activityLog.LogAsync(
                        ActivityType.EpisodeDownloaded,
                        title,
                        subtitle,
                        provider: "sonarr",
                        posterUrl: poster,
                        severity: "success",
                        sonarrSeriesId: first.SeriesId,
                        externalEventKey: $"sonarr-hist-{first.Id}",
                        // Sonarr tells us when the import happened; do not stamp the poll time.
                        occurredAt: ToOffset(group.Max(e => e.OccurredAt)),
                        metadata: new
                        {
                            season = first.SeasonNumber,
                            episodes = episodes.Select(e => e.EpisodeNumber).ToArray(),
                            sizeBytes = totalBytes,
                            sonarrSeriesId = first.SeriesId,
                        });

                    await LogSeasonCompletionAsync(first.SeriesId, title, poster, first.SeasonNumber);
                }
            }

            logger.LogInformation(
                Constants.LogMessages.DownloadStatsCacheJob.Refreshed,
                sync.Value.Fetched,
                sync.Value.SinceUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                sync.Value.Inserted,
                sync.Value.ElapsedMs);

            var result = await animeMediatorService.GetActivityStatsAsync(0);

            if (result.IsFailure)
            {
                logger.LogError(Constants.LogMessages.DownloadStatsCacheJob.Error + ": {ErrorMessage}", result.Error.Message);
                return;
            }

            var json = JsonSerializer.Serialize(result.Value, JsonOptions);
            var lastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"); // ISO 8601 UTC — formatted to local time on the client

            await propertyBagRepository.UpsertAsync(SettingsKeys.DownloadStatsCache, json);
            await propertyBagRepository.UpsertAsync(SettingsKeys.DownloadStatsCacheLastUpdated, lastUpdated);

            logger.LogInformation(Constants.LogMessages.DownloadStatsCacheJob.Succeeded, DateTime.UtcNow);
            succeeded = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, Constants.LogMessages.DownloadStatsCacheJob.Error);
            throw; // re-throw to mark the Hangfire job as failed
        }
        finally
        {
            await progressNotifier.NotifyActivitySyncFinishedAsync(succeeded);
        }
    }

    /// <summary>
    /// Schedules (or reschedules) the download stats cache job using the interval configured in the PropertyBag.
    /// Triggers the job immediately so the cache is populated on startup.
    /// </summary>
    /// <summary>
    /// Applies the user-chosen refresh interval. <paramref name="minutesOverride"/> wins over the
    /// stored setting so the config endpoint can reschedule immediately, without a re-read.
    /// </summary>
    public static async Task ScheduleAsync(
        IRecurringJobManager recurringJobManager,
        IServiceProvider services,
        int? minutesOverride = null)
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();

        var entry = await repo.GetByKeyAsync(SettingsKeys.ActivityRefreshIntervalMinutes);
        var minutes = minutesOverride ?? (int.TryParse(entry?.Value, out var stored) ? stored : 15);

        if (minutes <= 0)
        {
            // "Manual only": drop the recurring job entirely; the Refresh button still works.
            recurringJobManager.RemoveIfExists(Constants.JobNames.DownloadStatsCache);
            return;
        }

        recurringJobManager.AddOrUpdate<DownloadStatsCacheJob>(
            Constants.JobNames.DownloadStatsCache,
            job => job.CacheDownloadStatsAsync(),
            $"*/{minutes} * * * *");

        recurringJobManager.Trigger(Constants.JobNames.DownloadStatsCache);
    }

    /// <summary>Only 0/5/15/30/60 are accepted; anything else falls back to the 15-minute default.</summary>
    public static int NormaliseInterval(int minutes) =>
        minutes is 0 or 5 or 15 or 30 or 60 ? minutes : 15;

    private static string MapIntervalToCron(string interval) => interval switch
    {
        "1h"  => Cron.Hourly(),
        "3h"  => "0 */3 * * *",
        "6h"  => "0 */6 * * *",
        "12h" => "0 */12 * * *",
        _     => Cron.Hourly(), // "1h" is the default
    };

    /// <summary>
    /// A season counts as complete when Sonarr's own statistics say every episode has a file
    /// (<c>episodeFileCount &gt;= totalEpisodeCount</c>). Using Sonarr's counter rather than counting
    /// our mirrored history keeps cold-start imports from claiming completion for a season that
    /// simply arrived in bulk, and <c>ExternalEventKey</c> keeps it to one card per season.
    /// </summary>
    private async Task LogSeasonCompletionAsync(int seriesId, string seriesTitle, string? poster, int seasonNumber)
    {
        try
        {
            var entry = await cacheEntryRepository.GetAsync(AnimeSource.Sonarr, seriesId.ToString());
            if (entry is null) return;

            using var document = JsonDocument.Parse(entry.RawData);
            if (!document.RootElement.TryGetProperty("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array)
                return;

            foreach (var season in seasons.EnumerateArray())
            {
                if (!season.TryGetProperty("seasonNumber", out var number) || !number.TryGetInt32(out var parsed) || parsed != seasonNumber)
                    continue;
                if (!season.TryGetProperty("statistics", out var statistics)) return;

                var total = statistics.TryGetProperty("totalEpisodeCount", out var totalElement) && totalElement.TryGetInt32(out var t) ? t : 0;
                var have = statistics.TryGetProperty("episodeFileCount", out var fileElement) && fileElement.TryGetInt32(out var f) ? f : 0;

                if (total <= 0 || have < total) return;

                await activityLog.LogAsync(
                    ActivityType.SeasonCompleted,
                    seriesTitle,
                    $"Season {seasonNumber} complete ({have}/{total} eps)",
                    posterUrl: poster,
                    severity: "success",
                    sonarrSeriesId: seriesId,
                    externalEventKey: $"sonarr-season-{seriesId}-{seasonNumber}",
                    metadata: new { season = seasonNumber, episodes = total });
                return;
            }
        }
        catch
        {
            // A missing/unparseable cache row only costs us the completion card.
        }
    }

    /// <summary>
    /// Sonarr rows used to render as blank boxes because only the MAL cache was consulted.
    /// Sonarr always stores a TVDB poster URL in the series payload — read it from there.
    /// </summary>
    /// <summary>
    /// Series ids present in the Sonarr cache — i.e. the anime-tagged library this app syncs.
    /// An empty set (cache not built yet) disables the filter rather than hiding everything.
    /// </summary>
    private async Task<HashSet<string>> LoadAnimeSeriesIdsAsync()
    {
        try
        {
            var entries = await cacheEntryRepository.GetAsync(AnimeSource.Sonarr);
            return entries.Select(e => e.SourceId).ToHashSet(StringComparer.Ordinal);
        }
        catch
        {
            return [];
        }
    }

    private async Task<Dictionary<string, string>> LoadSonarrPostersAsync()
    {
        var map = new Dictionary<string, string>();
        try
        {
            foreach (var entry in await cacheEntryRepository.GetAsync(AnimeSource.Sonarr))
            {
                try
                {
                    using var document = JsonDocument.Parse(entry.RawData);
                    var root = document.RootElement;

                    if (root.TryGetProperty("remotePoster", out var remotePoster) &&
                        remotePoster.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(remotePoster.GetString()))
                    {
                        map[entry.SourceId] = remotePoster.GetString()!;
                        continue;
                    }

                    if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var image in images.EnumerateArray())
                        {
                            var coverType = image.TryGetProperty("coverType", out var cover) ? cover.GetString() : null;
                            var url = image.TryGetProperty("remoteUrl", out var remote) ? remote.GetString() : null;
                            if (string.Equals(coverType, "poster", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(url))
                            {
                                map[entry.SourceId] = url!;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Non-JSON row — skip it, the fallback placeholder is fine.
                }
            }
        }
        catch
        {
            // Cache unavailable: cards keep their placeholder rather than failing the job.
        }
        return map;
    }
}
