namespace MetaAnime.Core;

/// <summary>
/// Keys used in the PropertyBag for application configuration.
/// On first run these are seeded from the environment / appsettings;
/// afterwards the PropertyBag is the source of truth.
/// </summary>
public static class SettingsKeys
{
    // Plex
    public const string PlexEnabled = "Plex:Enabled";
    public const string PlexHost = "Plex:Host";
    public const string PlexToken = "Plex:Token";
    public const string PlexAnimeSections = "Plex:AnimeSections";

    // Sonarr
    public const string SonarrEnabled = "Sonarr:Enabled";
    public const string SonarrHost = "Sonarr:Host";
    public const string SonarrApiKey = "Sonarr:ApiKey";
    public const string SonarrAnimeTags = "Sonarr:AnimeTags";
    public const string SonarrRemoteUrl = "Sonarr:RemoteUrl";

    // Sonarr "Add Series" defaults
    public const string SonarrDefaultRootFolder = "Sonarr:DefaultRootFolder";
    public const string SonarrDefaultQualityProfileName = "Sonarr:DefaultQualityProfileName";
    public const string SonarrDefaultSeasonFolder = "Sonarr:DefaultSeasonFolder";
    public const string SonarrDefaultMonitor = "Sonarr:DefaultMonitor";

    // MyAnimeList
    public const string MalApiBaseUrl = "MAL:ApiBaseUrl";
    public const string MalWebBaseUrl = "MAL:WebBaseUrl";
    public const string MalApiToken = "MAL:ApiToken";
    public const string MalClientId = "MAL:ClientId";
    public const string MalClientSecret = "MAL:ClientSecret";
    public const string MalRefreshToken = "MAL:RefreshToken";

    // Kometa
    public const string KometaAnimeIdsJsonUrl = "Kometa:AnimeIdsJsonUrl";

    // App preferences
    public const string AppShowNsfw = "App:ShowNsfw";
    public const string AppSyncInterval = "App:SyncInterval";
    public const string AppTitleSimilarityThreshold = "App:TitleSimilarityThreshold";

    // Internal token storage keys (managed exclusively by backend OAuth flows)
    public const string MalAccessToken = "MAL_ACCESS_TOKEN";
    public const string MalRefreshTokenInternal = "MAL_REFRESH_TOKEN";

    // System (not user-configurable)
    public const string LastSyncDate = "System:LastSyncDate";
    public const string MalTokenRefreshedAt = "System:MalTokenRefreshedAt";

    // Internal OAuth state (temporary keys used during enrollment flows)
    public const string MalOAuthState = "System:MalOAuthState";
    public const string MalOAuthCodeVerifier = "System:MalOAuthCodeVerifier";
    public const string MalOAuthRedirectUri = "System:MalOAuthRedirectUri";
    public const string PlexOAuthPinId = "System:PlexOAuthPinId";
}
