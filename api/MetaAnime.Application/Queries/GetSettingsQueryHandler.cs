using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetSettingsQueryHandler(
    IPropertyBagRepository propertyBagRepository,
    IMalTokenService malTokenService,
    IPlexAuthService plexAuthService)
    : IRequestHandler<GetSettingsQuery, Result<IEnumerable<SettingDefinition>>>
{
    public async ValueTask<Result<IEnumerable<SettingDefinition>>> Handle(GetSettingsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var allEntries = (await propertyBagRepository.GetAllAsync())
                .ToDictionary(e => e.Key, e => e.Value);

            var malConnected = await malTokenService.IsConnectedAsync();
            var plexConnected = await plexAuthService.IsConnectedAsync();

            var definitions = BuildDefinitions(allEntries, malConnected, plexConnected);
            return Result.Success(definitions);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<SettingDefinition>>(new(Constants.Errors.GetAllPropertyBag, ex.Message));
        }
    }

    private static IEnumerable<SettingDefinition> BuildDefinitions(
        Dictionary<string, string> bag,
        bool malConnected,
        bool plexConnected)
    {
        string Get(string key) => bag.TryGetValue(key, out var v) ? v : string.Empty;

        return
        [
            // ── Plex ──────────────────────────────────────────────────────────
            new(SettingsKeys.PlexEnabled, "plex", false, false, Get(SettingsKeys.PlexEnabled), "boolean"),
            new(SettingsKeys.PlexHost, "plex", false, true, Get(SettingsKeys.PlexHost)),
            new(SettingsKeys.PlexAnimeSections, "plex", false, true, Get(SettingsKeys.PlexAnimeSections)),
            new(SettingsKeys.PlexMachineIdentifier, "plex", false, false, Get(SettingsKeys.PlexMachineIdentifier), "info"),
            // Enrollment status (read-only, managed via /plex/auth endpoints)
            new("Plex:Connected", "plex", false, false, plexConnected ? "true" : "false", "enrollment"),

            // ── Sonarr ────────────────────────────────────────────────────────
            new(SettingsKeys.SonarrEnabled, "sonarr", false, false, Get(SettingsKeys.SonarrEnabled), "boolean"),
            new(SettingsKeys.SonarrHost, "sonarr", false, true, Get(SettingsKeys.SonarrHost)),
            new(SettingsKeys.SonarrApiKey, "sonarr", true, true, Get(SettingsKeys.SonarrApiKey)),
            new(SettingsKeys.SonarrAnimeTags, "sonarr", false, true, Get(SettingsKeys.SonarrAnimeTags)),
            new(SettingsKeys.SonarrRemoteUrl, "sonarr", false, false, Get(SettingsKeys.SonarrRemoteUrl)),

            // ── Sonarr "Add Series" defaults ──────────────────────────────────
            new(SettingsKeys.SonarrDefaultRootFolder, "sonarr", false, false, Get(SettingsKeys.SonarrDefaultRootFolder)),
            new(SettingsKeys.SonarrDefaultQualityProfileName, "sonarr", false, false, Get(SettingsKeys.SonarrDefaultQualityProfileName)),
            new(SettingsKeys.SonarrDefaultSeasonFolder, "sonarr", false, false, Get(SettingsKeys.SonarrDefaultSeasonFolder), "boolean"),
            new(SettingsKeys.SonarrDefaultMonitor, "sonarr", false, false, Get(SettingsKeys.SonarrDefaultMonitor), "select",
                SelectOptions: ["all", "future", "missing", "existing", "recent", "pilot", "firstSeason", "lastSeason", "monitorSpecials", "unmonitorSpecials", "none"]),

            // ── Library Analysis preferences ──────────────────────────────────
            new(SettingsKeys.LibraryAnalysisAvgEpSizeMb, "sonarr", false, false, Get(SettingsKeys.LibraryAnalysisAvgEpSizeMb), "number"),
            new(SettingsKeys.LibraryAnalysisAvgGibPerHour, "sonarr", false, false, Get(SettingsKeys.LibraryAnalysisAvgGibPerHour), "number"),

            // ── MyAnimeList ───────────────────────────────────────────────────
            new(SettingsKeys.MalClientId, "mal", false, true, Get(SettingsKeys.MalClientId)),
            new(SettingsKeys.MalClientSecret, "mal", true, true, Get(SettingsKeys.MalClientSecret)),
            // Enrollment status (read-only, managed via /mal/auth endpoints)
            new("MAL:Connected", "mal", false, false, malConnected ? "true" : "false", "enrollment"),
            new(SettingsKeys.MalTokenRefreshedAt, "mal", false, false, Get(SettingsKeys.MalTokenRefreshedAt), "info"),

            // ── Kometa ────────────────────────────────────────────────────────
            new(SettingsKeys.KometaAnimeIdsJsonUrl, "kometa", false, true, Get(SettingsKeys.KometaAnimeIdsJsonUrl)),

            // ── App preferences ───────────────────────────────────────────────
            new(SettingsKeys.AppShowNsfw, "app", false, false, Get(SettingsKeys.AppShowNsfw), "boolean"),
            new(SettingsKeys.AppSyncInterval, "app", false, false, Get(SettingsKeys.AppSyncInterval), "select",
                SelectOptions: ["hourly", "every6h", "every12h", "daily", "weekly"]),
            new(SettingsKeys.AppTitleSimilarityThreshold, "app", false, false, Get(SettingsKeys.AppTitleSimilarityThreshold), "number"),

            // ── Bot-kun AI (OpenAI-compatible provider) ───────────────────────
            new(SettingsKeys.BotKunEnabled, "botkun", false, false, Get(SettingsKeys.BotKunEnabled), "boolean"),
            new(SettingsKeys.BotKunBaseUrl, "botkun", false, false, Get(SettingsKeys.BotKunBaseUrl)),
            new(SettingsKeys.BotKunApiKey,  "botkun", true,  false, Get(SettingsKeys.BotKunApiKey)),
            new(SettingsKeys.BotKunModel,   "botkun", false, false, Get(SettingsKeys.BotKunModel)),
        ];
    }
}
