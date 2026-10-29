namespace MetaAnime.Application.Contracts;

/// <summary>
/// Portable configuration backup payload (export/import). Null fields are
/// treated as "leave unchanged" on import. Sensitive values are exported
/// decrypted (true portable bootstrap) and re-encrypted on import.
/// Admin password hash / JWT secrets / OAuth transient state are deliberately
/// NOT part of this payload.
/// </summary>
public sealed record AppConfigBackupDto(
    int? ConfigVersion,
    string? ExportedAt,
    PlexConfigBackup? Plex,
    SonarrConfigBackup? Sonarr,
    MalConfigBackup? Mal,
    BotKunConfigBackup? BotKun,
    AppSettingsBackup? App,
    PathMapBackup? PathMap,
    HandBrakeBackup? HandBrake,
    LibraryAnalysisBackup? LibraryAnalysis);

public sealed record PlexConfigBackup(
    bool? Enabled,
    string? Host,
    string? Token,
    string? AnimeSections,
    string? MachineIdentifier);

public sealed record SonarrConfigBackup(
    bool? Enabled,
    string? Host,
    string? ApiKey,
    string? RemoteUrl,
    string? AnimeTags,
    string? DefaultRootFolder,
    string? DefaultQualityProfileName,
    bool? DefaultSeasonFolder,
    string? DefaultMonitor);

/// <summary>
/// MAL client credentials plus the current OAuth2 tokens so a restored install
/// does not need to re-run the PKCE flow while tokens remain valid.
/// </summary>
public sealed record MalConfigBackup(
    string? ClientId,
    string? ClientSecret,
    string? ApiToken,
    string? AccessToken,
    string? RefreshToken);

public sealed record BotKunConfigBackup(
    bool? Enabled,
    string? BaseUrl,
    string? ApiKey,
    string? Model);

public sealed record AppSettingsBackup(
    bool? ShowNsfw,
    string? SyncInterval,
    string? TitleSimilarityThreshold,
    string? DownloadStatsCacheInterval,
    string? KometaAnimeIdsJsonUrl,
    bool? LuckyOnlyFinished,
    bool? LuckyOnlyPlanToWatch,
    bool? LuckyOnlyWithFiles);

public sealed record PathMapBackup(
    string? ContainerRoot,
    string? HostRoot,
    string? Separator);

public sealed record HandBrakeBackup(
    string? PresetName,
    string? DestDir,
    string? DestMode);

public sealed record LibraryAnalysisBackup(
    string? AvgEpSizeMb,
    string? AvgGibPerHour);

/// <summary>Result of an import: what was written + fresh provider connectivity.</summary>
public sealed record AppConfigImportResponse(
    int ImportedValues,
    bool PlexConnected,
    bool MalConnected,
    bool SonarrConnected,
    bool RequiresAuth);
