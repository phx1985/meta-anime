namespace MetaAnime.Application.DTOs
{
    public sealed class GetAnimeDto
    {
        public required string AniDbId { get; set; }
        public required string Title { get; set; }
        public PlexAnimeDto[]? PlexAnimes { get; set; }
        public MalAnimeDto[]? MalAnimes { get; set; }
        public SonarrAnimeDto[]? SonarrAnimes { get; set; }
    }
}
