using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.DTOs
{
    public sealed record SonarrAnimeDto(
        string Title,
        string? Id,
        string? Url
    )
    {
        public SonarrAnimeDto(Anime? anime, string sonarrBaseUrl) 
            : this(
                anime?.Title ?? "NO_TITLE",
                anime?.SourceId,
                $"{sonarrBaseUrl}/series/{anime?.SourceId}"
            )
        { }
    }
}
