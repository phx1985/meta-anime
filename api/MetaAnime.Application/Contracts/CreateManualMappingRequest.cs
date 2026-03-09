namespace MetaAnime.Application.Contracts;

public sealed record CreateManualMappingRequest(
    string AniDbId,
    string PlexSourceId,
    string PlexTitle,
    string MalId,
    string MalTitle
);
