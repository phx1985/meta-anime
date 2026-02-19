using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Domain.Entities;
using System.Text.Json;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models
{
    public partial class SonarrSeries : IAnimeConvertible, ICacheable
    {
        public Anime ToAnime()
        {
            return new Anime
            {
                SourceId = Id.ToString(),
                Title = Title,
                Description = Overview ?? string.Empty,
                AnimeSource = AnimeSource.Sonarr,
                ExternalAnimeIdentifiers = GetExternalIdentifiers()
            };
        }

        private List<ExternalAnimeIdentifier> GetExternalIdentifiers()
        {
            var results = new List<ExternalAnimeIdentifier>();

            if (TvdbId != 0)
            {
                results.Add(new ExternalAnimeIdentifier
                {
                    MediaPlatform = MediaPlatform.Tvdb,
                    MediaPlatformId = TvdbId.ToString()
                });
            }

            if (TvMazeId != 0)
            {
                results.Add(new ExternalAnimeIdentifier
                {
                    MediaPlatform = MediaPlatform.TvMaze,
                    MediaPlatformId = TvMazeId.ToString()
                });
            }

            if (TvRageId != 0)
            {
                results.Add(new ExternalAnimeIdentifier
                {
                    MediaPlatform = MediaPlatform.TvRage,
                    MediaPlatformId = TvRageId.ToString()
                });
            }

            if (!string.IsNullOrEmpty(ImdbId))
            {
                results.Add(new ExternalAnimeIdentifier
                {
                    MediaPlatform = MediaPlatform.Imdb,
                    MediaPlatformId = ImdbId
                });
            }

            return results;
        }

        public CacheEntry ToCacheEntry()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
                // Don't use DefaultIgnoreCondition to ensure all fields are serialized, even if null
                // This prevents missing data when deserializing from cache
            };
            
            return new CacheEntry
            {
                SourceId = Id.ToString(),
                RawData = JsonSerializer.Serialize(this, options),
                AnimeSource = AnimeSource.Sonarr
            };
        }
    }
}
