using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Contracts
{
    public sealed record MalAnimeInfo(
        string Title,
        string? Id,
        string? Url
    )
    {
        public MalAnimeInfo(Anime? anime, string malBaseUrl) 
            : this(
                anime?.Title ?? "NO_TITLE",
                anime?.SourceId,
                $"{malBaseUrl}/anime/{anime?.SourceId}"
            )
        { }
    }
}
