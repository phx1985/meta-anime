using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Domain.Entities;
using System.Text.Json;

namespace MetaAnime.Infrastructure.MyAnimeList.Models
{
    public partial class MalAnime : IAnimeConvertible, ICacheable
    {
        public Anime ToAnime()
        {
            return new Anime
            {
                SourceId = Id.ToString(),
                Title = Title,
                Description = Synopsis ?? string.Empty,
                AnimeSource = AnimeSource.MyAnimeList,
                ExternalAnimeIdentifiers = []
            };
        }

        public CacheEntry ToCacheEntry()
        {
            return new CacheEntry
            {
                SourceId = Id.ToString(),
                RawData = JsonSerializer.Serialize(this),
                AnimeSource = AnimeSource.MyAnimeList
            };
        }
    }
}
