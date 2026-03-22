using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MetaAnime.Application.Services;

/// <summary>
/// Seeds the PropertyBag with values from appsettings / environment variables on startup.
/// Provider credentials (URLs, API keys, Client IDs) are always upserted from configuration,
/// allowing config-file changes to take effect on restart. Default values (toggles, preferences)
/// are only written once so that user changes made via the UI are preserved.
/// </summary>
public sealed class SettingsSeedService(
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

        // Always upsert provider credentials from configuration (if non-empty).
        // This ensures a config-file change is picked up on the next restart.
        var providerSeeds = BuildProviderSeeds();
        foreach (var (key, value) in providerSeeds)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            await repo.UpsertAsync(key, value);
            logger.LogInformation("Settings seed: upserted '{Key}' from configuration", key);
        }

        // Only initialise default values when the key is not yet in the database,
        // so that user changes made via the UI are preserved across restarts.
        var defaultSeeds = BuildDefaultSeeds();
        foreach (var (key, value) in defaultSeeds)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var existing = await repo.GetByKeyAsync(key);
            if (existing is null)
            {
                await repo.UpsertAsync(key, value);
                logger.LogInformation("Settings seed: initialised '{Key}' with default", key);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Third-party provider credentials and URLs sourced from the configuration file.
    /// These are always upserted so that config-file changes take effect on restart.
    /// </summary>
    private Dictionary<string, string?> BuildProviderSeeds()
    {
        var plex = plexOptions.Value;
        var sonarr = sonarrOptions.Value;
        var mal = malOptions.Value;
        var kometa = kometaOptions.Value;

        return new()
        {
            [SettingsKeys.PlexHost] = plex.Host,
            [SettingsKeys.PlexToken] = plex.Token,
            [SettingsKeys.PlexAnimeSections] = plex.AnimeSections,

            [SettingsKeys.SonarrHost] = sonarr.Host,
            [SettingsKeys.SonarrApiKey] = sonarr.ApiKey,
            [SettingsKeys.SonarrAnimeTags] = sonarr.AnimeTags,
            [SettingsKeys.SonarrRemoteUrl] = sonarr.RemoteUrl,

            [SettingsKeys.MalApiToken] = mal.ApiToken,
            [SettingsKeys.MalClientId] = mal.ClientId,
            [SettingsKeys.MalClientSecret] = mal.ClientSecret,
            [SettingsKeys.MalRefreshToken] = mal.RefreshToken,
            [SettingsKeys.MalApiBaseUrl] = mal.ApiBaseUrl,
            [SettingsKeys.MalWebBaseUrl] = mal.WebBaseUrl,

            // Only upsert the Kometa URL when explicitly set in config (null/empty values are skipped in the upsert loop above)
            [SettingsKeys.KometaAnimeIdsJsonUrl] = kometa.AnimeIdsJsonUrl,

            // Always-refreshed app reference data (updated with each release)
            [SettingsKeys.KnownAiApiUrls] = """[{"url":"https://api.openai.com/v1","name":"OpenAI"},{"url":"https://api.groq.com/openai/v1","name":"Groq"},{"url":"http://localhost:11434/v1","name":"Ollama (local)"},{"url":"http://localhost:1234/v1","name":"LM Studio (local)"},{"url":"https://openrouter.ai/api/v1","name":"OpenRouter"},{"url":"https://api.together.xyz/v1","name":"Together AI"}]""",
        };
    }

    /// <summary>
    /// Hardcoded defaults for app preferences and provider toggles.
    /// Written once on first run; subsequent user changes made via the UI are preserved.
    /// </summary>
    private static Dictionary<string, string?> BuildDefaultSeeds() => new()
    {
        [SettingsKeys.PlexEnabled] = "false",
        [SettingsKeys.SonarrEnabled] = "false",
        [SettingsKeys.SonarrDefaultSeasonFolder] = "true",
        [SettingsKeys.SonarrDefaultMonitor] = "all",
        [SettingsKeys.KometaAnimeIdsJsonUrl] = KometaSettings.DefaultAnimeIdsJsonUrl,
        [SettingsKeys.AppShowNsfw] = "false",
        [SettingsKeys.AppSyncInterval] = "daily",
        [SettingsKeys.AppTitleSimilarityThreshold] = "80",
        [SettingsKeys.BotKunEnabled]  = "false",
        [SettingsKeys.BotKunBaseUrl]  = string.Empty,
        [SettingsKeys.BotKunApiKey]   = string.Empty,
        [SettingsKeys.BotKunModel]    = string.Empty,
        // Library analysis alert thresholds
        [SettingsKeys.LibraryAnalysisAvgEpSizeMb] = "1200",
        [SettingsKeys.LibraryAnalysisAvgGibPerHour] = "1.2",
    };
}
