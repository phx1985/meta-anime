using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.DTOs
{
    public sealed record MalAnimeDto(
        string Title,
        string? Id,
        string? Url
    )
    {
        public MalAnimeDto(Anime? anime, string malBaseUrl) 
            : this(
                anime?.Title ?? "NO_TITLE",
                anime?.SourceId,
                $"{malBaseUrl}/anime/{anime?.SourceId}"
            )
        { }
    }
}
