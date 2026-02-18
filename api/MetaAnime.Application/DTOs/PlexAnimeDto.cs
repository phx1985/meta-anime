using MetaAnime.Core.Models;

namespace MetaAnime.Application.DTOs
{
    public sealed record PlexAnimeDto(
        string Title,
        string? Id,
        string? Url
    )
    {
        public PlexAnimeDto(Anime? anime, string plexBaseUrl) 
            : this(
                anime?.Title ?? "NO_TITLE",
                anime?.SourceId,
                $"{plexBaseUrl}{anime?.SourceId}"
            )
        { }
    }
}
