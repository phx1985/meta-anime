using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Domain.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

public partial class SonarrSeries : IAnimeConvertible, ICacheable
{
    /// <summary>
    /// Serializer options used for both <see cref="ToCacheEntry"/> (write) and the
    /// matching <see cref="AnimeMediatorService"/> (read).  A static instance avoids
    /// allocating a fresh options object – and its internal reflection cache – on
    /// every sync cycle.
    /// </summary>
    private static readonly JsonSerializerOptions CacheSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Serialize all fields; do NOT skip defaults/nulls so that every
        // Sonarr property (including "added") is preserved in the blob.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

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
        return new CacheEntry
        {
            SourceId = Id.ToString(),
            RawData = JsonSerializer.Serialize(this, CacheSerializerOptions),
            AnimeSource = AnimeSource.Sonarr
        };
    }
}
