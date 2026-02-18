using MetaAnime.Domain.Enums;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.DTOs
{
    public sealed record AnimeDto(
        Guid Id,
        string SourceId,
        string Title,
        string? Description,
        AnimeSource AnimeSource
    )
    {
        public static AnimeDto FromAnime(Anime anime)
        {
            return new AnimeDto(
                anime.Id,
                anime.SourceId,
                anime.Title,
                anime.Description,
                anime.AnimeSource
            );
        }
    }
}
