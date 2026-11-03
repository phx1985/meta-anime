using MetaAnime.Application.Contracts.LibraryAnalysis;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Enums;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Logging;
using RestSharp;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MetaAnime.Application.Services;

/// <summary>
/// Implements library analysis by combining Sonarr series metadata (Phase 1)
/// with per-episode MediaInfo data (Phase 2) to surface storage inefficiencies.
/// </summary>
public sealed class LibraryAnalysisService(
    ISonarrAnimeSourceService<SonarrSeries> sonarrService,
    IPropertyBagRepository propertyBagRepository,
    IAnimeMappingRepository animeMappingRepository,
    IHttpClientFactory httpClientFactory,
    ILogger<LibraryAnalysisService> logger) : ILibraryAnalysisService
{
    #region Constants

    private const string SonarrClientName = "Sonarr";

    /// <summary>
    /// Creates a RestSharp client backed by the pooled <see cref="IHttpClientFactory"/> client
    /// so requests flow through the Aspire standard resilience handler instead of allocating
    /// a fresh socket pool per call.
    /// </summary>
    private RestClient CreateRestClient(string host)
        => new(httpClientFactory.CreateClient(SonarrClientName), new RestClientOptions { BaseUrl = new Uri(host) });

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Maps raw codec tokens to a canonical label (case-insensitive lookup).</summary>
    private static readonly Dictionary<string, string> VideoCodecNormalizationMap =
        BuildCodecNormalizationMap(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["H264"]  = ["h264", "x264", "avc"],
            ["HEVC"]  = ["hevc", "h265", "x265"],
            ["AV1"]   = ["av1"],
            ["MPEG2"] = ["mpeg2", "mpeg-2"],
        });

    #endregion

    #region Phase 1 — GetSummaryAsync

    public async Task<Result<LibraryAnalysisSummary>> GetSummaryAsync()
    {
        try
        {
            var allSeriesResult = await sonarrService.GetAllAsync();
            if (allSeriesResult.IsFailure)
                return Result.Failure<LibraryAnalysisSummary>(allSeriesResult.Error);

            var seriesList = allSeriesResult.Value;

            // BUG 4 — read configurable threshold from PropertyBag (default 1200 MB)
            var thresholdEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.LibraryAnalysisAvgEpSizeMb);
            var thresholdMb = int.TryParse(thresholdEntry?.Value, out var t) ? t : 1200;

            var gibPerHourEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.LibraryAnalysisAvgGibPerHour);
            var avgGibPerHourThreshold = double.TryParse(gibPerHourEntry?.Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var gph) ? gph : 1.2;

            // Build Sonarr series ID → AniDb ID lookup.
            // The GET /api/v1/animes/{id} route expects an AniDb ID (e.g. "15"), NOT the
            // Anime entity GUID. AnimeMappings join Anime↔AniDbId so we load them with
            // their Anime navigation and filter for Sonarr-sourced entries.
            var allMappings = await animeMappingRepository.GetAllAsync(includeAnime: true);
            var sonarrIdToAnimeId = allMappings
                .Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr
                         && m.Anime?.SourceId != null
                         && int.TryParse(m.Anime.SourceId, out _))
                .DistinctBy(m => m.Anime!.SourceId)
                .ToDictionary(m => int.Parse(m.Anime!.SourceId), m => m.AniDbId);

            var rows = seriesList.Select(s => MapToPhase1Row(s, sonarrIdToAnimeId)).ToList();

            var totalSizeBytes = rows.Sum(r => r.TotalSizeBytes);
            var totalFileCount = rows.Sum(r => r.FileCount);
            var missingFileSeriesCount = rows.Count(r => r.FileCount < r.TotalEpisodes);

            var summary = new LibraryAnalysisSummary(
                TotalSizeBytes: totalSizeBytes,
                TotalFileCount: totalFileCount,
                TotalSeriesCount: rows.Count,
                MissingFileSeriesCount: missingFileSeriesCount,
                Series: rows,
                AvgEpSizeThresholdMb: thresholdMb,
                AvgGibPerHourThreshold: avgGibPerHourThreshold
            );

            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get library analysis summary");
            return Result.Failure<LibraryAnalysisSummary>(
                new Error(Constants.Errors.GetLibraryAnalysisAsync, ex.Message));
        }
    }

    #endregion

    #region Phase 2 — EnrichSeriesAsync

    public async Task<Result<IList<LibrarySeriesRow>>> EnrichSeriesAsync(IList<int> sonarrIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var (host, apiKey, isConfigured) = await GetSonarrSettingsAsync();
            if (!isConfigured)
                return Result.Success<IList<LibrarySeriesRow>>([]);

            var enrichedRows = new ConcurrentDictionary<int, LibrarySeriesRow>();
            using var semaphore = new SemaphoreSlim(15, 15);

            await Task.WhenAll(sonarrIds.Select(async sonarrId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var episodeFiles = await FetchEpisodeFilesAsync(sonarrId.ToString(), host, apiKey, cancellationToken);
                    if (episodeFiles is not { Length: > 0 })
                        return;

                    var enrichedRow = BuildEnrichedRow(sonarrId, episodeFiles);
                    enrichedRows[sonarrId] = enrichedRow;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to enrich series {SonarrId}", sonarrId);
                }
                finally
                {
                    semaphore.Release();
                }
            }));

            // Return enriched rows in the same order as the input IDs
            IList<LibrarySeriesRow> result = sonarrIds
                .Where(enrichedRows.ContainsKey)
                .Select(id => enrichedRows[id])
                .ToList();

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enrich library series");
            return Result.Failure<IList<LibrarySeriesRow>>(
                new Error(Constants.Errors.EnrichLibrarySeriesAsync, ex.Message));
        }
    }

    #endregion

    #region Private — Mapping Helpers

    /// <summary>Maps a Sonarr series to a Phase 1 row (no episode-file details).</summary>
    private static LibrarySeriesRow MapToPhase1Row(SonarrSeries series, Dictionary<int, string> sonarrIdToAnimeId)
    {
        var fileCount = series.Statistics?.EpisodeFileCount ?? 0;
        var totalEpisodes = series.Statistics?.TotalEpisodeCount ?? 0;
        var totalSizeBytes = ExtractSizeOnDisk(series.Statistics?.SizeOnDisk);
        var avgEpisodeSizeBytes = fileCount > 0 ? totalSizeBytes / fileCount : 0L;
        var percentDownloaded = series.Statistics?.PercentOfEpisodes ?? 0.0;

        return new LibrarySeriesRow(
            SonarrId: series.Id,
            Title: series.Title ?? string.Empty,
            Path: series.Path ?? string.Empty,
            Ended: series.Ended,
            FileCount: fileCount,
            TotalEpisodes: totalEpisodes,
            TotalSizeBytes: totalSizeBytes,
            AvgEpisodeSizeBytes: avgEpisodeSizeBytes,
            PercentDownloaded: percentDownloaded,
            PrimaryVideoCodec: null,
            PrimaryAudioCodec: null,
            PrimaryResolution: null,
            PrimaryBitDepth: null,
            VideoCodecDistribution: null,
            AudioCodecDistribution: null,
            ResolutionDistribution: null,
            EpisodeFiles: null,
            LocalAnimeId: sonarrIdToAnimeId.TryGetValue(series.Id, out var aid) ? aid : null,
            TotalDurationSeconds: 0,
            GibPerHour: 0.0,
            NextAiringDate: series.NextAiring
        );
    }

    /// <summary>Builds a fully enriched row from episode file data.</summary>
    private static LibrarySeriesRow BuildEnrichedRow(int sonarrId, SonarrEpisodeFile[] episodeFiles)
    {
        var videoCodecDist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var audioCodecDist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resolutionDist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bitDepthCounts = new Dictionary<int, int>();

        var mappedFiles = new List<LibraryEpisodeFileInfo>(episodeFiles.Length);
        long totalSize = 0;
        int totalDurationSeconds = 0;

        foreach (var file in episodeFiles)
        {
            var normalizedVideoCodec = NormalizeVideoCodec(file.MediaInfo?.VideoCodec);
            var normalizedAudioCodec = NormalizeAudioCodec(file.MediaInfo?.AudioCodec);
            var resolution = MediaInfo.ParseResolutionLabel(file.MediaInfo?.Resolution ?? string.Empty, file.MediaInfo?.Height ?? 0);
            var bitDepth = file.MediaInfo?.VideoBitDepth ?? 0;
            var rawDurationSeconds = ParseRunTimeSeconds(file.MediaInfo?.RunTime);
            const int FallbackDurationSeconds = 1380; // 23 minutes
            var isDurationEstimated = rawDurationSeconds == 0;
            var durationSeconds = isDurationEstimated ? FallbackDurationSeconds : rawDurationSeconds;

            videoCodecDist[normalizedVideoCodec] = videoCodecDist.GetValueOrDefault(normalizedVideoCodec, 0) + 1;
            audioCodecDist[normalizedAudioCodec] = audioCodecDist.GetValueOrDefault(normalizedAudioCodec, 0) + 1;
            resolutionDist[resolution] = resolutionDist.GetValueOrDefault(resolution, 0) + 1;
            bitDepthCounts[bitDepth] = bitDepthCounts.GetValueOrDefault(bitDepth, 0) + 1;

            totalSize += file.Size;
            totalDurationSeconds += durationSeconds;

            var qualityProfile = file.Quality?.QualityInfo?.Name ?? "Unknown";
            mappedFiles.Add(new LibraryEpisodeFileInfo(
                EpisodeFileId: file.Id,
                RelativePath: file.RelativePath ?? string.Empty,
                SizeBytes: file.Size,
                VideoCodec: normalizedVideoCodec,
                AudioCodec: normalizedAudioCodec,
                Resolution: resolution,
                VideoBitDepth: bitDepth,
                AudioChannels: file.MediaInfo?.AudioChannels ?? 0.0,
                QualityProfile: qualityProfile,
                DurationSeconds: durationSeconds,
                IsDurationEstimated: isDurationEstimated
            ));
        }

        var primaryVideoCodec = GetMostFrequentKey(videoCodecDist);
        var primaryAudioCodec = GetMostFrequentKey(audioCodecDist);
        var primaryResolution = GetMostFrequentKey(resolutionDist);
        var primaryBitDepth = bitDepthCounts.Count > 0
            ? bitDepthCounts.MaxBy(kvp => kvp.Value).Key
            : (int?)null;

        var fileCount = episodeFiles.Length;
        var avgSize = fileCount > 0 ? totalSize / fileCount : 0L;
        var totalHours = totalDurationSeconds / 3600.0;
        var gibPerHour = totalHours > 0 ? (totalSize / 1_073_741_824.0) / totalHours : 0.0;

        return new LibrarySeriesRow(
            SonarrId: sonarrId,
            Title: string.Empty,      // Caller should merge with Phase 1 data
            Path: string.Empty,       // Caller should merge with Phase 1 data
            Ended: false,             // Caller should merge with Phase 1 data
            FileCount: fileCount,
            TotalEpisodes: fileCount, // Best guess from episode files alone
            TotalSizeBytes: totalSize,
            AvgEpisodeSizeBytes: avgSize,
            PercentDownloaded: 0.0,   // Caller should merge with Phase 1 data
            PrimaryVideoCodec: primaryVideoCodec,
            PrimaryAudioCodec: primaryAudioCodec,
            PrimaryResolution: primaryResolution,
            PrimaryBitDepth: primaryBitDepth,
            VideoCodecDistribution: videoCodecDist,
            AudioCodecDistribution: audioCodecDist,
            ResolutionDistribution: resolutionDist,
            EpisodeFiles: mappedFiles,
            LocalAnimeId: null,       // Caller should merge with Phase 1 data
            TotalDurationSeconds: totalDurationSeconds,
            GibPerHour: gibPerHour
        );
    }

    #endregion

    #region Private — Sonarr API Helpers

    /// <summary>Retrieves Sonarr host and API key from PropertyBag settings.</summary>
    private async Task<(string Host, string ApiKey, bool IsConfigured)> GetSonarrSettingsAsync()
    {
        var hostEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrHost);
        var apiKeyEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrApiKey);

        var host = hostEntry?.Value ?? string.Empty;
        var apiKey = apiKeyEntry?.Value ?? string.Empty;
        var isConfigured = !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(apiKey);

        return (host, apiKey, isConfigured);
    }

    /// <summary>Fetches episode files for a single series from Sonarr's API.</summary>
    private async Task<SonarrEpisodeFile[]?> FetchEpisodeFilesAsync(string seriesId, string host, string apiKey, CancellationToken cancellationToken = default)
    {
        var restClient = CreateRestClient(host);
        var restRequest = new RestRequest("api/v3/episodefile");
        restRequest.AddParameter("apikey", apiKey);
        restRequest.AddParameter("seriesId", seriesId);
        var response = await restClient.ExecuteAsync(restRequest, cancellationToken);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            return JsonSerializer.Deserialize<SonarrEpisodeFile[]>(response.Content, JsonOptions);

        return null;
    }

    #endregion

    #region Private — Codec Normalization

    /// <summary>Normalizes a raw video codec string to a canonical label (e.g. "x264" → "H264"). Returns "Unknown" for null/empty input.</summary>
    private static string NormalizeVideoCodec(string? rawCodec)
    {
        if (string.IsNullOrWhiteSpace(rawCodec)) return "Unknown";
        return VideoCodecNormalizationMap.TryGetValue(rawCodec.Trim().ToLowerInvariant(), out var normalized)
            ? normalized
            : rawCodec.Trim().ToUpperInvariant();
    }

    /// <summary>Normalizes an audio codec string (uppercase + trim). Returns "Unknown" for null/empty input.</summary>
    private static string NormalizeAudioCodec(string? rawCodec)
    {
        if (string.IsNullOrWhiteSpace(rawCodec)) return "Unknown";
        return rawCodec.Trim().ToUpperInvariant();
    }

    /// <summary>Builds a reverse lookup from each alias to its canonical label.</summary>
    private static Dictionary<string, string> BuildCodecNormalizationMap(
        Dictionary<string, string[]> codecAliases)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, aliases) in codecAliases)
        {
            foreach (var alias in aliases)
            {
                map[alias] = canonical;
            }
        }
        return map;
    }

    #endregion

    #region Private — Utility

    /// <summary>Returns the key with the highest count, or null if the dictionary is empty.</summary>
    private static string? GetMostFrequentKey(Dictionary<string, int> distribution) =>
        distribution.Count > 0
            ? distribution.MaxBy(kvp => kvp.Value).Key
            : null;

    /// <summary>
    /// Parses a Sonarr MediaInfo.RunTime string (e.g. "00:24:02" or "24:02") to a total number of seconds.
    /// Returns 0 for null, empty, or unrecognised formats.
    /// </summary>
    private static int ParseRunTimeSeconds(string? runTime)
    {
        if (string.IsNullOrWhiteSpace(runTime)) return 0;
        var parts = runTime.Trim().Split(':');
        try
        {
            return parts.Length switch
            {
                3 => int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]),
                2 => int.Parse(parts[0]) * 60 + int.Parse(parts[1]),
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Safely extracts a long value from the Sonarr Statistics.SizeOnDisk property,
    /// which may be a JsonElement, long, int, or string depending on the Sonarr version.
    /// </summary>
    private static long ExtractSizeOnDisk(object? sizeOnDisk)
    {
        if (sizeOnDisk is null)
            return 0L;

        if (sizeOnDisk is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt64(out var longValue))
                return longValue;
            return 0L;
        }

        return sizeOnDisk switch
        {
            long l => l,
            int i => (long)i,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => 0L
        };
    }

    #endregion
}
