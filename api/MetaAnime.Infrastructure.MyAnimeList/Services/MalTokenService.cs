using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Services;

public class MalTokenService(
    IPropertyBagRepository propertyBagRepository,
    IOptions<MalSettings> options) : IMalTokenService
{
    private const string AccessTokenKey = "MAL_ACCESS_TOKEN";
    private const string RefreshTokenKey = "MAL_REFRESH_TOKEN";
    private const string TokenUrl = "https://myanimelist.net/v1/oauth2/token";

    private readonly MalSettings _settings = options.Value;

    public async Task<string?> GetAccessTokenAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(AccessTokenKey);
        if (entry is not null)
        {
            return entry.Value;
        }

        return string.IsNullOrWhiteSpace(_settings.ApiToken) ? null : _settings.ApiToken;
    }

    public async Task<Result> RefreshTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId))
        {
            return Result.Failure(new Error("MAL.MissingClientId", "MyAnimeList ClientId is not configured"));
        }

        var refreshToken = await GetCurrentRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Failure(new Error("MAL.MissingRefreshToken", "No MAL refresh token available in PropertyBag or settings"));
        }

        var client = new RestClient(new Uri(TokenUrl));
        var request = new RestRequest { Method = Method.Post };
        request.AddParameter("client_id", _settings.ClientId);
        if (!string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            request.AddParameter("client_secret", _settings.ClientSecret);
        }
        request.AddParameter("grant_type", "refresh_token");
        request.AddParameter("refresh_token", refreshToken);

        RestResponse response;
        try
        {
            response = await client.ExecuteAsync(request);
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("MAL.RefreshError", $"Error calling MAL token endpoint: {ex.Message}"));
        }

        if (!response.IsSuccessful || response.Content is null)
        {
            return Result.Failure(new Error("MAL.RefreshFailed", $"MAL token refresh failed: {response.StatusCode} {response.Content}"));
        }

        var tokenResponse = JsonSerializer.Deserialize<MalTokenResponse>(response.Content);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            return Result.Failure(new Error("MAL.InvalidResponse", "MAL token response was invalid"));
        }

        await propertyBagRepository.UpsertAsync(AccessTokenKey, tokenResponse.AccessToken);
        if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            await propertyBagRepository.UpsertAsync(RefreshTokenKey, tokenResponse.RefreshToken);
        }

        return Result.Success();
    }

    private async Task<string?> GetCurrentRefreshTokenAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(RefreshTokenKey);
        if (entry is not null)
        {
            return entry.Value;
        }

        return _settings.RefreshToken;
    }

    private sealed class MalTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
