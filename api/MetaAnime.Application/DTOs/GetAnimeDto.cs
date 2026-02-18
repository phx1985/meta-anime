namespace MetaAnime.Application.DTOs
{
    public sealed record GetAnimeDto(
        string AniDbId,
        string Title,
        PlexAnimeDto[]? PlexAnimes,
        MalAnimeDto[]? MalAnimes,
        SonarrAnimeDto[]? SonarrAnimes
    );
}
