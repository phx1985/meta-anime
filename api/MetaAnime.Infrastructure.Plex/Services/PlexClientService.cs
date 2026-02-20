using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using Microsoft.Extensions.Options;

namespace MetaAnime.Infrastructure.Plex.Services
{
    public class PlexClientService(IPlexAPI plexAPI, IOptions<PlexSettings> options, IHttpClientFactory httpClientFactory) : IPlexClientService
    {
        private const string PlexTvApiUrl = "https://plex.tv/api/v2";
        private const int NavigateCommandId = 1;
        public const string HttpClientName = "PlexNavigate";

        private readonly string plexHost = options.Value.Host;
        private readonly string plexToken = options.Value.Token;
        private readonly string plexClientIdentifier = options.Value.ClientIdentifier;

        public async Task<Result<IList<PlexClientDevice>>> GetAvailableClientsAsync()
        {
            try
            {
                var req = new GetServerResourcesRequest
                {
                    ClientIdentifier = plexClientIdentifier,
                    IncludeHttps = IncludeHttps.True,
                    IncludeRelay = IncludeRelay.True
                };

                var response = await plexAPI.Plex.GetServerResourcesAsync(req, serverUrl: PlexTvApiUrl);

                var devices = response.PlexDevices?
                    .Where(d => d.Provides.Contains("player") && d.Presence)
                    .Select(d => new PlexClientDevice
                    {
                        ClientIdentifier = d.ClientIdentifier,
                        Name = d.Name,
                        Product = d.Product,
                        Platform = d.Platform ?? string.Empty,
                        Presence = d.Presence
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
    }
}
