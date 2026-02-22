namespace MetaAnime.Application.Contracts;

public sealed record AnimeStatisticsResponse(
    int PlexAnime,
    int MyAnimeListAnime,
    int SonarrAnime,
    int MatchedAnime,
    int SonarrNotInMal,
    int MalNotInPlex,
    int PlexNotInMal,
    int PlexNotInSonarr,
    Dictionary<string, int> GenreStats,
    ViewingProgressStats ViewingProgress
);

public sealed record ViewingProgressStats(
    int MalEpisodesWatched,
    int MalTotalEpisodes,
    int PlexEpisodesWatched,
    int PlexTotalEpisodes
);
