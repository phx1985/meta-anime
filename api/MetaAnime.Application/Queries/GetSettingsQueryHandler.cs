using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public class GetSettingsQueryHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<GetSettingsQuery, Result<IEnumerable<SettingDefinition>>>
{
    public async Task<Result<IEnumerable<SettingDefinition>>> Handle(GetSettingsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var allEntries = (await propertyBagRepository.GetAllAsync())
                .ToDictionary(e => e.Key, e => e.Value);

            var definitions = BuildDefinitions(allEntries);
            return Result.Success(definitions);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<SettingDefinition>>(new(Constants.Errors.GetAllPropertyBag, ex.Message));
        }
    }

    private static IEnumerable<SettingDefinition> BuildDefinitions(Dictionary<string, string> bag)
    {
        string Get(string key) => bag.TryGetValue(key, out var v) ? v : string.Empty;
        string Mask(string key) => bag.ContainsKey(key) ? "••••••••" : string.Empty;

        return
        [
            // ── Plex ──────────────────────────────────────────────────────────
            new(SettingsKeys.PlexHost,             "plex",   "Plex Host URL",                 "Base URL of your Plex Media Server (e.g. http://localhost:32400).",      false, true,  Get(SettingsKeys.PlexHost)),
            new(SettingsKeys.PlexToken,            "plex",   "Plex Token",                    "Authentication token for your Plex account.",                            true,  true,  Mask(SettingsKeys.PlexToken)),
            new(SettingsKeys.PlexAnimeSections,    "plex",   "Anime Library Section IDs",     "Comma-separated Plex library section IDs that contain anime.",           false, true,  Get(SettingsKeys.PlexAnimeSections)),

            // ── Sonarr ────────────────────────────────────────────────────────
            new(SettingsKeys.SonarrHost,           "sonarr", "Sonarr Host URL",               "Base URL of your Sonarr instance (e.g. http://localhost:8989).",         false, true,  Get(SettingsKeys.SonarrHost)),
            new(SettingsKeys.SonarrApiKey,         "sonarr", "Sonarr API Key",                "API key found in Sonarr › Settings › General.",                          true,  true,  Mask(SettingsKeys.SonarrApiKey)),
            new(SettingsKeys.SonarrAnimeTags,      "sonarr", "Anime Tags",                    "Comma-separated Sonarr tag names used to identify anime series.",        false, true,  Get(SettingsKeys.SonarrAnimeTags)),
            new(SettingsKeys.SonarrRemoteUrl,      "sonarr", "Sonarr Remote URL",             "Optional public URL for Sonarr used to build series deep-links.",        false, false, Get(SettingsKeys.SonarrRemoteUrl)),

            // ── MyAnimeList ───────────────────────────────────────────────────
            new(SettingsKeys.MalApiToken,          "mal",    "MAL Access Token",              "Current OAuth access token for the MyAnimeList API.",                    true,  false, Mask(SettingsKeys.MalApiToken)),
            new(SettingsKeys.MalClientId,          "mal",    "MAL OAuth Client ID",           "OAuth Client ID registered on the MAL developer portal.",                false, false, Get(SettingsKeys.MalClientId)),
            new(SettingsKeys.MalClientSecret,      "mal",    "MAL OAuth Client Secret",       "OAuth Client Secret registered on the MAL developer portal.",            true,  false, Mask(SettingsKeys.MalClientSecret)),
            new(SettingsKeys.MalRefreshToken,      "mal",    "MAL Refresh Token",             "OAuth Refresh Token used to renew the access token automatically.",      true,  false, Mask(SettingsKeys.MalRefreshToken)),

            // ── Kometa ────────────────────────────────────────────────────────
            new(SettingsKeys.KometaAnimeIdsJsonUrl,"kometa", "Kometa Anime IDs JSON URL",     "URL of the Kometa Community anime_ids.json mapping file.",               false, true,  Get(SettingsKeys.KometaAnimeIdsJsonUrl)),
        ];
    }
}
