using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using RestSharp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Services;

public sealed class MalOAuthService(
    IPropertyBagRepository propertyBagRepository,
    IMalTokenService malTokenService,
    IHttpClientFactory httpClientFactory) : IMalOAuthService
{
    private const string TokenUrl = "https://myanimelist.net/v1/oauth2/token";
    private const string ClientName = "MAL";
    private const int CodeVerifierLength = 64;

    /// <summary>
    /// Creates a RestSharp client backed by the pooled <see cref="IHttpClientFactory"/> client
    /// so requests flow through the Aspire standard resilience handler instead of allocating
    /// a fresh socket pool per call.
    /// </summary>
    private RestClient CreateRestClient(Uri uri)
        => new(httpClientFactory.CreateClient(ClientName), new RestClientOptions { BaseUrl = uri });

    public async Task<Result<string>> GetAuthorizationUrlAsync(string redirectUri)
    {
        var clientIdEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalClientId);
        var clientId = clientIdEntry?.Value;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Result.Failure<string>(new Error(Constants.Errors.MissingClientId,
                "MyAnimeList Client ID is not configured. Set it in appsettings or the configuration file."));
        }

        var webBaseEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalWebBaseUrl);
        var webBase = (webBaseEntry?.Value ?? "https://myanimelist.net").TrimEnd('/');

        // Generate PKCE values
        var codeVerifier = GenerateCodeVerifier();
        var state = GenerateState();

        // Persist for later verification
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalOAuthCodeVerifier, codeVerifier);
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalOAuthState, state);
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalOAuthRedirectUri, redirectUri);

        // MAL uses "plain" code challenge method (challenge == verifier)
        var authUrl = $"{webBase}/v1/oauth2/authorize" +
                      $"?response_type=code" +
                      $"&client_id={Uri.EscapeDataString(clientId)}" +
                      $"&code_challenge={Uri.EscapeDataString(codeVerifier)}" +
                      $"&state={Uri.EscapeDataString(state)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";

        return Result.Success(authUrl);
    }

    public async Task<Result> ExchangeCodeAsync(string code, string state, string redirectUri)
    {
        // Verify state
        var storedStateEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalOAuthState);
        if (storedStateEntry?.Value != state)
        {
            return Result.Failure(new Error(Constants.Errors.AuthenticationFailed,
                "OAuth state mismatch — the request may have been tampered with."));
        }

        var codeVerifierEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalOAuthCodeVerifier);
        var codeVerifier = codeVerifierEntry?.Value;
        if (string.IsNullOrWhiteSpace(codeVerifier))
        {
            return Result.Failure(new Error(Constants.Errors.AuthenticationFailed,
                "OAuth code verifier not found. Please restart the authorization flow."));
        }

        var clientIdEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalClientId);
        var clientId = clientIdEntry?.Value;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Result.Failure(new Error(Constants.Errors.MissingClientId,
                "MyAnimeList Client ID is not configured."));
        }

        var clientSecretEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalClientSecret);
        var clientSecret = clientSecretEntry?.Value;

        var client = CreateRestClient(new Uri(TokenUrl));
        var request = new RestRequest { Method = Method.Post };
        request.AddParameter("client_id", clientId);
        if (!string.IsNullOrWhiteSpace(clientSecret))
            request.AddParameter("client_secret", clientSecret);
        request.AddParameter("code", code);
        request.AddParameter("code_verifier", codeVerifier);
        request.AddParameter("grant_type", "authorization_code");
        request.AddParameter("redirect_uri", redirectUri);

        RestResponse response;
        try
        {
            response = await client.ExecuteAsync(request);
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(Constants.Errors.RefreshError,
                $"Error calling MAL token endpoint: {ex.Message}"));
        }

        if (!response.IsSuccessful || response.Content is null)
        {
            return Result.Failure(new Error(Constants.Errors.RefreshFailed,
                $"MAL authorization failed: {response.StatusCode} {response.Content}"));
        }

        var tokenResponse = JsonSerializer.Deserialize<MalTokenResponse>(response.Content);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            return Result.Failure(new Error(Constants.Errors.InvalidResponse,
                "MAL token response was invalid."));
        }

        // Persist tokens
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalAccessToken, tokenResponse.AccessToken);
        if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            await propertyBagRepository.UpsertAsync(SettingsKeys.MalRefreshTokenInternal, tokenResponse.RefreshToken);
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalTokenRefreshedAt, DateTime.UtcNow.ToString("O"));

        // Clean up temporary OAuth state
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalOAuthState, string.Empty);
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalOAuthCodeVerifier, string.Empty);

        return Result.Success();
    }

    public async Task<Result> DisconnectAsync()
    {
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalAccessToken, string.Empty);
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalRefreshTokenInternal, string.Empty);
        await propertyBagRepository.UpsertAsync(SettingsKeys.MalTokenRefreshedAt, string.Empty);
        return Result.Success();
    }

    public async Task<bool> IsConnectedAsync()
        => await malTokenService.IsConnectedAsync();

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(CodeVerifierLength);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

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
