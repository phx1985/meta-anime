using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Text.Json;

namespace MetaAnime.Infrastructure.MyAnimeList.Services;

public sealed class MalAnimeSourceService(IOptions<MalSettings> options, IMalTokenService malTokenService) : IMalAnimeSourceService<MalAnime>
{
    #region Fields
    private readonly string baseUrl = options.Value.ApiBaseUrl;
    private readonly bool isConfigured = !string.IsNullOrWhiteSpace(options.Value.ApiBaseUrl);
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
    #endregion

    #region IMalAnimeSourceService Implementation
    public async Task<Result<IList<MalAnime>>> GetAllAsync()
    {
        var malApiAnimes = new List<MalAnime>();

        // Return empty list if MAL is not configured
        if (!isConfigured)
        {
            return Result.Success<IList<MalAnime>>(malApiAnimes);
        }

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Success<IList<MalAnime>>(malApiAnimes);
        }

        Paging paging = null!;

        // Fetch json result
        do
        {
            var response = await FetchAnimeListAsync(paging, token);
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
        // Return failure if MAL is not configured
        if (!isConfigured)
        {
            return Result.Failure<MalAnime>(new(Constants.Errors.NotConfigured, "MyAnimeList is not configured"));
        }

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<MalAnime>(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));
        }

        var response = await FetchAnimeByIdAsync(id, token);
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
        if (!isConfigured)
            return Result.Success<IList<MalAnime>>(new List<MalAnime>());

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return Result.Success<IList<MalAnime>>(new List<MalAnime>());

        // Fetch all pages from MAL (limit=500 per request, loop until no next page)
        const int perPage = 500;
        var allItems = new List<MalAnime>();
        string? nextUrl = $"{baseUrl}/anime/season/{year}/{season}?limit={perPage}&offset=0&nsfw=true&fields={SEASONAL_FIELDS}&sort=anime_score";

        while (nextUrl != null)
        {
            var uri = new Uri(nextUrl);
            var restClient = new RestClient(uri);
            var restRequest = new RestRequest { Method = Method.Get };
            restRequest.AddHeader("Authorization", $"Bearer {token}");

            var response = await ExecuteRequestAsync(restClient, restRequest);
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
    public async Task<Result> UpdateListStatusAsync(string animeId, string status, int numWatchedEpisodes, int score)
    {
        if (!isConfigured)
            return Result.Failure(new(Constants.Errors.NotConfigured, "MyAnimeList is not configured"));

        var token = await malTokenService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));

        var uri = new Uri($"{baseUrl}/anime/{animeId}/my_list_status");
        var restClient = new RestClient(uri);
        var restRequest = new RestRequest { Method = Method.Patch };
        restRequest.AddHeader("Authorization", $"Bearer {token}");
        restRequest.AddParameter("status", status);
        restRequest.AddParameter("num_watched_episodes", numWatchedEpisodes);
        restRequest.AddParameter("score", score);

        var response = await ExecuteRequestAsync(restClient, restRequest);
        if (response.IsSuccessful)
            return Result.Success();

        var error = JsonSerializer.Deserialize<MalError>(response.Content!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return Result.Failure(new Error(error?.Message ?? "Failed to update MAL list status"));
    }
    #endregion

    #region Private
    private async Task<RestResponse> FetchAnimeListAsync(Paging paging, string token)
    {
        var pagingUri = paging?.HasNextPage == true ? paging.Next : null;
        // Use EXTENDED_REQUEST_FIELDS to get my_list_status data (needed for viewState, addedDate, progression, rating)
        var animeListUri = new Uri(pagingUri ?? $"{baseUrl}/users/@me/animelist?nsfw=true&limit=1000&fields={EXTENDED_REQUEST_FIELDS}");

        var restClient = new RestClient(animeListUri);
        var restRequest = new RestRequest { Method = Method.Get };
        restRequest.AddHeader("Authorization", $"Bearer {token}");

        return await ExecuteRequestAsync(restClient, restRequest);
    }

    private async Task<RestResponse> FetchAnimeByIdAsync(string id, string token)
    {
        var animeUri = new Uri($"{baseUrl}/anime/{id}?fields={EXTENDED_REQUEST_FIELDS}");
        var restClient = new RestClient(animeUri);
        var restRequest = new RestRequest { Method = Method.Get };
        restRequest.AddHeader("Authorization", $"Bearer {token}");

        return await ExecuteRequestAsync(restClient, restRequest);
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
