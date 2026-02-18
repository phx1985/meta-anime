using MetaAnime.Domain.Enums;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Infrastructure.Utilities.Extensions
{
    public static class KometaAnimeMappingExtensions
    {
        /// <summary>
        /// Get the platform name from the platform enum value
        /// </summary>
        /// <param name="platform">Represent the platform enum value</param>
        /// <returns>Returns the platform name</returns>
        /// <exception cref="NotImplementedException"></exception>
        public static string GetPlatformName(this KometaAnimePlatform platform)
        {
            return platform switch
            {
                KometaAnimePlatform.Tvdb => "tvdb_id",
                KometaAnimePlatform.Imdb => "imdb_id",
                KometaAnimePlatform.Mal => "mal_id",
                KometaAnimePlatform.Anilist => "anilist_id",
                KometaAnimePlatform.TmdbMovie => "tmdb_movie_id",
                KometaAnimePlatform.TmdbShow => "tmdb_show_id",
                _ => throw new NotImplementedException()
            };
        }

        /// <summary>
        /// Get the platform enum value from the platform name
        /// </summary>
        /// <param name="kometaAnimeMapping">Instance of KometaAnimeMapping</param>
        /// <returns>Returns the platform enum value</returns>
        /// <exception cref="NotImplementedException"></exception>
        public static KometaAnimePlatform GetPlatformEnum(this KometaAnimeMapping kometaAnimeMapping)
        {
            return kometaAnimeMapping.PlatformName switch
            {
                "tvdb_id" => KometaAnimePlatform.Tvdb,
                "imdb_id" => KometaAnimePlatform.Imdb,
                "mal_id" => KometaAnimePlatform.Mal,
                "anilist_id" => KometaAnimePlatform.Anilist,
                "tmdb_movie_id" => KometaAnimePlatform.TmdbMovie,
                "tmdb_show_id" => KometaAnimePlatform.TmdbShow,
                _ => throw new NotImplementedException()
            };
        }
    }
}