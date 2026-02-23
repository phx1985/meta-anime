using System.Text.Json;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

namespace MetaAnime.Application.Contracts;

public sealed record SonarrAnimeInfo(
    string Title,
    string? Id,
    string? Url,
    bool Exists,
    SonarrSeasonInfo[]? SeasonInfo,
    Dictionary<string, int>? CodecDistribution,
    long? SizeOnDisk
)
{
    public SonarrAnimeInfo(Anime? anime, string sonarrBaseUrl)
        : this(
            Title: anime?.Title ?? "NO_TITLE",
            Id: anime?.SourceId,
            Url: $"{sonarrBaseUrl.TrimEnd('/')}/series/{anime?.SourceId}",
            Exists: false,
            SeasonInfo: null,
            CodecDistribution: null,
            SizeOnDisk: null
        )
    { }

    public SonarrAnimeInfo(SonarrSeries? sonarrSeries, string sonarrBaseUrl, Dictionary<string, int>? codecDistribution = null)
        : this(
            sonarrSeries?.Title ?? "NO_TITLE",
            sonarrSeries?.Id.ToString(),
            $"{sonarrBaseUrl.TrimEnd('/')}/series/{sonarrSeries?.TitleSlug ?? sonarrSeries?.Id.ToString()}",
            sonarrSeries != null,
            sonarrSeries?.Seasons?.Select(s => new SonarrSeasonInfo(
                s.SeasonNumber,
                s.Statistics?.EpisodeFileCount ?? 0,
                s.Statistics?.EpisodeCount ?? 0
            )).ToArray(),
            codecDistribution,
            CalculateTotalSizeOnDisk(sonarrSeries?.Seasons)
        )
    { }

    private static long? CalculateTotalSizeOnDisk(List<Season>? seasons)
    {
        if (seasons == null || seasons.Count == 0)
            return null;

        long totalSize = 0;
        bool hasAnySize = false;

        foreach (var season in seasons)
        {
            var seasonSize = ExtractSizeOnDisk(season.Statistics?.SizeOnDisk);
            if (seasonSize.HasValue)
            {
                totalSize += seasonSize.Value;
                hasAnySize = true;
            }
        }

        return hasAnySize ? totalSize : null;
    }

    private static long? ExtractSizeOnDisk(object? sizeOnDisk)
    {
        if (sizeOnDisk == null)
            return null;

        // Handle JsonElement type (when deserializing from System.Text.Json with object type)
        if (sizeOnDisk is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt64(out var longValue))
            {
                return longValue;
            }
            return null;
        }

        // Handle other possible types from Sonarr API
        return sizeOnDisk switch
        {
            long longValue => longValue,
            int intValue => (long)intValue,
            string strValue when long.TryParse(strValue, out var parsed) => parsed,
            _ => null
        };
    }
}

public sealed record SonarrSeasonInfo(
    int SeasonNumber,
    int AvailableEpisodes,
    int TotalEpisodes
);
