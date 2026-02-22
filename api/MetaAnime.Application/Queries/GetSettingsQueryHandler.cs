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

        return
        [
            // ── Plex ──────────────────────────────────────────────────────────
            new(SettingsKeys.PlexHost,              "plex",   false, true,  Get(SettingsKeys.PlexHost)),
            new(SettingsKeys.PlexToken,             "plex",   true,  true,  Get(SettingsKeys.PlexToken)),
            new(SettingsKeys.PlexAnimeSections,     "plex",   false, true,  Get(SettingsKeys.PlexAnimeSections)),

            // ── Sonarr ────────────────────────────────────────────────────────
            new(SettingsKeys.SonarrHost,            "sonarr", false, true,  Get(SettingsKeys.SonarrHost)),
            new(SettingsKeys.SonarrApiKey,          "sonarr", true,  true,  Get(SettingsKeys.SonarrApiKey)),
            new(SettingsKeys.SonarrAnimeTags,       "sonarr", false, true,  Get(SettingsKeys.SonarrAnimeTags)),
            new(SettingsKeys.SonarrRemoteUrl,       "sonarr", false, false, Get(SettingsKeys.SonarrRemoteUrl)),

            // ── MyAnimeList ───────────────────────────────────────────────────
            new(SettingsKeys.MalApiToken,           "mal",    true,  false, Get(SettingsKeys.MalApiToken)),
            new(SettingsKeys.MalClientId,           "mal",    false, false, Get(SettingsKeys.MalClientId)),
            new(SettingsKeys.MalClientSecret,       "mal",    true,  false, Get(SettingsKeys.MalClientSecret)),
            new(SettingsKeys.MalRefreshToken,       "mal",    true,  false, Get(SettingsKeys.MalRefreshToken)),

            // ── Kometa ────────────────────────────────────────────────────────
            new(SettingsKeys.KometaAnimeIdsJsonUrl, "kometa", false, true,  Get(SettingsKeys.KometaAnimeIdsJsonUrl)),

            // ── App preferences ───────────────────────────────────────────────
            new(SettingsKeys.AppShowNsfw, "app", false, false, Get(SettingsKeys.AppShowNsfw), "boolean"),
        ];
    }
}
