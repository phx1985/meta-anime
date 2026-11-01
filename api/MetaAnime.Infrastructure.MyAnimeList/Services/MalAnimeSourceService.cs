using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using Microsoft.Extensions.Logging;
using RestSharp;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetaAnime.Infrastructure.MyAnimeList.Services;

public sealed class MalAnimeSourceService(
    IPropertyBagRepository propertyBagRepository,
    IMalTokenService malTokenService,
    IHttpClientFactory httpClientFactory,
    ILogger<MalAnimeSourceService> logger) : IMalAnimeSourceService<MalAnime>
{
    #region Fields
    private const string ClientName = "MAL";
    private const int MalPageLimit = 500;
    // MAL rejects GET /anime?q= with 400 "invalid q" below 3 characters and caps limit at 100.
    private const int MalMinSearchQueryLength = 3;
    private const int MalMaxSearchLimit = 100;
    private const string UnreadableSearchResponseMessage = "MyAnimeList returned a search response that could not be read.";
    private static readonly Regex SearchNoiseRegex = new(@"[:\?!]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Creates a RestSharp client backed by the pooled <see cref="IHttpClientFactory"/> client
    /// so requests flow through the Aspire standard resilience handler instead of allocating
    /// a fresh socket pool per call.
    /// </summary>
    private RestClient CreateRestClient(Uri uri)
        => new(httpClientFactory.CreateClient(ClientName), new RestClientOptions { BaseUrl = uri });
    private const string REQUEST_FIELDS = "synopsis, ";
    private const string EXTENDED_REQUEST_FIELDS = REQUEST_FIELDS + "alternative_titles{en,ja,synonyms}, " +
                                                                    "genres{id,name}, " +
                                                                    "main_picture, " +
                                                                    "my_list_status{score,num_episodes_watched,created_at,updated_at,status}, " +
                                                                    "nsfw, " +
                                                                    "rating, " +
                                                                    "popularity, " +
                                                                    "rank, " +
                                                                    "start_date, " +
                                                                    "end_date, " +
                                                                    "status, " +
                                                                    "media_type, " +
                                                                    "mean, " +
                                                                    "num_episodes, " +
                                                                    "start_season, " +
                                                                    "trailer{youtube_id,url}, " +
                                                                    "pictures{medium,large}";
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
                                           "num_episodes, " +
                                           "rating";
    private const string FRANCHISE_FIELDS = "id,title,main_picture,synopsis,start_date,end_date,media_type,status,my_list_status{status},related_anime{node{id,title,main_picture,media_type,status,my_list_status{status}},relation_type,relation_type_formatted}";
    #endregion

    #region IMalAnimeSourceService Implementation
    public async Task<Result<IList<MalAnime>>> GetAllAsync()
    {
        List<MalAnime> malApiAnimes = [];

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
        var (ok, baseUrl, token) = await ValidateCredentialsAsync();
        if (!ok)
            return string.IsNullOrWhiteSpace(baseUrl)
                ? Result.Failure<MalAnime>(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."))
                : Result.Failure<MalAnime>(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));

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

        List<MalAnime> allItems = [];
        string? nextUrl = $"{baseUrl}/anime/season/{year}/{season}?limit={MalPageLimit}&offset=0&nsfw=true&fields={SEASONAL_FIELDS}&sort=anime_score";

        while (nextUrl != null)
        {
            var (response, _) = await ExecuteWithAutoRefreshAsync(token, t =>
            {
                var restClient = CreateRestClient(new Uri(nextUrl));
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
                    return Result.Success<IList<MalAnime>>([]);

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
        var (ok, baseUrl, token) = await ValidateCredentialsAsync();
        if (!ok)
            return string.IsNullOrWhiteSpace(baseUrl)
                ? Result.Failure<MalAnime>(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."))
                : Result.Failure<MalAnime>(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));

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
        var restClient = CreateRestClient(animeUri);
        var restRequest = new RestRequest { Method = Method.Get };
        restRequest.AddHeader("Authorization", $"Bearer {token}");

        return await ExecuteRequestAsync(restClient, restRequest);
    }

    public async Task<Result> UpdateListStatusAsync(string animeId, string status, int numWatchedEpisodes, int score)
    {
        var (ok, baseUrl, token) = await ValidateCredentialsAsync();
        if (!ok)
            return string.IsNullOrWhiteSpace(baseUrl)
                ? Result.Failure(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."))
                : Result.Failure(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));

        var uri = new Uri($"{baseUrl}/anime/{animeId}/my_list_status");
        var (response, _) = await ExecuteWithAutoRefreshAsync(token, t =>
        {
            var restClient = CreateRestClient(uri);
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

    public async Task<Result<IList<MalSearchResultItem>>> SearchByTitleAsync(string query, int limit = 10)
    {
        var normalised = NormaliseSearchQuery(query);
        if (normalised.Length < MalMinSearchQueryLength)
            return Result.Failure<IList<MalSearchResultItem>>(InvalidSearchQueryError());

        var (ok, baseUrl, token) = await ValidateCredentialsAsync();
        if (!ok)
            return string.IsNullOrWhiteSpace(baseUrl)
                ? Result.Failure<IList<MalSearchResultItem>>(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."))
                : Result.Failure<IList<MalSearchResultItem>>(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));

        var encodedQuery = Uri.EscapeDataString(normalised);
        var effectiveLimit = Math.Clamp(limit, 1, MalMaxSearchLimit);
        var (response, _) = await ExecuteWithAutoRefreshAsync(token, t =>
        {
            var uri = new Uri($"{baseUrl}/anime?q={encodedQuery}&limit={effectiveLimit}&fields=id,title,main_picture,start_date,media_type");
            var restClient = CreateRestClient(uri);
            var restRequest = new RestRequest { Method = Method.Get };
            restRequest.AddHeader("Authorization", $"Bearer {t}");
            return ExecuteRequestAsync(restClient, restRequest);
        });

        if (response.IsSuccessful)
        {
            var result = TryDeserialize<MalResult>(response.Content);
            if (result is null)
                return Result.Failure<IList<MalSearchResultItem>>(new(Constants.Errors.SearchByTitleFailed, UnreadableSearchResponseMessage));

            var items = (result.Data ?? [])
                .Select(d =>
                {
                    // Parse year from "YYYY-MM-DD" start_date — null/empty → null year
                    int? year = null;
                    if (!string.IsNullOrWhiteSpace(d.Node.StartDate)
                        && d.Node.StartDate.Length >= 4
                        && int.TryParse(d.Node.StartDate[..4], out var parsedYear))
                    {
                        year = parsedYear;
                    }

                    return new MalSearchResultItem(
                        d.Node.Id.ToString(),
                        d.Node.Title ?? string.Empty,
                        d.Node.MainPicture?.Medium,
                        year,
                        d.Node.MediaType,
                        false // IsAlreadyInLibrary enriched by the mediator layer
                    );
                })
                .ToList();
            return Result.Success<IList<MalSearchResultItem>>(items);
        }

        return Result.Failure<IList<MalSearchResultItem>>(new(Constants.Errors.SearchByTitleFailed, DescribeSearchFailure(response)));
    }

    public async Task<Result<IList<MalAnime>>> SearchByTitleRichAsync(string query, int limit = 100)
    {
        var normalised = NormaliseSearchQuery(query);
        if (normalised.Length < MalMinSearchQueryLength)
            return Result.Failure<IList<MalAnime>>(InvalidSearchQueryError());

        var (ok, baseUrl, token) = await ValidateCredentialsAsync();
        if (!ok)
            return string.IsNullOrWhiteSpace(baseUrl)
                ? Result.Failure<IList<MalAnime>>(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."))
                : Result.Failure<IList<MalAnime>>(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));

        var encodedQuery = Uri.EscapeDataString(normalised);
        var effectiveLimit = Math.Clamp(limit, 1, MalMaxSearchLimit);

        var (response, _) = await ExecuteWithAutoRefreshAsync(token, t =>
        {
            var uri = new Uri($"{baseUrl}/anime?q={encodedQuery}&limit={effectiveLimit}&nsfw=true&fields={SEASONAL_FIELDS}");
            var restClient = CreateRestClient(uri);
            var restRequest = new RestRequest { Method = Method.Get };
            restRequest.AddHeader("Authorization", $"Bearer {t}");
            return ExecuteRequestAsync(restClient, restRequest);
        });

        if (response.IsSuccessful)
        {
            var result = TryDeserialize<MalResult>(response.Content);
            if (result is null)
                return Result.Failure<IList<MalAnime>>(new(Constants.Errors.SearchByTitleRichFailed, UnreadableSearchResponseMessage));

            var items = (result.Data ?? []).Select(d => d.Node).ToList();
            return Result.Success<IList<MalAnime>>(items);
        }

        return Result.Failure<IList<MalAnime>>(new(Constants.Errors.SearchByTitleRichFailed, DescribeSearchFailure(response)));
    }

    public async Task<Result<MalAnimeCharactersResult>> GetAnimeCharactersAsync(string animeId)
    {
        var (ok, baseUrl, token) = await ValidateCredentialsAsync();
        if (!ok)
            return string.IsNullOrWhiteSpace(baseUrl)
                ? Result.Failure<MalAnimeCharactersResult>(new(Constants.Errors.NotConfigured, $"MyAnimeList is not configured: '{SettingsKeys.MalApiBaseUrl}' is blank."))
                : Result.Failure<MalAnimeCharactersResult>(new(Constants.Errors.NoToken, "MyAnimeList access token is not available"));

        const string fields = "characters{role,node{id,first_name,last_name,main_picture},voice_actors{language,node{id,first_name,last_name,main_picture}}}";
        var (response, _) = await ExecuteWithAutoRefreshAsync(token, t =>
        {
            var uri = new Uri($"{baseUrl}/anime/{animeId}?fields={fields}");
            var restClient = CreateRestClient(uri);
            var restRequest = new RestRequest { Method = Method.Get };
            restRequest.AddHeader("Authorization", $"Bearer {t}");
            return ExecuteRequestAsync(restClient, restRequest);
        });

        if (!response.IsSuccessful)
        {
            MalError? malErr = null;
            try { malErr = JsonSerializer.Deserialize<MalError>(response.Content!); } catch { }
            return Result.Failure<MalAnimeCharactersResult>(new Error(malErr?.Message ?? response.StatusCode.ToString()));
        }

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<MalAnimeCharactersResponse>(response.Content!, opts);
        if (raw is null)
            return Result.Success(new MalAnimeCharactersResult(animeId, []));

        var characters = raw.Characters.Select(c =>
        {
            var charName = string.Join(" ", new[] { c.Node?.FirstName, c.Node?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var vas = c.VoiceActors.Select(va =>
            {
                var vaName = string.Join(" ", new[] { va.Node?.FirstName, va.Node?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                return new MalVoiceActorInfo(va.Node?.Id ?? 0, vaName, va.Node?.MainPicture?.Medium, va.Language ?? "Japanese");
            }).ToList();
            return new MalCharacterInfo(c.Node?.Id ?? 0, charName, c.Node?.MainPicture?.Medium, c.Role ?? "Supporting", vas);
        }).Where(c => c.CharacterId > 0).ToList();

        return Result.Success(new MalAnimeCharactersResult(animeId, characters));
    }
    #endregion

    #region Private
    /// <summary>
    /// Fetches and validates the MAL API base URL and access token.
    /// Returns <c>(true, baseUrl, token)</c> on success.
    /// On failure, <c>ok</c> is false; a non-empty <c>baseUrl</c> in a failing tuple means
    /// the URL was present but the token was missing.
    /// </summary>
    private async Task<(bool ok, string baseUrl, string token)> ValidateCredentialsAsync()
    {
        var baseUrl = await GetBaseUrlAsync() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl)) return (false, string.Empty, string.Empty);
        var token = await malTokenService.GetAccessTokenAsync() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return (false, baseUrl, string.Empty);
        return (true, baseUrl, token);
    }

    private async Task<string?> GetBaseUrlAsync()
        => (await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))?.Value;

    /// <summary>
    /// Strips characters MAL's <c>q</c> parameter handles poorly (e.g. the colon in
    /// "Dragon Ball Super: Galactic Patrol") and collapses whitespace.
    /// </summary>
    private static string NormaliseSearchQuery(string? query)
        => WhitespaceRunRegex.Replace(SearchNoiseRegex.Replace(query ?? string.Empty, " "), " ").Trim();

    private static Error InvalidSearchQueryError()
        => new(Constants.Errors.InvalidSearchQuery,
               $"The search query must be at least {MalMinSearchQueryLength} characters long once special characters (: ? !) are removed (MAL API requirement).");

    /// <summary>
    /// Deserialises MAL JSON without throwing: returns <c>null</c> for an empty or malformed body
    /// (e.g. an HTML error page returned by a gateway).
    /// </summary>
    private static T? TryDeserialize<T>(string? content) where T : class
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(content);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a readable message for a failed MAL search. MAL normally answers with
    /// <c>{"message":"invalid q","error":"bad_request"}</c>, but a 403 ("DoS detected"), a gateway
    /// failure or a network error may carry an HTML/empty body — or no HTTP status at all.
    /// </summary>
    private static string DescribeSearchFailure(RestResponse response)
    {
        if (response.StatusCode == 0)
            return $"Could not reach MyAnimeList: {response.ErrorMessage ?? "no response received"}";

        var malError = TryDeserialize<MalError>(response.Content);
        var detail = string.Join(" / ", new[] { malError?.Detail, malError?.Message }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var message = $"MyAnimeList search failed (HTTP {(int)response.StatusCode} {response.StatusCode})";
        if (detail.Length > 0)
            message += $": {detail}";
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            message += ". MAL may be rate limiting this server; retry in a moment";

        return message;
    }

    private async Task<RestResponse> FetchAnimeListAsync(string baseUrl, Paging paging, string token)
    {
        var pagingUri = paging?.HasNextPage == true ? paging.Next : null;
        // Use EXTENDED_REQUEST_FIELDS to get my_list_status data (needed for viewState, addedDate, progression, rating)
        var animeListUri = new Uri(pagingUri ?? $"{baseUrl}/users/@me/animelist?nsfw=true&limit=1000&fields={EXTENDED_REQUEST_FIELDS}");

        var restClient = CreateRestClient(animeListUri);
        var restRequest = new RestRequest { Method = Method.Get };
        restRequest.AddHeader("Authorization", $"Bearer {token}");

        return await ExecuteRequestAsync(restClient, restRequest);
    }

    private async Task<RestResponse> FetchAnimeByIdAsync(string baseUrl, string id, string token)
    {
        var animeUri = new Uri($"{baseUrl}/anime/{id}?fields={EXTENDED_REQUEST_FIELDS}");
        var restClient = CreateRestClient(animeUri);
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
