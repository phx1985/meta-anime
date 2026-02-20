using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Net;
using System.Text.Json;

namespace MetaAnime.Infrastructure.Sonarr.Services;

public class SonarrAnimeSourceService(IOptions<SonarrSettings> options) : ISonarrAnimeSourceService<SonarrSeries>
{
    #region Fields
    private readonly string sonarrHost = options.Value.Host;
    private readonly string apiKey = options.Value.ApiKey;
    private readonly List<string> tags = [.. options.Value.AnimeTags.Split(',')];
    private static readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly bool isConfigured = !string.IsNullOrWhiteSpace(options.Value.Host) && !string.IsNullOrWhiteSpace(options.Value.ApiKey);
    #endregion

    #region ISonarrSourceService Implementation
    /// <summary>
    /// Allows to get all anime series from Sonarr
    /// </summary>
    /// <returns></returns>
    public async Task<Result<IList<SonarrSeries>>> GetAllAsync()
    {
        // Return empty list if Sonarr is not configured
        if (!isConfigured)
        {
            return Result.Success<IList<SonarrSeries>>(new List<SonarrSeries>());
        }

        var sonarrTags = await GetSonarrTagsAsync();
        var restClient = new RestClient(sonarrHost);
        var restRequest = new RestRequest("api/v3/series");
        restRequest.AddParameter("apikey", apiKey);
        var response = await restClient.ExecuteAsync(restRequest);

        if (response.IsSuccessful)
        {
            var json = response.Content!;
            var allSeries = JsonSerializer.Deserialize<SonarrSeries[]>(json, jsonOptions)!;
            IList<SonarrSeries> animes = [.. allSeries.Where(a => IsAnimeTagged(a, sonarrTags!))];
            return Result.Success(animes);
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
            return Result.Failure<IList<SonarrSeries>>(new("Sonarr.NotFound", "Sonarr series endpoint not found"));
        else
            throw new HttpRequestException($"Failed to get Sonarr series: {response.StatusCode} - {response.Content}");
    }

    public async Task<Result<SonarrSeries>> GetByIdAsync(string id)
    {
        // Return failure if Sonarr is not configured
        if (!isConfigured)
        {
            return Result.Failure<SonarrSeries>(new("Sonarr.NotConfigured", "Sonarr is not configured"));
        }

        var restClient = new RestClient(sonarrHost);
        var restRequest = new RestRequest($"api/v3/series/{id}");
        restRequest.AddParameter("apikey", apiKey);
        var response = await restClient.ExecuteAsync(restRequest);

        if (response.IsSuccessful)
        {
            var json = response.Content!;
            var anime = JsonSerializer.Deserialize<SonarrSeries>(json, jsonOptions)!;
            return Result.Success(anime);
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
            return Result.Failure<SonarrSeries>(new("Sonarr.NotFound", $"Sonarr series with id '{id}' not found"));
        else
            throw new HttpRequestException($"Failed to get Sonarr series: {response.StatusCode} - {response.Content}");
    }

    public async Task<Dictionary<string, int>> GetCodecDistributionAsync(string seriesId)
    {
        var codecDistribution = new Dictionary<string, int>();

        // Return empty dictionary if Sonarr is not configured
        if (!isConfigured)
        {
            return codecDistribution;
        }

        var episodeFiles = await FetchEpisodeFilesAsync(seriesId);
        if (episodeFiles != null)
        {
            foreach (var file in episodeFiles)
            {
                var codec = file?.MediaInfo?.VideoCodec ?? "Unknown";
                codecDistribution[codec] = codecDistribution.GetValueOrDefault(codec, 0) + 1;
            }
        }

        return codecDistribution;
    }

    public async Task<Result<IList<SonarrSeriesCodecResult>>> GetSeriesByCodecAsync(string codec)
    {
        if (!isConfigured)
            return Result.Success<IList<SonarrSeriesCodecResult>>(new List<SonarrSeriesCodecResult>());

        var allSeriesResult = await GetAllAsync();
        if (allSeriesResult.IsFailure)
            return Result.Failure<IList<SonarrSeriesCodecResult>>(allSeriesResult.Error);

        // Throttle concurrent requests to avoid overwhelming the Sonarr API
        using var semaphore = new SemaphoreSlim(10, 10);

        var tasks = allSeriesResult.Value.Select(async series =>
        {
            await semaphore.WaitAsync();
            try
            {
                var files = await FetchEpisodeFilesAsync(series.Id.ToString());
                if (files == null || files.Length == 0)
                    return null;

                var codecDist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                long totalSize = 0;
                foreach (var file in files)
                {
                    var c = file?.MediaInfo?.VideoCodec ?? "Unknown";
                    codecDist[c] = codecDist.GetValueOrDefault(c, 0) + 1;
                    totalSize += file?.Size ?? 0;
                }

                // Only include series that have at least one file matching the requested codec
                bool hasRequestedCodec = codecDist.Keys.Any(k =>
                    k.Contains(codec, StringComparison.OrdinalIgnoreCase));

                if (!hasRequestedCodec)
                    return null;

                return new SonarrSeriesCodecResult(series.Id, series.Title, codecDist, totalSize);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        IList<SonarrSeriesCodecResult> matched = results
            .Where(r => r != null)
            .Select(r => r!)
            .OrderBy(r => r.Title)
            .ToList();

        return Result.Success(matched);
    }
    #endregion

    #region Private Methods
    private async Task<SonarrEpisodeFile[]?> FetchEpisodeFilesAsync(string seriesId)
    {
        var restClient = new RestClient(sonarrHost);
        var restRequest = new RestRequest("api/v3/episodefile");
        restRequest.AddParameter("apikey", apiKey);
        restRequest.AddParameter("seriesId", seriesId);
        var response = await restClient.ExecuteAsync(restRequest);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            return JsonSerializer.Deserialize<SonarrEpisodeFile[]>(response.Content, jsonOptions);

        return null;
    }

    private async Task<List<SonarrTag>?> GetSonarrTagsAsync()
    {
        var restClient = new RestClient(sonarrHost);
        var restRequest = new RestRequest("api/v3/tag");
        restRequest.AddParameter("apikey", apiKey);
        var response = await restClient.ExecuteAsync(restRequest);
        if (response.IsSuccessful)
        {
            var json = response.Content!;
            var sonarrTags = JsonSerializer.Deserialize<SonarrTag[]>(json, jsonOptions)!;
            var filteredTags = sonarrTags.Where(st => tags.Any(t => t.Equals(st.Label, StringComparison.InvariantCultureIgnoreCase))).ToList();
            return filteredTags;
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        else
            throw new HttpRequestException($"Failed to get Sonarr tags: {response.StatusCode} - {response.Content}");
    }

    /// <summary>
    /// Check if the anime is tagged as anime
    /// </summary>
    /// <param name="anime"></param>
    /// <param name="sonarrTags"></param>
    /// <returns></returns>
    private bool IsAnimeTagged(SonarrSeries anime, List<SonarrTag> sonarrTags)
    {
        if (tags.Count == 0)
            return false;
        return anime.Tags.Any(tagId => sonarrTags.Any(sonarrTag => sonarrTag.Id == tagId));
    }
    #endregion
}
