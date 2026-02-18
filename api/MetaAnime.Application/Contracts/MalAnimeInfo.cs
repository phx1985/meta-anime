using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.MyAnimeList.Models;

namespace MetaAnime.Application.Contracts
{
    public sealed record MalAnimeInfo(
        string Title,
        string? Id,
        string? Url,
        string? ViewState,
        DateTime? AddedDate,
        MalProgressionInfo? Progression,
        MalRatingInfo? Rating
    )
    {
        public MalAnimeInfo(Anime? anime, string malBaseUrl) 
            : this(
                anime?.Title ?? "NO_TITLE",
                anime?.SourceId,
                $"{malBaseUrl}/anime/{anime?.SourceId}",
                null,
                null,
                null,
                null
            )
        { }

        public MalAnimeInfo(MalAnime? malAnime, string malBaseUrl)
            : this(
                malAnime?.Title ?? "NO_TITLE",
                malAnime?.Id.ToString(),
                $"{malBaseUrl}/anime/{malAnime?.Id}",
                malAnime?.MyListStatus?.Status,
                malAnime?.MyListStatus?.CreatedAt,
                malAnime != null ? new MalProgressionInfo(
                    malAnime.MyListStatus?.NumEpisodesWatched ?? 0,
                    malAnime.NumEpisodes
                ) : null,
                malAnime != null ? new MalRatingInfo(
                    malAnime.MyListStatus?.Score ?? 0,
                    malAnime.Mean
                ) : null
            )
        { }
    }

    public sealed record MalProgressionInfo(
        int EpisodesWatched,
        int TotalEpisodes
    );

    public sealed record MalRatingInfo(
        int MyScore,
        double MalMean
    );
}
