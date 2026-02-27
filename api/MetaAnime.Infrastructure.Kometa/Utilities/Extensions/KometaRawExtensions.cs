using MetaAnime.Domain.Enums;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.Kometa.Models;
using MetaAnime.Infrastructure.Utilities.Extensions;

namespace MetaAnime.Infrastructure.Kometa.Utilities.Extensions;

public static class KometaRawExtensions
{
    public static IList<KometaAnimeMapping> ToKometaAnimeMappings(this IEnumerable<KometaRaw> kometaRawMappings)
        => [.. kometaRawMappings.SelectMany(kometaRawMapping => kometaRawMapping.ToKometaAnimeMappings())];


    public static IList<KometaAnimeMapping> ToKometaAnimeMappings(this KometaRaw kometaRawMappings)
        =>
        [
            .. kometaRawMappings.GetMappingsFromIds(KometaAnimePlatform.Imdb, kometaRawMappings.ImdbIds),
            .. kometaRawMappings.GetMappingsFromIds(KometaAnimePlatform.Mal, kometaRawMappings.MalIds),
            .. kometaRawMappings.GetMappingsFromIds(KometaAnimePlatform.Anilist, kometaRawMappings.AnilistIds),
            .. kometaRawMappings.GetMappingsFromIds(KometaAnimePlatform.TmdbMovie, kometaRawMappings.TmdbMovieIds),
            .. kometaRawMappings.GetMappingsFromIds(KometaAnimePlatform.TmdbShow, kometaRawMappings.TmdbShowIds),
            .. kometaRawMappings.GetMappingsFromIds(KometaAnimePlatform.Tvdb, kometaRawMappings.TvdbIds),
        ];

    private static IEnumerable<KometaAnimeMapping> GetMappingsFromIds(this KometaRaw kometaRawMapping, KometaAnimePlatform animePlatform, IEnumerable<string> platformIds)
        => platformIds?.Select(id => new KometaAnimeMapping
        {
            AniDbId = kometaRawMapping.AniDbId,
            PlatformId = id,
            PlatformName = animePlatform.GetPlatformName(),
            Season = kometaRawMapping.GetSeason(animePlatform),
            EpOffset = kometaRawMapping.GetEpOffset(animePlatform),
        }) ?? [];

    private static int? GetSeason(this KometaRaw kometaRawMapping, KometaAnimePlatform animePlatform)
    => animePlatform switch
    {
        KometaAnimePlatform.Tvdb => kometaRawMapping.TvdbSeason,
        _ => null
    };

    private static int? GetEpOffset(this KometaRaw kometaRawMapping, KometaAnimePlatform animePlatform)
    => animePlatform switch
    {
        KometaAnimePlatform.Tvdb => kometaRawMapping.TvdbEpOffset,
        _ => null
    };
}
