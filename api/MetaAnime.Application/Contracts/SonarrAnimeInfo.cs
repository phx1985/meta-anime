using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

namespace MetaAnime.Application.Contracts
{
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
                anime?.Title ?? "NO_TITLE",
                anime?.SourceId,
                $"{sonarrBaseUrl}/series/{anime?.SourceId}",
                false,
                null,
                null,
                null
            )
        { }

        public SonarrAnimeInfo(SonarrSeries? sonarrSeries, string sonarrBaseUrl, Dictionary<string, int>? codecDistribution = null)
            : this(
                sonarrSeries?.Title ?? "NO_TITLE",
                sonarrSeries?.Id.ToString(),
                $"{sonarrBaseUrl}/series/{sonarrSeries?.Id}",
                sonarrSeries != null,
                sonarrSeries?.Seasons?.Select(s => new SonarrSeasonInfo(
                    s.SeasonNumber,
                    s.Statistics?.EpisodeFileCount ?? 0,
                    s.Statistics?.EpisodeCount ?? 0
                )).ToArray(),
                codecDistribution,
                ExtractSizeOnDisk(sonarrSeries?.Statistics?.SizeOnDisk)
            )
        { }

        private static long? ExtractSizeOnDisk(object? sizeOnDisk)
        {
            if (sizeOnDisk == null)
                return null;

            // Handle different possible types from Sonarr API
            return sizeOnDisk switch
            {
                long longValue => longValue,
                int intValue => intValue,
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
}
