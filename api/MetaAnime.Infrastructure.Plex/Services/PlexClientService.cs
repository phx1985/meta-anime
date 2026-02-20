using LukeHagar.PlexAPI.SDK;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using Microsoft.Extensions.Options;
using System.Xml.Linq;

namespace MetaAnime.Infrastructure.Plex.Services
{
    public class PlexClientService(IPlexAPI plexAPI, IOptions<PlexSettings> options, IHttpClientFactory httpClientFactory) : IPlexClientService
    {
        private const string PlexTvDevicesUrl = "https://plex.tv/devices.xml";
        private const int NavigateCommandId = 1;
        public const string HttpClientName = "PlexNavigate";

        private readonly string plexHost = options.Value.Host;
        private readonly string plexToken = options.Value.Token;

        public async Task<Result<IList<PlexClientDevice>>> GetAvailableClientsAsync()
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient(HttpClientName);
                var request = new HttpRequestMessage(HttpMethod.Get, PlexTvDevicesUrl);
                request.Headers.Add("X-Plex-Token", plexToken);

                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var xml = await response.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(xml);

                var devices = doc.Root?
                    .Elements("Device")
                    .Select(d => new PlexClientDevice
                    {
                        ClientIdentifier = d.Attribute("clientIdentifier")?.Value ?? string.Empty,
                        Name = d.Attribute("name")?.Value ?? string.Empty,
                        Product = d.Attribute("product")?.Value ?? string.Empty,
                        Platform = d.Attribute("platform")?.Value ?? string.Empty,
                        Provides = d.Attribute("provides")?.Value ?? string.Empty,
                        LastSeen = ParseDateTime(d.Attribute("lastSeenAt")?.Value)
                    })
                    .Where(d => !string.IsNullOrEmpty(d.ClientIdentifier)
                             && !d.Provides.Equals("server", StringComparison.OrdinalIgnoreCase))
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

        private static DateTime? ParseDateTime(string? unixSeconds)
        {
            if (string.IsNullOrEmpty(unixSeconds) || !long.TryParse(unixSeconds, out var ts))
                return null;
            return DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
        }
    }
}
