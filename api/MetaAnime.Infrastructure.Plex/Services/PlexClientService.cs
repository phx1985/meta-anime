using LukeHagar.PlexAPI.SDK;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.Plex.Services
{
    public class PlexClientService(IPlexAPI plexAPI, IOptions<PlexSettings> options, IHttpClientFactory httpClientFactory) : IPlexClientService
    {
        private const int NavigateCommandId = 1;
        public const string HttpClientName = "PlexNavigate";

        private readonly string plexHost = options.Value.Host;
        private readonly string plexToken = options.Value.Token;

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public async Task<Result<IList<PlexClientDevice>>> GetAvailableClientsAsync()
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient(HttpClientName);
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{plexHost}/clients");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("X-Plex-Token", plexToken);

                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var parsed = JsonSerializer.Deserialize<PmsClientsResponse>(json, JsonOpts);

                var devices = parsed?.MediaContainer?.Server?
                    .Select(c => new PlexClientDevice
                    {
                        ClientIdentifier = c.MachineIdentifier,
                        Name = c.Name,
                        Product = c.Product ?? string.Empty,
                        Platform = c.Platform ?? string.Empty,
                        DeviceClass = c.DeviceClass ?? string.Empty
                    })
                    .ToList() ?? [];

                return Result.Success<IList<PlexClientDevice>>(devices);
            }
            catch (Exception ex)
            {
                return Result.Failure<IList<PlexClientDevice>>(new("Plex.GetDevices.Failed", ex.Message));
            }
        }

        public async Task<Result> NavigateToSeriesAsync(string plexSeriesId, string clientIdentifier)
        {
            try
            {
                var identityResponse = await plexAPI.General.GetIdentityAsync();
                var machineIdentifier = identityResponse.Object?.MediaContainer?.MachineIdentifier;

                if (string.IsNullOrEmpty(machineIdentifier))
                {
                    return Result.Failure(new Error("Plex.Navigate.ServerNotFound", "Could not retrieve server machine identifier."));
                }

                var httpClient = httpClientFactory.CreateClient(HttpClientName);
                var key = Uri.EscapeDataString($"/library/metadata/{plexSeriesId}");
                var targetClient = Uri.EscapeDataString(clientIdentifier);
                var url = $"{plexHost}/player/navigation/navigateTo" +
                          $"?commandID={NavigateCommandId}" +
                          $"&X-Plex-Target-Client-Identifier={targetClient}" +
                          $"&key={key}" +
                          $"&machineIdentifier={Uri.EscapeDataString(machineIdentifier)}" +
                          $"&X-Plex-Token={plexToken}";

                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure(new Error("Plex.Navigate.Failed", $"Navigation command failed: HTTP {(int)response.StatusCode}"));
                }

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Plex.Navigate.Failed", ex.Message));
            }
        }

        #region Private response models

        private sealed class PmsClientsResponse
        {
            public PmsClientsMediaContainer? MediaContainer { get; set; }
        }

        private sealed class PmsClientsMediaContainer
        {
            // Plex returns connected clients as "Server" elements (a known PMS API quirk)
            public List<PmsClientEntry>? Server { get; set; }
        }

        private sealed class PmsClientEntry
        {
            [JsonPropertyName("machineIdentifier")]
            public string MachineIdentifier { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("product")]
            public string? Product { get; set; }

            [JsonPropertyName("platform")]
            public string? Platform { get; set; }

            [JsonPropertyName("deviceClass")]
            public string? DeviceClass { get; set; }
        }

        #endregion
    }
}
