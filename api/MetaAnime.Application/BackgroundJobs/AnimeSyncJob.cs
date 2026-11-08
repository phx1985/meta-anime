using Hangfire;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Application.Services;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.BackgroundJobs;

public sealed class AnimeSyncJob(
    IAnimeMatchingService animeMatchingService,
    IAnimeMediatorService animeMediatorService,
    ICacheEntryRepository cacheEntryRepository,
    ISyncProgressNotifier progressNotifier,
    ISyncStateTracker stateTracker,
    ActivityLogService activityLog,
    IBackgroundJobClient backgroundJobClient,
    ILogger<AnimeSyncJob> logger)
{
    /// <summary>
    /// Synchronizes anime metadata from external sources
    /// </summary>
    public async Task SyncAnimeMetadata(bool forceRefreshKometaMapping = false)
    {
        stateTracker.BeginSync();
        logger.LogInformation(Constants.LogMessages.AnimeSyncJob.Starting, DateTime.UtcNow);
        var succeeded = false;
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // Snapshot the provider contents *before* the match rewrites CacheEntries. Diffing the
        // snapshots is what makes the hub show "1 anime added" instead of "+705".
        var beforeSonarr = await SnapshotAsync(AnimeSource.Sonarr);
        var beforeMal = await SnapshotAsync(AnimeSource.MyAnimeList);
        var beforePlex = await SnapshotAsync(AnimeSource.Plex);
        var before = await TryGetCountsAsync();

        try
        {
            var matchResult = await animeMatchingService.MatchAsync(forceRefreshKometaMapping, progressNotifier);

            if (matchResult.IsSuccess)
            {
                var report = matchResult.Value;
                logger.LogInformation(
                    Constants.LogMessages.AnimeSyncJob.Succeeded,
                    DateTime.UtcNow);
                logger.LogInformation(
                    Constants.LogMessages.AnimeSyncJob.MatchSummary,
                    report.ExactIdMappings,
                    report.StrongTitleYearMappings,
                    report.UnmatchedCandidates.Count);

                // Codec enrichment is deferred out of the sync critical path: it runs as an
                // asynchronous follow-up job and only touches entries still missing codec data.
                backgroundJobClient.Enqueue<SonarrCodecEnrichmentJob>(job => job.EnrichAsync());

                // First successful sync: populate the activity mirror immediately instead of
                // waiting for the scheduled interval, otherwise the Activity panel is empty
                // right after onboarding.
                var durationMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                var diffs = BuildDiffs(
                    beforeSonarr, await SnapshotAsync(AnimeSource.Sonarr),
                    beforeMal, await SnapshotAsync(AnimeSource.MyAnimeList),
                    beforePlex, await SnapshotAsync(AnimeSource.Plex));

                // First run against an empty database: everything is "new", but none of it was
                // user action. Record it as a baseline so the card reads "(baseline)", not +2960.
                var isBaseline = before.Sonarr == 0 && before.Mal == 0 && before.Plex == 0;

                if (!isBaseline)
                {
                    await ReconcileRemovalsAsync(diffs.Where(d => d.Action == "removed").ToList());
                }

                var counted = isBaseline ? Array.Empty<SyncDiffItem>() : diffs.ToArray();
                var added = counted.Count(d => d.Action == "added");
                var removed = counted.Count(d => d.Action == "removed");
                var updated = counted.Count(d => d.Action == "updated");

                await activityLog.LogAsync(
                    ActivityType.SyncCompleted,
                    $"Metadata sync · {durationMs} ms",
                    isBaseline
                        ? $"Initial baseline indexed · Sonarr {before.Sonarr} · MAL {before.Mal} · Plex {before.Plex}"
                        : $"{FormatDelta(added, '+')} {FormatDelta(removed, '-')} {FormatDelta(updated, '~')} · Sonarr {before.Sonarr} · MAL {before.Mal} · Plex {before.Plex}",
                    severity: "success",
                    metadata: new
                    {
                        durationMs,
                        isBaseline,
                        added,
                        removed,
                        updated,
                        sonarrTotal = before.Sonarr,
                        malTotal = before.Mal,
                        plexTotal = before.Plex,
                        diffs = counted.Take(60),
                        matched = report.ExactIdMappings + report.StrongTitleYearMappings,
                        unmatched = report.UnmatchedCandidates.Count,
                    });

                if (stateTracker.LastActivitySyncDate is null)
                    backgroundJobClient.Enqueue<DownloadStatsCacheJob>(job => job.CacheDownloadStatsAsync());

                succeeded = true;
            }
            else
            {
                logger.LogError(Constants.LogMessages.AnimeSyncJob.MatchFailed, matchResult.Error.Message);
            }
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            // A concurrent writer touched a row another job already updated ("0 rows affected").
            // One anime must never abort the whole run: log it, tell the UI, and let the next
            // cycle reconcile — the alternative is a job that fails every time it retries.
            logger.LogWarning(ex, "Sync aborted on a concurrency conflict; completed work is kept.");
            await progressNotifier.NotifySyncFailedAsync(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, Constants.LogMessages.AnimeSyncJob.Error);
            await progressNotifier.NotifySyncFailedAsync(ex.Message);
            throw; // re-throw to mark the Hangfire job as failed
        }
        finally
        {
            stateTracker.EndSync(succeeded);
        }
    }

    /// <summary>
    /// EF's <c>DbUpdateConcurrencyException</c> by name — the Application layer has no EF
    /// reference by design, and a type-name check keeps the railway intact without adding one.
    /// </summary>
    private static bool IsConcurrencyConflict(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name == "DbUpdateConcurrencyException") return true;
        }
        return false;
    }

    /// <summary>
    /// Schedules (or reschedules) the anime sync job using the interval configured in the PropertyBag.
    /// </summary>
    public static async Task ScheduleAsync(IRecurringJobManager recurringJobManager, IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();
        var entry = await repo.GetByKeyAsync(SettingsKeys.AppSyncInterval);
        var cronExpr = MapIntervalToCron(entry?.Value ?? "daily");

        recurringJobManager.AddOrUpdate<AnimeSyncJob>(
            Constants.JobNames.AnimeMetadataSync,
            job => job.SyncAnimeMetadata(true),
            cronExpr);
    }

    private static string MapIntervalToCron(string interval) => interval switch
    {
        "hourly" => Cron.Hourly(),
        "every6h" => "0 */6 * * *",
        "every12h" => "0 */12 * * *",
        "weekly" => Cron.Weekly(),
        _ => Cron.Daily(), // "daily" is the default
    };

    private readonly record struct ProviderCounts(int Sonarr, int Mal, int Plex);

    /// <summary>Counts are decorative: a failure must never abort the sync itself.</summary>
    private async Task<ProviderCounts> TryGetCountsAsync()
    {
        try
        {
            var stats = await animeMediatorService.GetStatisticsAsync();
            return stats.IsSuccess
                ? new ProviderCounts(stats.Value.SonarrAnime, stats.Value.MyAnimeListAnime, stats.Value.PlexAnime)
                : new ProviderCounts(0, 0, 0);
        }
        catch
        {
            return new ProviderCounts(0, 0, 0);
        }
    }

    private static string BuildSyncSubtitle(ProviderCounts before, ProviderCounts after) =>
        $"Sonarr {after.Sonarr} ({after.Sonarr - before.Sonarr:+0;-0;0}) · " +
        $"MAL {after.Mal} ({after.Mal - before.Mal:+0;-0;0}) · " +
        $"Plex {after.Plex} ({after.Plex - before.Plex:+0;-0;0})";

    private sealed record CacheSnapshotItem(string SourceId, string Title);

    private sealed record SyncDiffItem(
        string Provider,
        string Action,
        string ExternalId,
        string Title,
        string Reason);

    private static string FormatDelta(int count, char sign) => $"{sign}{count}";

    /// <summary>Titles only — enough to label a diff row without shipping the whole payload.</summary>
    private async Task<Dictionary<string, CacheSnapshotItem>> SnapshotAsync(AnimeSource source)
    {
        try
        {
            var entries = await cacheEntryRepository.GetAsync(source);
            return entries.ToDictionary(
                e => e.SourceId,
                e => new CacheSnapshotItem(e.SourceId, ExtractTitle(e.RawData) ?? e.SourceId));
        }
        catch
        {
            return new Dictionary<string, CacheSnapshotItem>();
        }
    }

    /// <summary>Best-effort title probe across the three provider payload shapes.</summary>
    private static string? ExtractTitle(string rawData)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(rawData);
            var root = document.RootElement;
            foreach (var property in new[] { "title", "shownTitle", "name" })
            {
                if (root.TryGetProperty(property, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return value.GetString();
            }
            if (root.TryGetProperty("Series", out var series) && series.TryGetProperty("title", out var seriesTitle))
                return seriesTitle.GetString();
            if (root.TryGetProperty("grandparentTitle", out var grandParent))
                return grandParent.GetString();
        }
        catch
        {
            // Not JSON / unexpected shape — the SourceId is a fine label.
        }
        return null;
    }

    private static List<SyncDiffItem> BuildDiffs(
        Dictionary<string, CacheSnapshotItem> beforeSonarr, Dictionary<string, CacheSnapshotItem> afterSonarr,
        Dictionary<string, CacheSnapshotItem> beforeMal, Dictionary<string, CacheSnapshotItem> afterMal,
        Dictionary<string, CacheSnapshotItem> beforePlex, Dictionary<string, CacheSnapshotItem> afterPlex)
    {
        var diffs = new List<SyncDiffItem>();
        Collect("sonarr", "Sonarr", beforeSonarr, afterSonarr, diffs);
        Collect("mal", "MyAnimeList", beforeMal, afterMal, diffs);
        Collect("plex", "Plex", beforePlex, afterPlex, diffs);
        return diffs;
    }

    private static void Collect(
        string providerKey,
        string providerLabel,
        Dictionary<string, CacheSnapshotItem> before,
        Dictionary<string, CacheSnapshotItem> after,
        List<SyncDiffItem> diffs)
    {
        foreach (var (id, item) in after)
        {
            if (!before.ContainsKey(id))
                diffs.Add(new SyncDiffItem(providerKey, "added", id, item.Title, $"Added to {providerLabel}"));
        }

        foreach (var (id, item) in before)
        {
            if (!after.ContainsKey(id))
                diffs.Add(new SyncDiffItem(providerKey, "removed", id, item.Title, $"Removed from {providerLabel}"));
        }
    }

    /// <summary>
    /// A removal must undo the matching "added" card: flip it to reverted (so the net KPI drops)
    /// and log the deletion with the same title so the timeline reads as a story.
    /// </summary>
    private async Task ReconcileRemovalsAsync(IReadOnlyList<SyncDiffItem> removals)
    {
        foreach (var removal in removals.Take(25))
        {
            var tvdbId = removal.Provider == "sonarr" && int.TryParse(removal.ExternalId, out var id) ? id : (int?)null;
            var malId = removal.Provider == "mal" && int.TryParse(removal.ExternalId, out var malExternal) ? malExternal : (int?)null;

            await activityLog.RevertAsync(malId, tvdbId);
            await activityLog.LogAsync(
                ActivityType.AnimeRemoved,
                removal.Title,
                removal.Reason,
                malId: malId,
                tvdbId: tvdbId,
                severity: "warning");
        }
    }
}
