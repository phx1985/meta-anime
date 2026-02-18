using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Contracts
{
    public sealed record PlexAnimeInfo(
        string Title,
        string? Id,
        string? Url
    )
    {
        public PlexAnimeInfo(Anime? anime, string plexBaseUrl) 
            : this(
                anime?.Title ?? "NO_TITLE",
                anime?.SourceId,
                $"{plexBaseUrl}{anime?.SourceId}"
            )
        { }
    }
}
