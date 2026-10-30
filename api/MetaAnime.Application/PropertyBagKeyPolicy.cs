using MetaAnime.Core;

namespace MetaAnime.Application;

/// <summary>
/// Policy for the generic property-bag / settings write API.
/// Internal <c>System:*</c> keys (admin password hash, JWT secret, transient OAuth
/// state, …) must never be exposed or overwritten through the generic property-bag
/// endpoints; the write path additionally restricts upserts to the known settings /
/// property keys the app seeds or the UI writes, so arbitrary keys cannot be planted.
/// </summary>
public static class PropertyBagKeyPolicy
{
    /// <summary>
    /// True for internal <c>System:*</c> keys that are managed exclusively by the
    /// backend. They are excluded from the generic property-bag API entirely.
    /// </summary>
    public static bool IsSystemManaged(string key) =>
        key.StartsWith("System:", StringComparison.Ordinal);

    /// <summary>
    /// The known settings / property keys the app seeds or uses (from
    /// <see cref="SettingsKeys"/>) plus the keys the Angular UI writes directly.
    /// Internal key groups (System:*, Cache:*, MAL_*_TOKEN) are deliberately absent
    /// except System:SessionLifetimeDays, which the settings UI manages.
    /// </summary>
    public static readonly HashSet<string> AllowedUpsertKeys =
    [
        // Plex
        SettingsKeys.PlexEnabled,
        SettingsKeys.PlexHost,
        SettingsKeys.PlexToken,
        SettingsKeys.PlexAnimeSections,
        SettingsKeys.PlexMachineIdentifier,

        // Sonarr
        SettingsKeys.SonarrEnabled,
        SettingsKeys.SonarrHost,
        SettingsKeys.SonarrApiKey,
        SettingsKeys.SonarrAnimeTags,
        SettingsKeys.SonarrRemoteUrl,
        SettingsKeys.SonarrDefaultRootFolder,
        SettingsKeys.SonarrDefaultQualityProfileName,
        SettingsKeys.SonarrDefaultSeasonFolder,
        SettingsKeys.SonarrDefaultMonitor,

        // Library analysis
        SettingsKeys.LibraryAnalysisAvgEpSizeMb,
        SettingsKeys.LibraryAnalysisAvgGibPerHour,

        // MyAnimeList
        SettingsKeys.MalApiBaseUrl,
        SettingsKeys.MalWebBaseUrl,
        SettingsKeys.MalApiToken,
        SettingsKeys.MalClientId,
        SettingsKeys.MalClientSecret,
        SettingsKeys.MalRefreshToken,

        // Kometa
        SettingsKeys.KometaAnimeIdsJsonUrl,

        // App preferences
        SettingsKeys.AppShowNsfw,
        SettingsKeys.AppSyncInterval,
        SettingsKeys.AppTitleSimilarityThreshold,
        SettingsKeys.DownloadStatsCacheInterval,
        SettingsKeys.LuckyOnlyFinished,
        SettingsKeys.LuckyOnlyPlanToWatch,
        SettingsKeys.LuckyOnlyWithFiles,

        // Cross-link mappings (user-confirmed bridges)
        SettingsKeys.CrossLinks,

        // Bot-kun AI
        SettingsKeys.BotKunEnabled,
        SettingsKeys.BotKunBaseUrl,
        SettingsKeys.BotKunApiKey,
        SettingsKeys.BotKunModel,

        // User path mapping / HandBrake export / transcode preferences
        SettingsKeys.PathMapContainerRoot,
        SettingsKeys.PathMapHostRoot,
        SettingsKeys.PathMapSeparator,
        SettingsKeys.HandBrakePresetName,
        SettingsKeys.HandBrakeDestDir,
        SettingsKeys.HandBrakeDestMode,
        SettingsKeys.TranscodeIncludeUnknown,

        // Session lifetime (managed by the settings security section)
        SettingsKeys.SessionLifetimeDays,

        // Keys the Angular UI writes through /property-bag (legacy localStorage keys)
        "meta-anime:filterPresets",
        "meta-anime:seasonalFilterPresets",
        "meta-anime:autoApplyLastFilters",
        "meta-anime:lastUsedFilters",
        "meta-anime:seasonalLastUsedFilters",
        "meta-anime:welcomeShown",
    ];
}
