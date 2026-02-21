using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Text.Json;

namespace MetaAnime.Infrastructure.MyAnimeList.Services;

public class MalAnimeSourceService(IOptions<MalSettings> options, IMalTokenService malTokenService) : IMalAnimeSourceService<MalAnime>
{
    #region Fields
    private readonly string baseUrl = options.Value.ApiBaseUrl;
    private readonly bool isConfigured = !string.IsNullOrWhiteSpace(options.Value.ApiBaseUrl);
    private const string REQUEST_FIELDS = "synopsis";
    private const string EXTENDED_REQUEST_FIELDS = REQUEST_FIELDS + "alternative_titles{en,synonyms}, " +
                                                                    "genres{id,name}, " +
                                                                    "my_list_status{score,created_at,updated_at,status}, " +
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
