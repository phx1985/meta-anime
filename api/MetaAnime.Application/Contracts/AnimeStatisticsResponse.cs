namespace MetaAnime.Application.Contracts;

public sealed record AnimeStatisticsResponse(
    int PlexAnime,
    int MyAnimeListAnime,
    int SonarrAnime,
    int MatchedAnime,
    Dictionary<string, int> GenreStats,
    ViewingProgressStats ViewingProgress
);

public sealed record ViewingProgressStats(
    int MalEpisodesWatched,
    int MalTotalEpisodes,
    int PlexEpisodesWatched,
    int PlexTotalEpisodes
);
