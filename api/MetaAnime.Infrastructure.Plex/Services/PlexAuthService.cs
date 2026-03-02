using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.Plex.Services;

public sealed class PlexAuthService(
    IPropertyBagRepository propertyBagRepository,
    IHttpClientFactory httpClientFactory) : IPlexAuthService
{
    private const string PlexTvBaseUrl = "https://plex.tv/api/v2";
    private const string PlexAppAuthUrl = "https://app.plex.tv/auth#";
    private const string ProductName = "MetaAnime";

    public async Task<Result<PlexPinData>> InitiatePinAsync()
    {
        var client = httpClientFactory.CreateClient("PlexTV");

        try
        {
            var response = await client.PostAsync(
                $"{PlexTvBaseUrl}/pins?strong=true",
                null);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                return Result.Failure<PlexPinData>(new Error(Constants.Errors.PlexPinFailed,
                    $"Failed to create Plex pin: {response.StatusCode} {body}"));
            }

            var pin = await response.Content.ReadFromJsonAsync<PlexPinResponse>();
            if (pin is null || pin.Id == 0 || string.IsNullOrWhiteSpace(pin.Code))
            {
                return Result.Failure<PlexPinData>(new Error(Constants.Errors.PlexPinFailed,
                    "Plex returned an invalid pin response."));
            }

            // Persist pin id for polling
            await propertyBagRepository.UpsertAsync(SettingsKeys.PlexOAuthPinId, pin.Id.ToString());

            var authUrl = $"{PlexAppAuthUrl}?" +
                          $"clientID={Uri.EscapeDataString(Constants.PlexClientIdentifier)}" +
                          $"&code={Uri.EscapeDataString(pin.Code)}" +
                          $"&context%5Bdevice%5D%5Bproduct%5D={Uri.EscapeDataString(ProductName)}";

            return Result.Success(new PlexPinData(pin.Id, pin.Code, authUrl));
        }
        catch (Exception ex)
        {
            return Result.Failure<PlexPinData>(new Error(Constants.Errors.PlexPinFailed,
                $"Error contacting plex.tv: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> PollPinAsync(int pinId)
    {
        var client = httpClientFactory.CreateClient("PlexTV");

        try
        {
            var response = await client.GetAsync($"{PlexTvBaseUrl}/pins/{pinId}");

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                return Result.Failure<bool>(new Error(Constants.Errors.PlexPinFailed,
                    $"Failed to poll Plex pin: {response.StatusCode} {body}"));
            }

            var pin = await response.Content.ReadFromJsonAsync<PlexPinResponse>();
            if (pin is null)
            {
                return Result.Failure<bool>(new Error(Constants.Errors.PlexPinFailed,
                    "Plex returned an invalid pin poll response."));
            }

            if (string.IsNullOrWhiteSpace(pin.AuthToken))
            {
                // Not yet authorized — caller should poll again
                return Result.Success(false);
            }

            // Token received — store it
            await propertyBagRepository.UpsertAsync(SettingsKeys.PlexToken, pin.AuthToken);
            // Clear the temporary pin id
            await propertyBagRepository.UpsertAsync(SettingsKeys.PlexOAuthPinId, string.Empty);

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            return Result.Failure<bool>(new Error(Constants.Errors.PlexPinFailed,
                $"Error polling plex.tv: {ex.Message}"));
        }
    }

    public async Task<Result> DisconnectAsync()
    {
        await propertyBagRepository.UpsertAsync(SettingsKeys.PlexToken, string.Empty);
        return Result.Success();
    }

    public async Task<bool> IsConnectedAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexToken);
        return !string.IsNullOrWhiteSpace(entry?.Value);
    }

    private sealed class PlexPinResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("authToken")]
        public string? AuthToken { get; set; }
    }
}
