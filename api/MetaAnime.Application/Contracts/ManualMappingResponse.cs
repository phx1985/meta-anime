namespace MetaAnime.Application.Contracts;

public sealed record ManualMappingResponse(
    Guid Id,
    string PlexSourceId,
    string PlexTitle,
    string MalId,
    string MalTitle,
    string AniDbId,
    DateTime CreatedAt
);
