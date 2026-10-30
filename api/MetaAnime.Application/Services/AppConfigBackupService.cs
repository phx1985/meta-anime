using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;

namespace MetaAnime.Application.Services;

/// <summary>
/// PropertyBag-backed config backup. The whole app stores configuration as
/// flat key→value entries, so export maps them into the nested DTO and import
/// maps them back. Secrets that are encrypted at rest (BotKun:ApiKey) come out
/// decrypted and go back in protected.
/// </summary>
public sealed class AppConfigBackupService(
    IPropertyBagRepository propertyBagRepository,
    ISecretProtector secretProtector) : IAppConfigBackupService
{
    public async Task<AppConfigBackupDto> ExportAsync(CancellationToken cancellationToken = default)
    {
        var bag = (await propertyBagRepository.GetAllAsync())
            .ToDictionary(e => e.Key, e => e.Value ?? string.Empty, StringComparer.Ordinal);

        string? Get(string key) => bag.TryGetValue(key, out var v) ? Unprotect(key, v) : null;
        bool? GetBool(string key) => bool.TryParse(Get(key), out var b) ? b : null;

        return new AppConfigBackupDto(
            ConfigVersion: 1,
            ExportedAt: DateTime.UtcNow.ToString("O"),
            Plex: new PlexConfigBackup(
                GetBool(SettingsKeys.PlexEnabled),
                Get(SettingsKeys.PlexHost),
                Get(SettingsKeys.PlexToken),
                Get(SettingsKeys.PlexAnimeSections),
                Get(SettingsKeys.PlexMachineIdentifier)),
            Sonarr: new SonarrConfigBackup(
                GetBool(SettingsKeys.SonarrEnabled),
                Get(SettingsKeys.SonarrHost),
                Get(SettingsKeys.SonarrApiKey),
                Get(SettingsKeys.SonarrRemoteUrl),
                Get(SettingsKeys.SonarrAnimeTags),
                Get(SettingsKeys.SonarrDefaultRootFolder),
                Get(SettingsKeys.SonarrDefaultQualityProfileName),
                GetBool(SettingsKeys.SonarrDefaultSeasonFolder),
                Get(SettingsKeys.SonarrDefaultMonitor)),
            Mal: new MalConfigBackup(
                Get(SettingsKeys.MalClientId),
                Get(SettingsKeys.MalClientSecret),
                Get(SettingsKeys.MalApiToken),
                Get(SettingsKeys.MalAccessToken),
                Get(SettingsKeys.MalRefreshTokenInternal)),
            BotKun: new BotKunConfigBackup(
                GetBool(SettingsKeys.BotKunEnabled),
                Get(SettingsKeys.BotKunBaseUrl),
                Get(SettingsKeys.BotKunApiKey),
                Get(SettingsKeys.BotKunModel)),
            App: new AppSettingsBackup(
                GetBool(SettingsKeys.AppShowNsfw),
                Get(SettingsKeys.AppSyncInterval),
                Get(SettingsKeys.AppTitleSimilarityThreshold),
                Get(SettingsKeys.DownloadStatsCacheInterval),
                Get(SettingsKeys.KometaAnimeIdsJsonUrl),
                GetBool(SettingsKeys.LuckyOnlyFinished),
                GetBool(SettingsKeys.LuckyOnlyPlanToWatch),
                GetBool(SettingsKeys.LuckyOnlyWithFiles)),
            PathMap: new PathMapBackup(
                Get(SettingsKeys.PathMapContainerRoot),
                Get(SettingsKeys.PathMapHostRoot),
                Get(SettingsKeys.PathMapSeparator)),
            HandBrake: new HandBrakeBackup(
                Get(SettingsKeys.HandBrakePresetName),
                Get(SettingsKeys.HandBrakeDestDir),
                Get(SettingsKeys.HandBrakeDestMode),
                Get(SettingsKeys.TranscodeIncludeUnknown)),
            LibraryAnalysis: new LibraryAnalysisBackup(
                Get(SettingsKeys.LibraryAnalysisAvgEpSizeMb),
                Get(SettingsKeys.LibraryAnalysisAvgGibPerHour)));
    }

    public async Task<int> ImportAsync(AppConfigBackupDto backup, CancellationToken cancellationToken = default)
    {
        var written = 0;

        async Task Set(string key, string? value)
        {
            if (value is null) return;
            await propertyBagRepository.UpsertAsync(key, Protect(key, value));
            written++;
        }

        Task SetBool(string key, bool? value) => Set(key, value?.ToString().ToLowerInvariant());

        if (backup.Plex is { } plex)
        {
            await SetBool(SettingsKeys.PlexEnabled, plex.Enabled);
            await Set(SettingsKeys.PlexHost, plex.Host);
            await Set(SettingsKeys.PlexToken, plex.Token);
            await Set(SettingsKeys.PlexAnimeSections, plex.AnimeSections);
            await Set(SettingsKeys.PlexMachineIdentifier, plex.MachineIdentifier);
        }

        if (backup.Sonarr is { } sonarr)
        {
            await SetBool(SettingsKeys.SonarrEnabled, sonarr.Enabled);
            await Set(SettingsKeys.SonarrHost, sonarr.Host);
            await Set(SettingsKeys.SonarrApiKey, sonarr.ApiKey);
            await Set(SettingsKeys.SonarrRemoteUrl, sonarr.RemoteUrl);
            await Set(SettingsKeys.SonarrAnimeTags, sonarr.AnimeTags);
            await Set(SettingsKeys.SonarrDefaultRootFolder, sonarr.DefaultRootFolder);
            await Set(SettingsKeys.SonarrDefaultQualityProfileName, sonarr.DefaultQualityProfileName);
            await SetBool(SettingsKeys.SonarrDefaultSeasonFolder, sonarr.DefaultSeasonFolder);
            await Set(SettingsKeys.SonarrDefaultMonitor, sonarr.DefaultMonitor);
        }

        if (backup.Mal is { } mal)
        {
            await Set(SettingsKeys.MalClientId, mal.ClientId);
            await Set(SettingsKeys.MalClientSecret, mal.ClientSecret);
            await Set(SettingsKeys.MalApiToken, mal.ApiToken);
            await Set(SettingsKeys.MalAccessToken, mal.AccessToken);
            await Set(SettingsKeys.MalRefreshTokenInternal, mal.RefreshToken);
            if (mal.AccessToken is not null || mal.RefreshToken is not null)
                await Set(SettingsKeys.MalTokenRefreshedAt, DateTime.UtcNow.ToString("O"));
        }

        if (backup.BotKun is { } botKun)
        {
            await SetBool(SettingsKeys.BotKunEnabled, botKun.Enabled);
            await Set(SettingsKeys.BotKunBaseUrl, botKun.BaseUrl);
            await Set(SettingsKeys.BotKunApiKey, botKun.ApiKey);
            await Set(SettingsKeys.BotKunModel, botKun.Model);
        }

        if (backup.App is { } app)
        {
            await SetBool(SettingsKeys.AppShowNsfw, app.ShowNsfw);
            await Set(SettingsKeys.AppSyncInterval, app.SyncInterval);
            await Set(SettingsKeys.AppTitleSimilarityThreshold, app.TitleSimilarityThreshold);
            await Set(SettingsKeys.DownloadStatsCacheInterval, app.DownloadStatsCacheInterval);
            await Set(SettingsKeys.KometaAnimeIdsJsonUrl, app.KometaAnimeIdsJsonUrl);
            await SetBool(SettingsKeys.LuckyOnlyFinished, app.LuckyOnlyFinished);
            await SetBool(SettingsKeys.LuckyOnlyPlanToWatch, app.LuckyOnlyPlanToWatch);
            await SetBool(SettingsKeys.LuckyOnlyWithFiles, app.LuckyOnlyWithFiles);
        }

        if (backup.PathMap is { } pathMap)
        {
            await Set(SettingsKeys.PathMapContainerRoot, pathMap.ContainerRoot);
            await Set(SettingsKeys.PathMapHostRoot, pathMap.HostRoot);
            await Set(SettingsKeys.PathMapSeparator, pathMap.Separator);
        }

        if (backup.HandBrake is { } handBrake)
        {
            await Set(SettingsKeys.HandBrakePresetName, handBrake.PresetName);
            await Set(SettingsKeys.HandBrakeDestDir, handBrake.DestDir);
            await Set(SettingsKeys.HandBrakeDestMode, handBrake.DestMode);
            if (handBrake.IncludeUnknown is { } includeUnknown)
            {
                await Set(SettingsKeys.TranscodeIncludeUnknown, includeUnknown);
            }
        }

        if (backup.LibraryAnalysis is { } library)
        {
            await Set(SettingsKeys.LibraryAnalysisAvgEpSizeMb, library.AvgEpSizeMb);
            await Set(SettingsKeys.LibraryAnalysisAvgGibPerHour, library.AvgGibPerHour);
        }

        return written;
    }

    // Only keys the app encrypts at rest need protection; legacy plaintext
    // passes through ISecretProtector.TryUnprotect unchanged.
    private string Unprotect(string key, string value) =>
        key == SettingsKeys.BotKunApiKey && secretProtector.TryUnprotect(value, out var plain)
            ? plain
            : value;

    private string Protect(string key, string value) =>
        key == SettingsKeys.BotKunApiKey && !string.IsNullOrEmpty(value)
            ? secretProtector.Protect(value)
            : value;
}
