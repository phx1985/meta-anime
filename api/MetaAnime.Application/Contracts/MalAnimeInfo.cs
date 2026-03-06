using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.MyAnimeList.Models;

namespace MetaAnime.Application.Contracts;

public sealed record MalAnimeInfo(
    string Title,
    string? Id,
    string? Url,
    string? ViewState,
    DateTime? AddedDate,
    MalProgressionInfo? Progression,
    MalRatingInfo? Rating,
    string[]? Genres,
    string? Synopsis,
    string? StartDate,
    string? EndDate,
    MalAlternativeTitles? AlternativeTitles,
    string? CoverImageUrl,
    string? Status,
    string? MediaType,
    string? TrailerUrl,
    int? Popularity,
    int? Rank,
    string[]? Pictures
)
{
    public MalAnimeInfo(Anime? anime, string malBaseUrl)
        : this(
            Title: anime?.Title ?? "NO_TITLE",
            Id: anime?.SourceId,
            Url: $"{malBaseUrl}/anime/{anime?.SourceId}",
            ViewState: null,
            AddedDate: null,
            Progression: null,
            Rating: null,
            Genres: null,
            Synopsis: anime?.Description,
            StartDate: null,
            EndDate: null,
            AlternativeTitles: null,
            CoverImageUrl: null,
            Status: null,
            MediaType: null,
            TrailerUrl: null,
            Popularity: null,
            Rank: null,
            Pictures: null
        )
    { }

    public MalAnimeInfo(MalAnime? malAnime, string malBaseUrl)
        : this(
            malAnime?.Title ?? "NO_TITLE",
            malAnime?.Id.ToString(),
            $"{malBaseUrl}/anime/{malAnime?.Id}",
            malAnime?.MyListStatus?.Status,
            malAnime?.MyListStatus is { } ls && ls.CreatedAt.Year > 1 ? ls.CreatedAt : (DateTime?)null,
            malAnime != null ? new MalProgressionInfo(
                malAnime.MyListStatus?.NumEpisodesWatched ?? 0,
                malAnime.NumEpisodes
            ) : null,
            malAnime != null ? new MalRatingInfo(
                malAnime.MyListStatus?.Score ?? 0,
                malAnime.Mean
            ) : null,
            malAnime?.Genres?.Select(g => g.Name).ToArray(),
            malAnime?.Synopsis,
            malAnime?.StartDate,
            malAnime?.EndDate,
            malAnime?.AlternativeTitles != null
                ? new MalAlternativeTitles(
                    malAnime.AlternativeTitles.En,
                    malAnime.AlternativeTitles.Ja,
                    malAnime.AlternativeTitles.Synonyms?.ToArray())
                : null,
            malAnime?.MainPicture?.Large ?? malAnime?.MainPicture?.Medium,
            malAnime?.Status,
            malAnime?.MediaType,
            malAnime?.Trailer?.YoutubeId is { } ytId ? $"https://www.youtube-nocookie.com/embed/{ytId}" : null,
            malAnime?.Popularity > 0 ? malAnime.Popularity : (int?)null,
            malAnime?.Rank > 0 ? malAnime.Rank : (int?)null,
            malAnime?.Pictures?
                .Select(p => p.Large ?? p.Medium)
                .Where(url => url != null)
                .Select(url => url!)
                .ToArray()
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

public sealed record MalAlternativeTitles(
    string? En,
    string? Ja,
    string[]? Synonyms
);
