using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MetaAnime.Application.Services;

/// <summary>
/// Seeds the PropertyBag with values from appsettings / environment variables
/// on first run. Existing entries are never overwritten, so the PropertyBag
/// remains the source of truth after the initial seed.
/// </summary>
public class SettingsSeedService(
    IServiceProvider serviceProvider,
    IOptions<PlexSettings> plexOptions,
    IOptions<SonarrSettings> sonarrOptions,
    IOptions<MalSettings> malOptions,
    IOptions<KometaSettings> kometaOptions,
    ILogger<SettingsSeedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();

        var seeds = BuildSeeds();

        foreach (var (key, value) in seeds)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var existing = await repo.GetByKeyAsync(key);
            if (existing is null)
            {
                await repo.UpsertAsync(key, value);
                logger.LogInformation("Settings seed: initialised '{Key}' from environment", key);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Dictionary<string, string?> BuildSeeds()
    {
        var plex = plexOptions.Value;
        var sonarr = sonarrOptions.Value;
        var mal = malOptions.Value;
        var kometa = kometaOptions.Value;

        return new Dictionary<string, string?>
        {
            [SettingsKeys.PlexEnabled] = "true",
            [SettingsKeys.PlexHost] = plex.Host,
            [SettingsKeys.PlexToken] = plex.Token,
            [SettingsKeys.PlexAnimeSections] = plex.AnimeSections,

            [SettingsKeys.SonarrEnabled] = "true",
            [SettingsKeys.SonarrHost] = sonarr.Host,
            [SettingsKeys.SonarrApiKey] = sonarr.ApiKey,
            [SettingsKeys.SonarrAnimeTags] = sonarr.AnimeTags,
            [SettingsKeys.SonarrRemoteUrl] = sonarr.RemoteUrl,

            [SettingsKeys.SonarrDefaultSeasonFolder] = "true",
            [SettingsKeys.SonarrDefaultMonitor] = "all",

            [SettingsKeys.MalApiToken] = mal.ApiToken,
            [SettingsKeys.MalClientId] = mal.ClientId,
            [SettingsKeys.MalClientSecret] = mal.ClientSecret,
            [SettingsKeys.MalRefreshToken] = mal.RefreshToken,

            [SettingsKeys.KometaAnimeIdsJsonUrl] = kometa.AnimeIdsJsonUrl,

            [SettingsKeys.AppShowNsfw] = "false",
            [SettingsKeys.AppSyncInterval] = "daily",
            [SettingsKeys.AppTitleSimilarityThreshold] = "80",
        };
    }
}
