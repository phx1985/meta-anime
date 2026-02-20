using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Domain.Entities;
using System.Text.Json;

namespace MetaAnime.Infrastructure.Plex.Models;

public class PlexMedia : IAnimeConvertible, ICacheable
{
    #region Consts
    private const string TVDB_ID_PREFIX = "tvdb://";
    private const string TMDB_ID_PREFIX = "tmdb://";
    private const string IMDB_ID_PREFIX = "imdb://";
    #endregion

    #region Properties
    public required string Description { get; set; }
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string[] ExternalGuilds { get; set; } = [];
    public int ViewedLeafCount { get; set; }
    public int LeafCount { get; set; }
    #endregion

    #region IAnimeConvertible Implementation
    public Anime ToAnime()
    {
        return new Anime
        {
            SourceId = Id,
            Title = Title,
            Description = Description,
            AnimeSource = AnimeSource.Plex,
            ExternalAnimeIdentifiers = GetExternalAnimeIdentifiers()
        };
    }
    #endregion

    private List<ExternalAnimeIdentifier> GetExternalAnimeIdentifiers()
    {
        var identifiers = new List<ExternalAnimeIdentifier>();
        foreach (var guid in ExternalGuilds)
        {
            var identifier = guid switch
            {
                var g when g.StartsWith(TVDB_ID_PREFIX) => new ExternalAnimeIdentifier
                {
                    MediaPlatform = MediaPlatform.Tvdb,
                    MediaPlatformId = g.Replace(TVDB_ID_PREFIX, string.Empty)
                },
                var g when g.StartsWith(TMDB_ID_PREFIX) => new ExternalAnimeIdentifier
                {
                    MediaPlatform = MediaPlatform.Tmdb,
                    MediaPlatformId = g.Replace(TMDB_ID_PREFIX, string.Empty)
                },
                var g when g.StartsWith(IMDB_ID_PREFIX) => new ExternalAnimeIdentifier
                {
                    MediaPlatform = MediaPlatform.Imdb,
                    MediaPlatformId = g.Replace(IMDB_ID_PREFIX, string.Empty)
                },
                _ => null
            };

            if (identifier != null)
            {
                identifiers.Add(identifier);
            }
        }
        return identifiers;
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
            SourceId = Id,
            RawData = JsonSerializer.Serialize(this, options),
            AnimeSource = AnimeSource.Plex
        };
    }
}
