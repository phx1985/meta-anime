using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace MetaAnime.Infrastructure.Sonarr.Services;

public class SonarrAnimeSourceService(IOptions<SonarrSettings> options, IPropertyBagRepository propertyBagRepository) : ISonarrAnimeSourceService<SonarrSeries>
{
    #region Fields
    private static readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };
    #endregion

    private async Task<(string host, string apiKey, List<string> tags, bool isConfigured)> GetSettingsAsync()
    {
        var hostEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrHost);
        var apiKeyEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrApiKey);
        var tagsEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrAnimeTags);

        var host = hostEntry?.Value ?? options.Value.Host ?? string.Empty;
        var apiKey = apiKeyEntry?.Value ?? options.Value.ApiKey ?? string.Empty;
        var tagsStr = tagsEntry?.Value ?? options.Value.AnimeTags ?? string.Empty;
        var tags = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        var isConfigured = !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(apiKey);

        return (host, apiKey, tags, isConfigured);
    }

    #region ISonarrSourceService Implementation
    /// <summary>
    /// Allows to get all anime series from Sonarr
    /// </summary>
    public async Task<Result<IList<SonarrSeries>>> GetAllAsync()
    {
        var (host, apiKey, tags, isConfigured) = await GetSettingsAsync();

        // Return empty list if Sonarr is not configured
        if (!isConfigured)
        {
            return Result.Success<IList<SonarrSeries>>(new List<SonarrSeries>());
        }

        var sonarrTags = await GetSonarrTagsAsync(host, apiKey, tags);
        var restClient = new RestClient(host);
        var restRequest = new RestRequest("api/v3/series");
        restRequest.AddParameter("apikey", apiKey);
        var response = await restClient.ExecuteAsync(restRequest);

        if (response.IsSuccessful)
        {
            var json = response.Content!;
            var allSeries = JsonSerializer.Deserialize<SonarrSeries[]>(json, jsonOptions)!;
            IList<SonarrSeries> animes = [.. allSeries.Where(a => IsAnimeTagged(a, sonarrTags!, tags))];
            return Result.Success(animes);
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
            return Result.Failure<IList<SonarrSeries>>(new(Constants.Errors.NotFound, "Sonarr series endpoint not found"));
        else
            throw new HttpRequestException($"Failed to get Sonarr series: {response.StatusCode} - {response.Content}");
    }

    public async Task<Result<SonarrSeries>> GetByIdAsync(string id)
    {
        var (host, apiKey, _, isConfigured) = await GetSettingsAsync();

        // Return failure if Sonarr is not configured
        if (!isConfigured)
        {
            return Result.Failure<SonarrSeries>(new(Constants.Errors.NotConfigured, "Sonarr is not configured"));
        }

        var restClient = new RestClient(host);
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
            return Result.Failure<SonarrSeries>(new(Constants.Errors.NotFound, $"Sonarr series with id '{id}' not found"));
        else
            throw new HttpRequestException($"Failed to get Sonarr series: {response.StatusCode} - {response.Content}");
    }

    public async Task<Dictionary<string, int>> GetCodecDistributionAsync(string seriesId)
    {
        var codecDistribution = new Dictionary<string, int>();
        var (host, apiKey, _, isConfigured) = await GetSettingsAsync();

        // Return empty dictionary if Sonarr is not configured
        if (!isConfigured)
        {
            return codecDistribution;
        }

        var episodeFiles = await FetchEpisodeFilesAsync(seriesId, host, apiKey);
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

    public async Task<Dictionary<string, Dictionary<string, int>>> GetBulkCodecDistributionsAsync(IEnumerable<string> seriesIds)
    {
        var ids = seriesIds.ToList();
        var result = new ConcurrentDictionary<string, Dictionary<string, int>>();

        if (ids.Count == 0)
            return [];

        var (host, apiKey, _, isConfigured) = await GetSettingsAsync();
        if (!isConfigured)
            return [];

        using var semaphore = new SemaphoreSlim(10, 10);
        await Task.WhenAll(ids.Select(async seriesId =>
        {
            await semaphore.WaitAsync();
            try
            {
                var distribution = new Dictionary<string, int>();
                var episodeFiles = await FetchEpisodeFilesAsync(seriesId, host, apiKey);
                if (episodeFiles != null)
                {
                    foreach (var file in episodeFiles)
                    {
                        var codec = file?.MediaInfo?.VideoCodec ?? "Unknown";
                        distribution[codec] = distribution.GetValueOrDefault(codec, 0) + 1;
                    }
                }
                result[seriesId] = distribution;
            }
            finally
            {
                semaphore.Release();
            }
        }));

        return new Dictionary<string, Dictionary<string, int>>(result);
    }

    public async Task<Result<IList<SonarrSeriesCodecResult>>> GetSeriesByCodecAsync(string codec)
    {
        var (host, apiKey, _, isConfigured) = await GetSettingsAsync();

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
                var files = await FetchEpisodeFilesAsync(series.Id.ToString(), host, apiKey);
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

    public async Task<Result<SonarrInstanceOptions>> GetOptionsAsync()
    {
        var (host, apiKey, tags, isConfigured) = await GetSettingsAsync();
        if (!isConfigured)
            return Result.Failure<SonarrInstanceOptions>(
                new(Constants.Errors.NotConfigured, "Sonarr is not configured"));

        var client = new RestClient(host);

        var rootReq = new RestRequest("api/v3/rootfolder");
        rootReq.AddParameter("apikey", apiKey);
        var rootResp = await client.ExecuteAsync(rootReq);
        if (!rootResp.IsSuccessful)
            return Result.Failure<SonarrInstanceOptions>(
                new("sonarr.rootfolders", rootResp.Content ?? "Failed to get root folders"));
        var rawFolders = JsonSerializer.Deserialize<SonarrRootFolder[]>(rootResp.Content!, jsonOptions) ?? [];
        IList<SonarrRootFolderInfo> rootFolders = rawFolders.Select(f => new SonarrRootFolderInfo(f.Id, f.Path, f.FreeSpace)).ToList();

        var profileReq = new RestRequest("api/v3/qualityprofile");
        profileReq.AddParameter("apikey", apiKey);
        var profileResp = await client.ExecuteAsync(profileReq);
        if (!profileResp.IsSuccessful)
            return Result.Failure<SonarrInstanceOptions>(
                new("sonarr.qualityprofiles", profileResp.Content ?? "Failed to get quality profiles"));
        var rawProfiles = JsonSerializer.Deserialize<SonarrQualityProfile[]>(profileResp.Content!, jsonOptions) ?? [];
        IList<SonarrQualityProfileInfo> profiles = rawProfiles.Select(p => new SonarrQualityProfileInfo(p.Id, p.Name)).ToList();

        // Resolve configured tag names → Sonarr tag IDs
        var sonarrTags = await GetSonarrTagsAsync(host, apiKey, tags);
        IList<int> tagIds = sonarrTags?.Select(t => t.Id).ToList() ?? [];

        return Result.Success(new SonarrInstanceOptions(rootFolders, profiles, tagIds));
    }

    public async Task<Result<SonarrAddedSeries>> AddSeriesAsync(int tvdbId, string rootFolderPath, int qualityProfileId, IList<int> tagIds)
    {
        var (host, apiKey, _, isConfigured) = await GetSettingsAsync();
        if (!isConfigured)
            return Result.Failure<SonarrAddedSeries>(new(Constants.Errors.NotConfigured, "Sonarr is not configured"));

        var client = new RestClient(host);

        // Look up series by TVDB ID
        var lookupReq = new RestRequest("api/v3/series/lookup");
        lookupReq.AddParameter("apikey", apiKey);
        lookupReq.AddParameter("term", $"tvdb:{tvdbId}");
        var lookupResp = await client.ExecuteAsync(lookupReq);
        if (!lookupResp.IsSuccessful || string.IsNullOrEmpty(lookupResp.Content))
            return Result.Failure<SonarrAddedSeries>(new("sonarr.lookup", "Series lookup failed"));

        var lookupResults = JsonSerializer.Deserialize<SonarrSeries[]>(lookupResp.Content, jsonOptions);
        var match = lookupResults?.FirstOrDefault();
        if (match == null)
            return Result.Failure<SonarrAddedSeries>(new(Constants.Errors.NotFound, $"TVDB ID {tvdbId} not found in Sonarr lookup"));

        // Build the add-series payload
        var addPayload = new
        {
            tvdbId,
            title = match.Title,
            titleSlug = match.TitleSlug,
            images = match.Images,
            seasons = match.Seasons?.Select(s => new { s.SeasonNumber, monitored = true }) ?? [],
            rootFolderPath,
            qualityProfileId,
            seriesType = "anime",
            seasonFolder = true,
            monitored = true,
            tags = tagIds,
        };

        var addReq = new RestRequest("api/v3/series", Method.Post);
        addReq.AddQueryParameter("apikey", apiKey);
        addReq.AddJsonBody(addPayload);
        var addResp = await client.ExecuteAsync(addReq);
        if (!addResp.IsSuccessful)
            return Result.Failure<SonarrAddedSeries>(new("sonarr.add", addResp.Content ?? "Failed to add series"));

        var added = JsonSerializer.Deserialize<SonarrSeries>(addResp.Content!, jsonOptions)!;
        return Result.Success(new SonarrAddedSeries(added.Id, added.Title, added.TitleSlug ?? added.Id.ToString()));
    }

    public async Task<Result<IList<SonarrLookupItem>>> SearchLookupAsync(string term)
    {
        var (host, apiKey, _, isConfigured) = await GetSettingsAsync();
        if (!isConfigured)
            return Result.Failure<IList<SonarrLookupItem>>(new(Constants.Errors.NotConfigured, "Sonarr is not configured"));

        var client = new RestClient(host);
        var req = new RestRequest("api/v3/series/lookup");
        req.AddParameter("apikey", apiKey);
        req.AddParameter("term", term);

        var resp = await client.ExecuteAsync(req);
        if (!resp.IsSuccessful || string.IsNullOrEmpty(resp.Content))
            return Result.Failure<IList<SonarrLookupItem>>(new("sonarr.lookup", resp.Content ?? "Lookup failed"));

        var results = JsonSerializer.Deserialize<SonarrSeries[]>(resp.Content, jsonOptions) ?? [];
        IList<SonarrLookupItem> items = results.Select(s => new SonarrLookupItem(
            s.TvdbId,
            s.Title,
            s.Year > 0 ? s.Year : null,
            s.TitleSlug,
            s.Images?.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl
                ?? s.Images?.FirstOrDefault()?.RemoteUrl,
            s.Overview,
            AlreadyInLibrary: s.Id > 0
        )).ToList();

        return Result.Success(items);
    }

    public async Task<Result> RemoveSeriesAsync(int sonarrId)
    {
        var (host, apiKey, _, isConfigured) = await GetSettingsAsync();
        if (!isConfigured)
            return Result.Failure(new(Constants.Errors.NotConfigured, "Sonarr is not configured"));

        var client = new RestClient(host);
        var req = new RestRequest($"api/v3/series/{sonarrId}", Method.Delete);
        req.AddQueryParameter("apikey", apiKey);
        req.AddQueryParameter("deleteFiles", "false");
        var resp = await client.ExecuteAsync(req);
        return resp.IsSuccessful
            ? Result.Success()
            : Result.Failure(new("sonarr.remove", $"Failed to remove series (HTTP {(int)resp.StatusCode}): {resp.Content ?? "no details"}"));
    }
    #endregion

    #region Private Methods
    private async Task<SonarrEpisodeFile[]?> FetchEpisodeFilesAsync(string seriesId, string host, string apiKey)
    {
        var restClient = new RestClient(host);
        var restRequest = new RestRequest("api/v3/episodefile");
        restRequest.AddParameter("apikey", apiKey);
        restRequest.AddParameter("seriesId", seriesId);
        var response = await restClient.ExecuteAsync(restRequest);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            return JsonSerializer.Deserialize<SonarrEpisodeFile[]>(response.Content, jsonOptions);

        return null;
    }

    private async Task<List<SonarrTag>?> GetSonarrTagsAsync(string host, string apiKey, List<string> tags)
    {
        var restClient = new RestClient(host);
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
    private static bool IsAnimeTagged(SonarrSeries anime, List<SonarrTag> sonarrTags, List<string> tags)
    {
        if (tags.Count == 0)
            return false;
        return anime.Tags.Any(tagId => sonarrTags.Any(sonarrTag => sonarrTag.Id == tagId));
    }
    #endregion
}
