using MetaAnime.Domain.Enums;

namespace MetaAnime.Application.DTOs
{
    public sealed record CreateAnimeRequest(
        string SourceId,
        string Title,
        string? Description,
        AnimeSource AnimeSource
    );
}
