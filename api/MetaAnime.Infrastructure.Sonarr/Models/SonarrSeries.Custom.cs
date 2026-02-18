using MetaAnime.Core.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Models;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models
{
    public partial class SonarrSeries : IAnimeConvertible
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
    }
}
