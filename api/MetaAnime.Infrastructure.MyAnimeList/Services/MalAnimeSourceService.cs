using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using Microsoft.Extensions.Logging;
using RestSharp;
using System.Text.Json;

namespace MetaAnime.Infrastructure.MyAnimeList.Services;

public sealed class MalAnimeSourceService(IPropertyBagRepository propertyBagRepository, IMalTokenService malTokenService, ILogger<MalAnimeSourceService> logger) : IMalAnimeSourceService<MalAnime>
{
    #region Fields
    private const string REQUEST_FIELDS = "synopsis, ";
    private const string EXTENDED_REQUEST_FIELDS = REQUEST_FIELDS + "alternative_titles{en,ja,synonyms}, " +
                                                                    "genres{id,name}, " +
                                                                    "main_picture, " +
                                                                    "my_list_status{score,num_episodes_watched,created_at,updated_at,status}, " +
                                                                    "nsfw, " +
                                                                    "popularity, " +
                                                                    "rank, " +
                                                                    "start_date, " +
                                                                    "end_date, " +
                                                                    "status, " +
                                                                    "media_type, " +
                                                                    "mean, " +
                                                                    "num_episodes, " +
                                                                    "start_season";
    private const string SEASONAL_FIELDS = "synopsis, " +
                                           "alternative_titles{en,ja}, " +
                                           "genres{id,name}, " +
                                           "main_picture, " +
                                           "nsfw, " +
                                           "start_date, " +
                                           "start_season{year,season}, " +
                                           "status, " +
                                           "media_type, " +
                                           "mean, " +
                                           "num_episodes";
    private const string FRANCHISE_FIELDS = "id,title,main_picture,synopsis,start_date,end_date,related_anime{node{id,title,main_picture},relation_type,relation_type_formatted}";
    #endregion

    #region IMalAnimeSourceService Implementation
    public async Task<Result<IList<MalAnime>>> GetAllAsync()
    {
        var malApiAnimes = new List<MalAnime>();

        var baseUrl = await GetBaseUrlAsync();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning("Skipping MAL sync: '{Key}' is blank in the database. Configure the MAL API Base URL before running a sync.", SettingsKeys.MalApiBaseUrl);
            return Result.Failure<IList<MalAnime>>(new Error(Constants.Errors.NotConfigured, $"MAL sync aborted: '{SettingsKeys.MalApiBaseUrl}' is blank. Please set the MAL API Base URL in Settings."));
        }

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Skipping MAL sync: no access token available (MAL_ACCESS_TOKEN / {Key} is blank).", SettingsKeys.MalApiToken);
            return Result.Failure<IList<MalAnime>>(new Error(Constants.Errors.NoToken, "MAL sync aborted: no access token available. Please complete the MAL OAuth flow in Settings."));
        }

        Paging paging = null!;

        // Fetch json result
        do
        {
            var (response, refreshedToken) = await ExecuteWithAutoRefreshAsync(
                token,
                t => FetchAnimeListAsync(baseUrl, paging, t));
            token = refreshedToken;
            var json = response.Content!;

            if (response.IsSuccessful)
            {
                var result = JsonSerializer.Deserialize<MalResult>(json);
                paging = result!.Paging;
                malApiAnimes.AddRange(result.Data.Select(d => d.Node));
            }
            else
            {

                var error = JsonSerializer.Deserialize<MalError>(json)!;
                return Result.Failure<IList<MalAnime>>(new Error(error.Message));
            }

        } while (paging.HasNextPage);
        return Result.Success<IList<MalAnime>>(malApiAnimes);
    }

    public async Task<Result<MalAnime>> GetByIdAsync(string id)
    {
        var baseUrl = await GetBaseUrlAsync();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result.Failure<MalAnime>(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."));
        }

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<MalAnime>(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));
        }

        var (response, _) = await ExecuteWithAutoRefreshAsync(
            token,
            t => FetchAnimeByIdAsync(baseUrl, id, t));
        var json = response.Content!;
        if (response.IsSuccessful)
        {
            var result = JsonSerializer.Deserialize<MalAnime>(json)!;
            return Result.Success(result);
        }
        else
        {

            var error = JsonSerializer.Deserialize<MalError>(json)!;
            return Result.Failure<MalAnime>(new Error(error.Message));
        }
    }

    public async Task<Result<IList<MalAnime>>> GetSeasonalAsync(int year, string season)
    {
        var baseUrl = await GetBaseUrlAsync();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning("Skipping MAL seasonal fetch: '{Key}' is blank in the database.", SettingsKeys.MalApiBaseUrl);
            return Result.Failure<IList<MalAnime>>(new Error(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."));
        }

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<IList<MalAnime>>(new Error(Constants.Errors.NoToken, "MyAnimeList access token is not available"));
        }

        // Fetch all pages from MAL (limit=500 per request, loop until no next page)
        const int perPage = 500;
        var allItems = new List<MalAnime>();
        string? nextUrl = $"{baseUrl}/anime/season/{year}/{season}?limit={perPage}&offset=0&nsfw=true&fields={SEASONAL_FIELDS}&sort=anime_score";

        while (nextUrl != null)
        {
            var (response, _) = await ExecuteWithAutoRefreshAsync(token, t =>
            {
                var restClient = new RestClient(new Uri(nextUrl));
                var restRequest = new RestRequest { Method = Method.Get };
                restRequest.AddHeader("Authorization", $"Bearer {t}");
                return ExecuteRequestAsync(restClient, restRequest);
            });
            var json = response.Content!;

            if (response.IsSuccessful)
            {
                var result = JsonSerializer.Deserialize<MalResult>(json)!;
                allItems.AddRange(result.Data.Select(d => d.Node));
                nextUrl = result.Paging.HasNextPage ? result.Paging.Next : null;
            }
            else
            {
                // 404 means MAL has no data for this season yet (e.g. a future season) — treat as empty.
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return Result.Success<IList<MalAnime>>(new List<MalAnime>());

                MalError? malError = null;
                try { malError = JsonSerializer.Deserialize<MalError>(json); }
                catch (JsonException) { /* Non-JSON body (e.g. HTML gateway error) — fall through to status-code fallback. */ }
                return Result.Failure<IList<MalAnime>>(new Error(malError?.Message ?? response.StatusCode.ToString()));
            }
        }

        // Strictly filter to only anime whose start_season matches the requested year/season.
        // MAL's seasonal endpoint may include currently-airing continuations from prior seasons;
        // this filter ensures we only show series that actually started in this exact season.
        var filtered = allItems
            .Where(a => a.StartSeason?.Year == year &&
                        string.Equals(a.StartSeason?.Season, season, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Result.Success<IList<MalAnime>>(filtered);
    }
    public async Task<Result<MalAnime>> GetWithRelatedAnimeAsync(string id)
    {
        var baseUrl = await GetBaseUrlAsync();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result.Failure<MalAnime>(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."));
        }

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<MalAnime>(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));
        }

        var (response, _) = await ExecuteWithAutoRefreshAsync(
            token,
            t => FetchAnimeWithRelatedAsync(baseUrl, id, t));
        var json = response.Content!;
        if (response.IsSuccessful)
        {
            var result = JsonSerializer.Deserialize<MalAnime>(json)!;
            return Result.Success(result);
        }
        else
        {
            var error = JsonSerializer.Deserialize<MalError>(json)!;
            return Result.Failure<MalAnime>(new Error(error.Message));
        }
    }

    private async Task<RestResponse> FetchAnimeWithRelatedAsync(string baseUrl, string id, string token)
    {
        var animeUri = new Uri($"{baseUrl}/anime/{id}?fields={FRANCHISE_FIELDS}");
        var restClient = new RestClient(animeUri);
        var restRequest = new RestRequest { Method = Method.Get };
        restRequest.AddHeader("Authorization", $"Bearer {token}");

        return await ExecuteRequestAsync(restClient, restRequest);
    }

    public async Task<Result> UpdateListStatusAsync(string animeId, string status, int numWatchedEpisodes, int score)
    {
        var baseUrl = await GetBaseUrlAsync();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result.Failure(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."));
        }

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));

        var uri = new Uri($"{baseUrl}/anime/{animeId}/my_list_status");
        var (response, _) = await ExecuteWithAutoRefreshAsync(token, t =>
        {
            var restClient = new RestClient(uri);
            var restRequest = new RestRequest { Method = Method.Patch };
            restRequest.AddHeader("Authorization", $"Bearer {t}");
            restRequest.AddParameter("status", status);
            restRequest.AddParameter("num_watched_episodes", numWatchedEpisodes);
            restRequest.AddParameter("score", score);
            return ExecuteRequestAsync(restClient, restRequest);
        });
        if (response.IsSuccessful)
            return Result.Success();

        var error = JsonSerializer.Deserialize<MalError>(response.Content!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return Result.Failure(new Error(error?.Message ?? "Failed to update MAL list status"));
    }
    #endregion

    #region Private
    private async Task<string?> GetBaseUrlAsync()
        => (await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))?.Value;

    private async Task<RestResponse> FetchAnimeListAsync(string baseUrl, Paging paging, string token)
    {
        var pagingUri = paging?.HasNextPage == true ? paging.Next : null;
        // Use EXTENDED_REQUEST_FIELDS to get my_list_status data (needed for viewState, addedDate, progression, rating)
        var animeListUri = new Uri(pagingUri ?? $"{baseUrl}/users/@me/animelist?nsfw=true&limit=1000&fields={EXTENDED_REQUEST_FIELDS}");

        var restClient = new RestClient(animeListUri);
        var restRequest = new RestRequest { Method = Method.Get };
        restRequest.AddHeader("Authorization", $"Bearer {token}");

        return await ExecuteRequestAsync(restClient, restRequest);
    }

    private async Task<RestResponse> FetchAnimeByIdAsync(string baseUrl, string id, string token)
    {
        var animeUri = new Uri($"{baseUrl}/anime/{id}?fields={EXTENDED_REQUEST_FIELDS}");
        var restClient = new RestClient(animeUri);
        var restRequest = new RestRequest { Method = Method.Get };
        restRequest.AddHeader("Authorization", $"Bearer {token}");

        return await ExecuteRequestAsync(restClient, restRequest);
    }

    /// <summary>
    /// Executes a request via the provided factory. If the response is 401 Unauthorized,
    /// attempts a token refresh exactly once and retries. Throws <see cref="MalAuthenticationException"/>
    /// when the refresh fails or the retry is still unauthorized.
    /// </summary>
    private async Task<(RestResponse Response, string Token)> ExecuteWithAutoRefreshAsync(
        string initialToken,
        Func<string, Task<RestResponse>> requestFactory)
    {
        var response = await requestFactory(initialToken);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return (response, initialToken);

        // 401 — attempt token refresh exactly once
        logger.LogWarning("MAL API returned 401. Attempting token refresh.");
        var refreshResult = await malTokenService.RefreshTokenAsync();
        if (refreshResult.IsFailure)
        {
            throw new MalAuthenticationException(
                $"MyAnimeList token is invalid or expired and could not be refreshed: {refreshResult.Error.Message}");
        }

        var newToken = await malTokenService.GetAccessTokenAsync() ?? initialToken;
        var retryResponse = await requestFactory(newToken);

        if (retryResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new MalAuthenticationException(
                "MyAnimeList token is invalid or expired. Please re-authorize in Settings.");
        }

        return (retryResponse, newToken);
    }

    private static async Task<RestResponse> ExecuteRequestAsync(RestClient client, RestRequest request)
    {
        try
        {
            return await client.ExecuteAsync(request);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error when retrieving data from api: {ex.Message}", ex);
        }
    }
    #endregion
}
