using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using RestSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Services;

public sealed class MalTokenService(
    IPropertyBagRepository propertyBagRepository) : IMalTokenService
{
    private const string AccessTokenKey = "MAL_ACCESS_TOKEN";
    private const string RefreshTokenKey = "MAL_REFRESH_TOKEN";
    private const string TokenUrl = "https://myanimelist.net/v1/oauth2/token";

    public async Task<string?> GetAccessTokenAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(AccessTokenKey);
        if (entry is not null)
        {
            return entry.Value;
        }

        // Fall back to legacy API token stored in PropertyBag
        var legacyEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalApiToken);
        return string.IsNullOrWhiteSpace(legacyEntry?.Value) ? null : legacyEntry.Value;
    }

    public async Task<Result> RefreshTokenAsync()
    {
        var clientIdEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalClientId);
        var clientId = clientIdEntry?.Value;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Result.Failure(new Error(Constants.Errors.MissingClientId, "MyAnimeList ClientId is not configured"));
        }

        var refreshToken = await GetCurrentRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Failure(new Error(Constants.Errors.MissingRefreshToken, "No MAL refresh token available"));
        }

        var clientSecretEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalClientSecret);
        var clientSecret = clientSecretEntry?.Value;

        var client = new RestClient(new Uri(TokenUrl));
        var request = new RestRequest { Method = Method.Post };
        request.AddParameter("client_id", clientId);
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            request.AddParameter("client_secret", clientSecret);
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
            return Result.Failure(new Error(Constants.Errors.RefreshError, $"Error calling MAL token endpoint: {ex.Message}"));
        }

        if (!response.IsSuccessful || response.Content is null)
        {
            return Result.Failure(new Error(Constants.Errors.RefreshFailed, $"MAL token refresh failed: {response.StatusCode} {response.Content}"));
        }

        var tokenResponse = JsonSerializer.Deserialize<MalTokenResponse>(response.Content);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            return Result.Failure(new Error(Constants.Errors.InvalidResponse, "MAL token response was invalid"));
        }

        await propertyBagRepository.UpsertAsync(AccessTokenKey, tokenResponse.AccessToken);
        if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            await propertyBagRepository.UpsertAsync(RefreshTokenKey, tokenResponse.RefreshToken);
        }

        await propertyBagRepository.UpsertAsync(
            Core.SettingsKeys.MalTokenRefreshedAt,
            DateTime.UtcNow.ToString("O"));

        return Result.Success();
    }

    private async Task<string?> GetCurrentRefreshTokenAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(RefreshTokenKey);
        if (entry is not null)
        {
            return entry.Value;
        }

        // Fall back to initial refresh token stored in PropertyBag (seeded from config)
        var seededEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalRefreshToken);
        return seededEntry?.Value;
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
