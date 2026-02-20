using MetaAnime.Domain.Enums;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Contracts;

public sealed record AnimeResponse(
    Guid Id,
    string SourceId,
    string Title,
    string? Description,
    AnimeSource AnimeSource
)
{
    public static AnimeResponse FromAnime(Anime anime)
    {
        return new AnimeResponse(
            anime.Id,
            anime.SourceId,
            anime.Title,
            anime.Description,
            anime.AnimeSource
        );
    }
}
