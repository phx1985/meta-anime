namespace MetaAnime.Application.Contracts;

public sealed record AnimeDetailsResponse(
    string AniDbId,
    string Title,
    string? Description,
    PlexAnimeInfo[]? PlexAnimes,
    MalAnimeInfo[]? MalAnimes,
    SonarrAnimeInfo[]? SonarrAnimes
);
