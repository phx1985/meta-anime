namespace MetaAnime.Application.Contracts
{
    public sealed record AnimeDetailsResponse(
        string AniDbId,
        string Title,
        PlexAnimeInfo[]? PlexAnimes,
        MalAnimeInfo[]? MalAnimes,
        SonarrAnimeInfo[]? SonarrAnimes
    );
}
