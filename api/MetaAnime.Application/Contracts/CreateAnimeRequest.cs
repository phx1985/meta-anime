using MetaAnime.Domain.Enums;

namespace MetaAnime.Application.Contracts;

public sealed record CreateAnimeRequest(
    string SourceId,
    string Title,
    string? Description,
    AnimeSource AnimeSource
);
