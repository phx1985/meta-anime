using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Contracts
{
    public sealed record SonarrAnimeInfo(
        string Title,
        string? Id,
        string? Url
    )
    {
        public SonarrAnimeInfo(Anime? anime, string sonarrBaseUrl) 
            : this(
                anime?.Title ?? "NO_TITLE",
                anime?.SourceId,
                $"{sonarrBaseUrl}/series/{anime?.SourceId}"
            )
        { }
    }
}
