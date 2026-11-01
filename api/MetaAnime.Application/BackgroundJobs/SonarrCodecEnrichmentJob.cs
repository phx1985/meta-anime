using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MetaAnime.Application.BackgroundJobs;

/// <summary>
/// Asynchronous codec enrichment: fills the <c>metaCodecDistribution</c> of Sonarr cache
/// entries that lost (or never had) codec data, so codec fetching never blocks the sync
/// critical path. The sync carries existing codec data over for series whose files did not
/// change (keyed by series id + <c>added</c> + statistics). Only rows without codec data are
/// fetched here, series with zero episode files are skipped entirely (no codec is possible
/// for them — otherwise they would be re-fetched every cycle forever), and all writes go out
/// as ONE batched update call instead of one EF command per row.
/// </summary>
public sealed class SonarrCodecEnrichmentJob(
    ICacheEntryService cacheEntryService,
    ISonarrAnimeSourceService<SonarrSeries> sonarrAnimeSourceService,
    ILogger<SonarrCodecEnrichmentJob> logger)
{
    private static readonly JsonSerializerOptions CacheReadOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task EnrichAsync()
    {
        logger.LogInformation(Constants.LogMessages.CodecEnrichmentJob.Starting, DateTime.UtcNow);

        try
        {
            var entriesResult = await cacheEntryService.GetAsync(AnimeSource.Sonarr);
            if (entriesResult.IsFailure)
            {
                logger.LogError(Constants.LogMessages.CodecEnrichmentJob.Failed, entriesResult.Error.Message);
                return;
            }

            var missing = new List<(CacheEntry Entry, SonarrSeries Series)>();
            foreach (var entry in entriesResult.Value)
            {
                try
                {
                    var series = JsonSerializer.Deserialize<SonarrSeries>(entry.RawData, CacheReadOptions);
                    if (series is null)
                        continue;

                    if (series.CodecDistribution is { Count: > 0 })
                        continue; // already enriched — never touched again

                    // No episode files ⇒ no codec is possible. Skip without fetching, so
                    // unaired/empty series don't trigger API calls and rewrites every cycle.
                    if (series.Statistics?.EpisodeFileCount == 0)
                        continue;

                    missing.Add((entry, series));
                }
                catch (JsonException)
                {
                    logger.LogWarning("Skipping unreadable Sonarr cache entry {SourceId}", entry.SourceId);
                }
            }

            if (missing.Count == 0)
            {
                logger.LogInformation(Constants.LogMessages.CodecEnrichmentJob.Succeeded, 0);
                return;
            }

            var distributions = await sonarrAnimeSourceService.GetBulkCodecDistributionsAsync(
                missing.Select(m => m.Series.Id.ToString()));

            var updates = new List<(string SourceId, string RawData)>();
            foreach (var (entry, series) in missing)
            {
                if (!distributions.TryGetValue(series.Id.ToString(), out var distribution))
                    continue;

                // Nothing gained → don't rewrite the row (keeps empty-codec rows stable).
                if (distribution.Count == 0)
                    continue;

                series.CodecDistribution = distribution;
                var rawData = series.ToCacheEntry().RawData;
                if (string.Equals(rawData, entry.RawData, StringComparison.Ordinal))
                    continue;

                updates.Add((entry.SourceId, rawData));
            }

            if (updates.Count > 0)
            {
                var updateResult = await cacheEntryService.BulkUpdateRawDataAsync(AnimeSource.Sonarr, updates);
                if (updateResult.IsFailure)
                {
                    logger.LogError(Constants.LogMessages.CodecEnrichmentJob.Failed, updateResult.Error.Message);
                    return;
                }
            }

            logger.LogInformation(Constants.LogMessages.CodecEnrichmentJob.Succeeded, updates.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, Constants.LogMessages.CodecEnrichmentJob.Error);
        }
    }
}
