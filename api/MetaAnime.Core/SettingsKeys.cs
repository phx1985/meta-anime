namespace MetaAnime.Core;

/// <summary>
/// Keys used in the PropertyBag for application configuration.
/// On first run these are seeded from the environment / appsettings;
/// afterwards the PropertyBag is the source of truth.
/// </summary>
public static class SettingsKeys
{
    // Plex
    public const string PlexHost = "Plex:Host";
    public const string PlexToken = "Plex:Token";
    public const string PlexAnimeSections = "Plex:AnimeSections";

    // Sonarr
    public const string SonarrHost = "Sonarr:Host";
    public const string SonarrApiKey = "Sonarr:ApiKey";
    public const string SonarrAnimeTags = "Sonarr:AnimeTags";
    public const string SonarrRemoteUrl = "Sonarr:RemoteUrl";

    // MyAnimeList
    public const string MalApiToken = "MAL:ApiToken";
    public const string MalClientId = "MAL:ClientId";
    public const string MalClientSecret = "MAL:ClientSecret";
    public const string MalRefreshToken = "MAL:RefreshToken";

    // Kometa
    public const string KometaAnimeIdsJsonUrl = "Kometa:AnimeIdsJsonUrl";

    // App preferences
    public const string AppShowNsfw                   = "App:ShowNsfw";
    public const string AppSyncInterval               = "App:SyncInterval";
    public const string AppTitleSimilarityThreshold   = "App:TitleSimilarityThreshold";

    // System (not user-configurable)
    public const string LastSyncDate           = "System:LastSyncDate";
    public const string MalTokenRefreshedAt    = "System:MalTokenRefreshedAt";

    // Seasonal manual-add tracking (JSON in property bag; not user-configurable via Settings UI)
    // Seasonal:SonarrAdded  — JSON Dictionary<malId, sonarrId> of manually added Sonarr series
    // Seasonal:MalAdded     — JSON List<malId> of manually added MAL entries
    public const string SeasonalSonarrAdded    = "Seasonal:SonarrAdded";
    public const string SeasonalMalAdded       = "Seasonal:MalAdded";
}
